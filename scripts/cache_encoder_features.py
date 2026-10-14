import os
import sys
import json
import time

sys.stdout.reconfigure(encoding="utf-8")
from PIL import Image
import numpy as np
import onnxruntime as ort

DATA_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "matrix_data")
MANIFEST_PATH = os.path.join(DATA_DIR, "dataset_manifest.json")
IMG_DIR = os.path.join(DATA_DIR, "images")
CACHE_OUTPUT_PATH = os.path.join(DATA_DIR, "cached_features.npz")
ENCODER_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "models", "math_encoder.onnx")

print("Initializing ONNX Encoder Session...")
if not os.path.exists(ENCODER_PATH):
    print(f"Error: Encoder not found at {ENCODER_PATH}")
    sys.exit(1)

session = ort.InferenceSession(ENCODER_PATH)
manifest = json.load(open(MANIFEST_PATH, encoding="utf-8"))
print(f"Loaded manifest with {len(manifest)} samples.")

features_dict = {}
start_time = time.time()

for idx, item in enumerate(manifest):
    fname = item["id"]
    img_path = os.path.join(IMG_DIR, fname)
    if not os.path.exists(img_path):
        continue
        
    img = Image.open(img_path).convert("L")
    arr = np.array(img, dtype=np.float32) / 255.0
    h, w = arr.shape
    
    img_tensor = arr[np.newaxis, np.newaxis, :, :]  # [1, 1, H, W]
    mask_tensor = np.zeros((1, h, w), dtype=bool)    # [1, H, W]
    
    out = session.run(["feature"], {
        "img": img_tensor,
        "img_mask": mask_tensor
    })[0]  # [1, T, 256]
    
    # Store feature vector
    features_dict[fname] = out[0]  # [T, 256]
    
    if (idx + 1) % 50 == 0 or (idx + 1) == len(manifest):
        elapsed = time.time() - start_time
        print(f"  [Cached] {idx + 1}/{len(manifest)} images in {elapsed:.1f}s ({elapsed / (idx + 1) * 1000:.1f} ms/img)")

print(f"Saving cached features to {CACHE_OUTPUT_PATH}...")
np.savez_compressed(CACHE_OUTPUT_PATH, **features_dict)
file_mb = os.path.getsize(CACHE_OUTPUT_PATH) / (1024 * 1024)
print(f"[DONE] Successfully cached {len(features_dict)} features ({file_mb:.2f} MB).")
