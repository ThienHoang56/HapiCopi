import os
import sys
import json
import numpy as np
from PIL import Image
import onnxruntime as ort

sys.stdout.reconfigure(encoding="utf-8")

ENCODER_PATH = "models/math_encoder.onnx"
DECODER_PATH = "models/math_decoder.onnx"
VOCAB_PATH = "models/math_e2e_vocab.json"
MANIFEST_PATH = "scripts/matrix_data/dataset_manifest.json"
IMG_DIR = "scripts/matrix_data/images"

print("Loading ONNX models...")
enc_sess = ort.InferenceSession(ENCODER_PATH)
dec_sess = ort.InferenceSession(DECODER_PATH)
vocab = json.load(open(VOCAB_PATH, encoding="utf-8"))["tokens"]
manifest = json.load(open(MANIFEST_PATH, encoding="utf-8"))

def recognize(img_path):
    img = Image.open(img_path).convert("L")
    arr = np.array(img, dtype=np.float32) / 255.0
    h, w = arr.shape
    
    # 1. Encoder
    img_tensor = arr[np.newaxis, np.newaxis, :, :]
    mask_tensor = np.zeros((1, h, w), dtype=bool)
    feat = enc_sess.run(["feature"], {"img": img_tensor, "img_mask": mask_tensor})[0]
    
    # 2. Decoder
    gen_tokens = [1]  # <sos>
    max_len = 60
    for _ in range(max_len):
        tgt = np.array([gen_tokens], dtype=np.int64)
        logits = dec_sess.run(["logits"], {"src": feat, "tgt": tgt})[0]
        best_id = int(np.argmax(logits[0]))
        if best_id == 2:  # <eos>
            break
        gen_tokens.append(best_id)
        
    out_words = [vocab[idx] for idx in gen_tokens[1:] if idx < len(vocab)]
    return " ".join(out_words)

print("\n--- TEST ON REAL HANDWRITTEN MATRIX SAMPLES ---")
matrix_samples = [m for m in manifest if m["is_matrix"]][:5]
for s in matrix_samples:
    path = os.path.join(IMG_DIR, s["id"])
    pred = recognize(path)
    print(f"\n[ID: {s['id']}]")
    print(f"  Ground Truth: {s['latex']}")
    print(f"  AI Prediction: {pred}")
