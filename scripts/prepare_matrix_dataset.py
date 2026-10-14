import os
import sys
import json
import re
from PIL import Image, ImageOps
import numpy as np
from datasets import load_dataset

OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "matrix_data")
os.makedirs(OUTPUT_DIR, exist_ok=True)
IMG_DIR = os.path.join(OUTPUT_DIR, "images")
os.makedirs(IMG_DIR, exist_ok=True)

TARGET_MATRIX_SAMPLES = 300
TARGET_REGULAR_SAMPLES = 100

print(f"--> Target: {TARGET_MATRIX_SAMPLES} matrix/vector samples + {TARGET_REGULAR_SAMPLES} regular math samples")
print("--> Streaming deepcopy/MathWriting-human from Hugging Face...")

def preprocess_image(pil_img, target_h=96):
    """Convert to grayscale, detect background, invert if dark-on-light, scale to height 96."""
    gray = pil_img.convert("L")
    arr = np.array(gray)
    
    # Check borders to determine background
    border_pixels = np.concatenate([arr[0, :], arr[-1, :], arr[:, 0], arr[:, -1]])
    if np.mean(border_pixels) > 128:
        # Dark strokes on light background -> invert to white strokes on black background
        gray = ImageOps.invert(gray)
        arr = np.array(gray)
        
    # Crop bounding box of foreground (pixels > 40)
    coords = np.argwhere(arr > 40)
    if coords.size == 0:
        return None
    y0, x0 = coords.min(axis=0)
    y1, x1 = coords.max(axis=0) + 1
    cropped = gray.crop((x0, y0, x1, y1))
    
    # Scale to height 96 maintaining aspect ratio
    w, h = cropped.size
    scale = target_h / max(1, h)
    new_w = max(32, int(round(w * scale)))
    scaled = cropped.resize((new_w, target_h), Image.Resampling.BILINEAR)
    
    # Pad to multiples of 8 with 16px margins
    pad = 16
    final_h = ((target_h + pad * 2 + 7) // 8) * 8
    final_w = max(64, ((new_w + pad * 2 + 7) // 8) * 8)
    
    canvas = Image.new("L", (final_w, final_h), 0)
    canvas.paste(scaled, (pad, pad))
    return canvas

def tokenize_latex(latex_str):
    """Tokenize LaTeX string into discrete CROHME-style tokens."""
    # Match commands (\begin, \end, \matrix, \vec, \frac, etc.), numbers, operators, brackets
    tokens = re.findall(r"\\[a-zA-Z]+|\\\\|&|[0-9]|[a-zA-Z]|[+\-*/=<>()[\]{}]", latex_str)
    return tokens

try:
    ds = load_dataset("deepcopy/MathWriting-human", split="train", streaming=True)
    matrix_count = 0
    regular_count = 0
    records = []
    
    for idx, item in enumerate(ds):
        latex = item.get("latex", "")
        img = item.get("image", None)
        if not latex or img is None:
            continue
            
        is_matrix = any(kw in latex for kw in ["matrix", "pmatrix", "bmatrix", "\\\\", "\\vec"])
        
        if is_matrix and matrix_count < TARGET_MATRIX_SAMPLES:
            prep_img = preprocess_image(img)
            if prep_img is not None:
                fname = f"mat_{matrix_count:04d}.png"
                prep_img.save(os.path.join(IMG_DIR, fname))
                tokens = tokenize_latex(latex)
                records.append({
                    "id": fname,
                    "latex": latex,
                    "tokens": tokens,
                    "is_matrix": True
                })
                matrix_count += 1
                if matrix_count % 50 == 0:
                    print(f"  [Progress] Downloaded & processed {matrix_count}/{TARGET_MATRIX_SAMPLES} matrix samples...")
                    
        elif not is_matrix and regular_count < TARGET_REGULAR_SAMPLES:
            prep_img = preprocess_image(img)
            if prep_img is not None:
                fname = f"reg_{regular_count:04d}.png"
                prep_img.save(os.path.join(IMG_DIR, fname))
                tokens = tokenize_latex(latex)
                records.append({
                    "id": fname,
                    "latex": latex,
                    "tokens": tokens,
                    "is_matrix": False
                })
                regular_count += 1
                
        if matrix_count >= TARGET_MATRIX_SAMPLES and regular_count >= TARGET_REGULAR_SAMPLES:
            break

    manifest_path = os.path.join(OUTPUT_DIR, "dataset_manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(records, f, indent=2, ensure_ascii=False)
        
    print(f"\n[DONE] Successfully prepared dataset:")
    print(f"  - Total samples: {len(records)} ({matrix_count} matrices + {regular_count} regular formulas)")
    print(f"  - Manifest saved to: {manifest_path}")

except Exception as e:
    print(f"Error in prepare_matrix_dataset: {e}")
    sys.exit(1)
