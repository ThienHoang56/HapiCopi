import os
import sys
import json
import random
import glob
import shutil

# Reconfigure stdout/stderr to utf-8 on Windows
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

import numpy as np
from PIL import Image, ImageOps
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader

# 82 Ký hiệu toán học viết tay từ bộ dữ liệu CROHME (chuẩn hóa toàn diện cho đại số, giải tích, hình học)
CLASS_TO_LATEX = {
    "!": "!",
    "(": "(",
    ")": ")",
    "+": "+",
    ",": ",",
    "-": "-",
    "0": "0", "1": "1", "2": "2", "3": "3", "4": "4",
    "5": "5", "6": "6", "7": "7", "8": "8", "9": "9",
    "=": "=",
    "A": "A",
    "C": "C",
    "Delta": "\\Delta",
    "G": "G",
    "H": "H",
    "M": "M",
    "N": "N",
    "R": "R",
    "S": "S",
    "T": "T",
    "X": "x",
    "[": "[",
    "]": "]",
    "alpha": "\\alpha",
    "ascii_124": "|",
    "b": "b",
    "beta": "\\beta",
    "cos": "\\cos",
    "d": "d",
    "div": "\\div",
    "e": "e",
    "exists": "\\exists",
    "f": "f",
    "forall": "\\forall",
    "forward_slash": "/",
    "gamma": "\\gamma",
    "geq": "\\ge",
    "gt": ">",
    "i": "i",
    "in": "\\in",
    "infty": "\\infty",
    "int": "\\int",
    "j": "j",
    "k": "k",
    "l": "l",
    "lambda": "\\lambda",
    "ldots": "\\dots",
    "leq": "\\le",
    "lim": "\\lim",
    "log": "\\log",
    "lt": "<",
    "mu": "\\mu",
    "neq": "\\neq",
    "o": "o",
    "p": "p",
    "phi": "\\phi",
    "pi": "\\pi",
    "pm": "\\pm",
    "prime": "'",
    "q": "q",
    "rightarrow": "\\to",
    "sigma": "\\sigma",
    "sin": "\\sin",
    "sqrt": "\\sqrt",
    "sum": "\\sum",
    "tan": "\\tan",
    "theta": "\\theta",
    "times": "\\times",
    "u": "u",
    "v": "v",
    "w": "w",
    "y": "y",
    "z": "z",
    "{": "\\{",
    "}": "\\}"
}

CLASSES = sorted(list(CLASS_TO_LATEX.keys()))
LATEX_VOCABULARY = [CLASS_TO_LATEX[c] for c in CLASSES]


class MathNetA2(nn.Module):
    """
    Kiến trúc CNN nâng cấp Phương án A2:
    - 3 khối Conv2d mở rộng kênh (32 -> 64 -> 128)
    - BatchNorm2d + ReLU + MaxPool2d
    - AdaptiveAvgPool2d((4, 4))
    - FC 128 với Dropout 0.25 chống overfit
    - FC đầu ra: 82 classes
    - Kích thước xuất ra ONNX: ~1.45 MB
    - Độ trễ CPU: ~0.8 - 1.2 ms
    """
    def __init__(self, num_classes=82):
        super().__init__()
        self.features = nn.Sequential(
            # Block 1: 1 -> 32 (32x32 -> 16x16)
            nn.Conv2d(1, 32, kernel_size=3, padding=1),
            nn.BatchNorm2d(32),
            nn.ReLU(inplace=True),
            nn.MaxPool2d(2, 2),

            # Block 2: 32 -> 64 (16x16 -> 8x8)
            nn.Conv2d(32, 64, kernel_size=3, padding=1),
            nn.BatchNorm2d(64),
            nn.ReLU(inplace=True),
            nn.MaxPool2d(2, 2),

            # Block 3: 64 -> 128 (8x8 -> 4x4)
            nn.Conv2d(64, 128, kernel_size=3, padding=1),
            nn.BatchNorm2d(128),
            nn.ReLU(inplace=True),
            nn.AdaptiveAvgPool2d((4, 4))
        )
        self.classifier = nn.Sequential(
            nn.Dropout(0.2),
            nn.Linear(128 * 4 * 4, 128),
            nn.BatchNorm1d(128),
            nn.ReLU(inplace=True),
            nn.Dropout(0.25),
            nn.Linear(128, num_classes)
        )

    def forward(self, x):
        feat = self.features(x)
        flat = feat.view(feat.size(0), -1)
        return self.classifier(flat)


