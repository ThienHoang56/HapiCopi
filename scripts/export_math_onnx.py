import os
import sys
import math
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import torch
import torch.nn as nn
import torch.optim as optim

VOCABULARY = [
    "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
    "x", "y", "z", "a", "b", "c", "d", "e", "f", "g",
    "k", "m", "n", "p", "q", "r", "s", "t", "u", "v",
    "+", "-", "=", "\\times", "/", "\\pm", "<", ">",
    "\\le", "\\ge", "\\neq", "(", ")", "[", "]",
    "\\alpha", "\\beta", "\\gamma", "\\theta", "\\pi",
    "\\sqrt", "\\int", "\\sum", "\\infty"
]

GLYPH_MAP = {
    "\\times": "×",
    "\\pm": "±",
    "\\le": "≤",
    "\\ge": "≥",
    "\\neq": "≠",
    "\\alpha": "α",
    "\\beta": "β",
    "\\gamma": "γ",
    "\\theta": "θ",
    "\\pi": "π",
    "\\sqrt": "√",
    "\\int": "∫",
    "\\sum": "∑",
    "\\infty": "∞"
}

def get_char_for_label(label):
    return GLYPH_MAP.get(label, label)

class MathNet(nn.Module):
    def __init__(self, num_classes=54):
        super().__init__()
        self.features = nn.Sequential(
            nn.Conv2d(1, 16, kernel_size=3, padding=1),
            nn.BatchNorm2d(16),
            nn.ReLU(inplace=True),
            nn.MaxPool2d(2, 2),  # 16x16

            nn.Conv2d(16, 32, kernel_size=3, padding=1),
            nn.BatchNorm2d(32),
            nn.ReLU(inplace=True),
            nn.MaxPool2d(2, 2),  # 8x8

            nn.Conv2d(32, 64, kernel_size=3, padding=1),
            nn.BatchNorm2d(64),
            nn.ReLU(inplace=True),
            nn.AdaptiveAvgPool2d((4, 4))  # 4x4
        )
        self.classifier = nn.Sequential(
            nn.Linear(64 * 4 * 4, 128),
            nn.ReLU(inplace=True),
            nn.Dropout(0.15),
            nn.Linear(128, num_classes)
        )

    def forward(self, x):
        feat = self.features(x)
        flat = feat.view(feat.size(0), -1)
        return self.classifier(flat)

def render_base_glyph(char, font, size=64):
    img = Image.new("L", (size, size), color=0)
    draw = ImageDraw.Draw(img)
    # Get bounding box
    bbox = draw.textbbox((0, 0), char, font=font)
    w = max(1, bbox[2] - bbox[0])
    h = max(1, bbox[3] - bbox[1])
    x = (size - w) // 2 - bbox[0]
    y = (size - h) // 2 - bbox[1]
    draw.text((x, y), char, fill=255, font=font)
    return img