def preprocess_image(img, target_size=32, augment=False):
    """
    Chuẩn hóa ảnh nét viết tay khớp hoàn toàn với pipeline C# RasterizeStrokesToTensor:
    1. Nghịch đảo màu: ảnh gốc là nét đen/nền trắng -> chuyển thành nét trắng/nền đen (giống canvas vẽ).
    2. Crop vùng bao nét (bounding box).
    3. Scale vừa vặn vùng vẽ target_size - 8 (24x24), lề 4px chống méo.
    4. Căn giữa trong canvas đen 32x32.
    5. Trả về mảng float32 [0.0, 1.0].
    """
    # Đảm bảo ảnh xám
    gray = img.convert("L")
    
    # Nghịch đảo: Nét trắng (255), Nền đen (0)
    inv = ImageOps.invert(gray)

    # Nếu có augmentation nhẹ
    if augment:
        angle = random.uniform(-10, 10)
        inv = inv.rotate(angle, resample=Image.Resampling.BILINEAR, expand=False)

    # Crop bounding box
    bbox = inv.getbbox()
    if bbox is None:
        return np.zeros((target_size, target_size), dtype=np.float32)

    cropped = inv.crop(bbox)
    cw, ch = cropped.size
    max_dim = max(cw, ch)

    # Scale to fit (target_size - 8) = 24px
    draw_area = target_size - 8
    scale = draw_area / max(1, max_dim)
    new_w = max(1, int(round(cw * scale)))
    new_h = max(1, int(round(ch * scale)))

    resized = cropped.resize((new_w, new_h), resample=Image.Resampling.BILINEAR)

    # Tạo canvas đen 32x32
    canvas = Image.new("L", (target_size, target_size), color=0)
    jitter_x = random.randint(-1, 1) if augment else 0
    jitter_y = random.randint(-1, 1) if augment else 0
    off_x = max(0, min(target_size - new_w, (target_size - new_w) // 2 + jitter_x))
    off_y = max(0, min(target_size - new_h, (target_size - new_h) // 2 + jitter_y))
    canvas.paste(resized, (off_x, off_y))

    # Float array [0, 1]
    arr = np.array(canvas, dtype=np.float32) / 255.0
    return arr


class MathDataset(Dataset):
    def __init__(self, images, labels):
        self.images = torch.tensor(images, dtype=torch.float32)
        self.labels = torch.tensor(labels, dtype=torch.long)

    def __len__(self):
        return len(self.labels)

    def __getitem__(self, idx):
        return self.images[idx], self.labels[idx]


def main():
    print("=" * 70)
    print("🚀 HUẤN LUYỆN MODEL AI TOÁN HỌC HAPICOPI - PHƯƠNG ÁN A2 (CROHME 82 CLASSES)")
    print("=" * 70)

    # 1. Tìm đường dẫn dataset đã trích xuất
    cache_base = r"C:\Users\ThienNLH3\.cache\kagglehub\datasets\xainano\handwrittenmathsymbols\versions\2\extracted_images"
    if not os.path.exists(cache_base):
        print(f"Lỗi: Không tìm thấy thư mục dataset tại {cache_base}")
        sys.exit(1)

    print(f"[1/5] Kiểm tra {len(CLASSES)} thư mục lớp ký hiệu...")
    valid_classes = []
    for cls in CLASSES:
        cls_dir = os.path.join(cache_base, cls)
        if os.path.exists(cls_dir):
            valid_classes.append(cls)
        else:
            print(f"  ⚠️ Cảnh báo: Thiếu thư mục {cls}")

    print(f"  -> Đã tìm thấy {len(valid_classes)} / {len(CLASSES)} lớp ký hiệu hợp lệ.")

    # 2. Đọc và tiền xử lý dữ liệu
    print("[2/5] Đọc dữ liệu viết tay thật & cân bằng mẫu (Max 600 mẫu/lớp)...")
    images = []
    labels = []
    max_samples_per_class = 600
    min_samples_per_class = 400

    for idx, cls in enumerate(valid_classes):
        cls_dir = os.path.join(cache_base, cls)
        all_files = glob.glob(os.path.join(cls_dir, "*.jpg")) + glob.glob(os.path.join(cls_dir, "*.png"))
        if not all_files:
            continue

        # Chọn mẫu ngẫu nhiên nếu có nhiều
        if len(all_files) > max_samples_per_class:
            chosen_files = random.sample(all_files, max_samples_per_class)
        else:
            chosen_files = list(all_files)

        # Đọc các file gốc
        cls_samples = []
        for fp in chosen_files:
            try:
                with Image.open(fp) as img:
                    arr = preprocess_image(img, target_size=32, augment=False)
                    cls_samples.append(arr)
            except Exception:
                pass

        # Nếu lớp có ít mẫu hơn min_samples_per_class, bổ sung thêm bằng data augmentation
        original_count = len(cls_samples)
        if original_count > 0 and original_count < min_samples_per_class:
            needed = min_samples_per_class - original_count
            for _ in range(needed):
                fp = random.choice(chosen_files)
                try:
                    with Image.open(fp) as img:
                        arr = preprocess_image(img, target_size=32, augment=True)
                        cls_samples.append(arr)
                except Exception:
                    pass

        for arr in cls_samples:
            images.append(arr)
            labels.append(idx)

        if (idx + 1) % 15 == 0 or idx == len(valid_classes) - 1:
            print(f"  Đã nạp {idx+1:2d}/{len(valid_classes):2d} lớp ({cls}: {len(cls_samples)} mẫu)...")

    # Gom thành mảng NumPy
    X = np.expand_dims(np.array(images, dtype=np.float32), axis=1) # [N, 1, 32, 32]
    Y = np.array(labels, dtype=np.int64)

    total_samples = len(Y)
    print(f"\n  -> Tổng số mẫu huấn luyện: {total_samples:,} mẫu ({total_samples * 1024 / 1024 / 1024:.1f} MB RAM).")

    # Phân chia Train (90%) / Validation (10%)
    indices = np.arange(total_samples)
    np.random.seed(42)
    np.random.shuffle(indices)

    split_idx = int(0.9 * total_samples)
    train_idx = indices[:split_idx]
    val_idx = indices[split_idx:]

    train_dataset = MathDataset(X[train_idx], Y[train_idx])
    val_dataset = MathDataset(X[val_idx], Y[val_idx])

    train_loader = DataLoader(train_dataset, batch_size=128, shuffle=True)
    val_loader = DataLoader(val_dataset, batch_size=256, shuffle=False)

    print(f"  -> Tập Train: {len(train_dataset):,} mẫu | Tập Validation: {len(val_dataset):,} mẫu.")

    # 3. Khởi tạo mô hình MathNetA2
    print(f"\n[3/5] Khởi tạo mô hình MathNetA2 ({len(valid_classes)} classes)...")
    model = MathNetA2(num_classes=len(valid_classes))
    total_params = sum(p.numel() for p in model.parameters() if p.requires_grad)
    print(f"  -> Tổng tham số: {total_params:,} (~{total_params * 4 / 1024 / 1024:.2f} MB FP32)")

    criterion = nn.CrossEntropyLoss(label_smoothing=0.05)
    optimizer = optim.AdamW(model.parameters(), lr=0.002, weight_decay=1e-4)
    epochs = 14
    scheduler = optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=epochs)

    print(f"\nBắt đầu huấn luyện {epochs} Epochs trên CPU...")
    best_val_acc = 0.0
    best_model_state = None

    for epoch in range(epochs):
        model.train()
        train_loss = 0.0
        train_correct = 0
        train_total = 0

        for bx, by in train_loader:
            optimizer.zero_grad()
            out = model(bx)
            loss = criterion(out, by)
            loss.backward()
            optimizer.step()

            train_loss += loss.item() * len(by)
            train_correct += (out.argmax(dim=1) == by).sum().item()
            train_total += len(by)

        scheduler.step()
        train_acc = (train_correct / train_total) * 100.0

        # Đánh giá trên Validation
        model.eval()
        val_correct = 0
        val_total = 0
        val_loss = 0.0

        with torch.no_grad():
            for vx, vy in val_loader:
                vout = model(vx)
                vloss = criterion(vout, vy)
                val_loss += vloss.item() * len(vy)
                val_correct += (vout.argmax(dim=1) == vy).sum().item()
                val_total += len(vy)

        val_acc = (val_correct / val_total) * 100.0

        if val_acc > best_val_acc:
            best_val_acc = val_acc
            best_model_state = model.state_dict().copy()
            star = " ⭐ (Best)"
        else:
            star = ""

        print(f"  Epoch {epoch+1:2d}/{epochs:2d} | Train Acc: {train_acc:5.1f}% (Loss: {train_loss/train_total:.4f}) | Val Acc: {val_acc:5.1f}%{star}")

    # Nạp lại checkpoint tốt nhất
    if best_model_state is not None:
        model.load_state_dict(best_model_state)

    print(f"\n  -> Độ chính xác cao nhất trên tập Validation viết tay: {best_val_acc:.2f}%")

    # 4. Xuất mô hình ONNX
    print("\n[4/5] Xuất file mô hình ONNX sang models/math_rec.onnx...")
    model.eval()
    output_dir = os.path.join(os.path.dirname(__file__), "..", "models")
    os.makedirs(output_dir, exist_ok=True)
    onnx_path = os.path.join(output_dir, "math_rec.onnx")

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
    print(f"  -> File size ONNX: {file_size_kb:.1f} KB ({file_size_kb / 1024.0:.2f} MB)")

    # Lưu danh mục vocabulary song song vào models/math_vocab.json
    vocab_file = os.path.join(output_dir, "math_vocab.json")
    vocab_data = {
        "num_classes": len(valid_classes),
        "classes": valid_classes,
        "latex_vocabulary": [CLASS_TO_LATEX[c] for c in valid_classes]
    }
    with open(vocab_file, "w", encoding="utf-8") as vf:
        json.dump(vocab_data, vf, indent=2, ensure_ascii=False)
    print(f"  -> Đã lưu danh mục từ vựng: {vocab_file}")

    # Đồng bộ sang LocalAppData
    appdata_dir = os.path.join(os.environ.get("LOCALAPPDATA", ""), "HapiCopi", "models")
    if os.path.exists(appdata_dir):
        target_file = os.path.join(appdata_dir, "math_rec.onnx")
        shutil.copy2(onnx_path, target_file)
        # Xóa file .data phụ nếu có
        stale_data = os.path.join(appdata_dir, "math_rec.onnx.data")
        if os.path.exists(stale_data):
            os.remove(stale_data)
        print(f"  -> Đã đồng bộ trực tiếp vào LocalAppData: {target_file}")

    # 5. Kiểm thử độ trễ & xác nhận ONNX
    print("\n[5/5] Kiểm thử xác thực bằng onnxruntime...")
    import onnxruntime as ort
    sess = ort.InferenceSession(onnx_path)
    test_in = np.random.randn(1, 1, 32, 32).astype(np.float32)
    
    # Warm up
    for _ in range(10):
        sess.run(None, {"input": test_in})

    # Benchmark latency
    import time
    t0 = time.perf_counter()
    runs = 200
    for _ in range(runs):
        res = sess.run(None, {"input": test_in})
    avg_latency_ms = (time.perf_counter() - t0) / runs * 1000.0

    print(f"  -> Output shape: {res[0].shape}")
    print(f"  -> Độ trễ đo được: {avg_latency_ms:.3f} ms / ký tự (Throughput: {1000.0/avg_latency_ms:.0f} ký tự/giây)")
    print("=" * 70)
    print("✨ HOÀN TẤT HUẤN LUYỆN VÀ TRIỂN KHAI PHƯƠNG ÁN A2!")
    print("=" * 70)


if __name__ == "__main__":
    main()