def augment_and_resize(img, target_size=32):
    # Random rotation
    angle = random.uniform(-14, 14)
    rot = img.rotate(angle, resample=Image.Resampling.BILINEAR, expand=False)
    
    # Bounding box crop & center
    bbox = rot.getbbox()
    if bbox is None:
        bbox = img.getbbox()
        rot = img
    if bbox is None:
        return np.zeros((target_size, target_size), dtype=np.float32)

    cropped = rot.crop(bbox)
    cw, ch = cropped.size
    max_dim = max(cw, ch)

    # Scale to fit (target_size - 8)
    draw_area = target_size - 8
    scale = draw_area / max(1, max_dim)
    new_w = max(1, int(round(cw * scale)))
    new_h = max(1, int(round(ch * scale)))
    
    resized = cropped.resize((new_w, new_h), resample=Image.Resampling.BILINEAR)

    # Place in target canvas with slight jitter
    canvas = Image.new("L", (target_size, target_size), color=0)
    jitter_x = random.randint(-1, 1)
    jitter_y = random.randint(-1, 1)
    off_x = max(0, min(target_size - new_w, (target_size - new_w) // 2 + jitter_x))
    off_y = max(0, min(target_size - new_h, (target_size - new_h) // 2 + jitter_y))
    canvas.paste(resized, (off_x, off_y))

    # Convert to float numpy array [0, 1]
    arr = np.array(canvas, dtype=np.float32) / 255.0
    return arr

def main():
    print("[1/5] Loading Windows fonts...")
    font_candidates = [
        "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/calibri.ttf",
        "C:/Windows/Fonts/cambria.ttc"
    ]
    fonts = []
    for fp in font_candidates:
        if os.path.exists(fp):
            try:
                fonts.append(ImageFont.truetype(fp, 44))
            except Exception:
                pass

    if not fonts:
        fonts.append(ImageFont.load_default())

    print(f"Loaded {len(fonts)} fonts.")

    print("[2/5] Generating synthetic training samples...")
    images = []
    labels = []
    samples_per_class = 60

    for idx, vocab_item in enumerate(VOCABULARY):
        char = get_char_for_label(vocab_item)
        for s in range(samples_per_class):
            chosen_font = random.choice(fonts)
            base = render_base_glyph(char, chosen_font, size=64)
            arr = augment_and_resize(base, target_size=32)
            images.append(arr)
            labels.append(idx)

    X = np.expand_dims(np.array(images, dtype=np.float32), axis=1) # [N, 1, 32, 32]
    Y = np.array(labels, dtype=np.int64)

    dataset_size = len(Y)
    indices = np.arange(dataset_size)
    np.random.shuffle(indices)
    X = torch.tensor(X[indices])
    Y = torch.tensor(Y[indices])
    print(f"Generated {dataset_size} total samples across {len(VOCABULARY)} math classes.")

    print("[3/5] Training lightweight MathNet...")
    model = MathNet(num_classes=len(VOCABULARY))
    criterion = nn.CrossEntropyLoss()
    optimizer = optim.Adam(model.parameters(), lr=0.003, weight_decay=1e-4)

    batch_size = 64
    epochs = 18
    model.train()

    for epoch in range(epochs):
        perm = torch.randperm(dataset_size)
        total_loss = 0.0
        correct = 0
        batches = 0

        for i in range(0, dataset_size, batch_size):
            batch_idx = perm[i:i + batch_size]
            bx = X[batch_idx]
            by = Y[batch_idx]

            optimizer.zero_grad()
            out = model(bx)
            loss = criterion(out, by)
            loss.backward()
            optimizer.step()

            total_loss += loss.item()
            preds = out.argmax(dim=1)
            correct += (preds == by).sum().item()
            batches += 1

        acc = (correct / dataset_size) * 100.0
        if (epoch + 1) % 6 == 0 or epoch == epochs - 1:
            print(f"  Epoch {epoch+1:2d}/{epochs:2d} - Loss: {total_loss/batches:.4f} - Accuracy: {acc:.1f}%")

    model.eval()

    sys.stdout.reconfigure(encoding='utf-8')
    output_dir = os.path.join(os.path.dirname(__file__), "..", "models")
    os.makedirs(output_dir, exist_ok=True)
    onnx_path = os.path.join(output_dir, "math_rec.onnx")

    print("[4/5] Exporting ONNX model to models/math_rec.onnx...")
    dummy_input = torch.randn(1, 1, 32, 32, dtype=torch.float32)

    torch.onnx.export(
        model,
        dummy_input,
        onnx_path,
        export_params=True,
        opset_version=14,
        do_constant_folding=True,
        dynamo=False,
        input_names=["input"],
        output_names=["output"]
    )

    file_size_kb = os.path.getsize(onnx_path) / 1024.0
    print(f"Exported successfully! File size: {file_size_kb:.1f} KB (Self-contained, no external data)")

    # Sync to LocalAppData as well
    appdata_dir = os.path.join(os.environ.get("LOCALAPPDATA", ""), "HapiCopi", "models")
    if os.path.exists(appdata_dir):
        import shutil
        target_file = os.path.join(appdata_dir, "math_rec.onnx")
        shutil.copy2(onnx_path, target_file)
        # Remove any stale .data file if existed
        stale_data = os.path.join(appdata_dir, "math_rec.onnx.data")
        if os.path.exists(stale_data):
            os.remove(stale_data)
        print(f"Synced model directly to LocalAppData: {target_file}")

    print("[5/5] Verifying ONNX model with onnxruntime...")
    import onnxruntime as ort
    sess = ort.InferenceSession(onnx_path)
    test_in = np.random.randn(1, 1, 32, 32).astype(np.float32)
    res = sess.run(None, {"input": test_in})
    print(f"Verification output shape: {res[0].shape}. Ready for deployment!")

if __name__ == "__main__":
    main()
