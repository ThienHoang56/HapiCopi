import os
import sys
import json
import numpy as np
from PIL import Image, ImageDraw
import onnxruntime as ort

sys.stdout.reconfigure(encoding="utf-8")

ENCODER_PATH = "models/math_encoder.onnx"
DECODER_PATH = "models/math_decoder.onnx"
VOCAB_PATH = "models/math_e2e_vocab.json"

enc_sess = ort.InferenceSession(ENCODER_PATH)
dec_sess = ort.InferenceSession(DECODER_PATH)
vocab = json.load(open(VOCAB_PATH, encoding="utf-8"))["tokens"]

# Render column matrix [ 1 \\ 2 \\ 3 ]
img = Image.new("L", (128, 128), color=0)
draw = ImageDraw.Draw(img)

# Left bracket [
draw.line([(30, 15), (18, 15), (18, 110), (30, 110)], fill=255, width=4)

# Digit 1
draw.line([(40, 25), (48, 25), (48, 45)], fill=255, width=4)

# Digit 2
draw.line([(38, 56), (46, 50), (54, 50), (58, 56), (38, 75), (58, 75)], fill=255, width=4)

# Digit 3
draw.line([(38, 85), (55, 85), (46, 95), (56, 105), (38, 105)], fill=255, width=4)

# Right bracket ]
draw.line([(70, 15), (82, 15), (82, 110), (70, 110)], fill=255, width=4)

# Preprocess to height 96, multiple of 8
bbox = img.getbbox()
cropped = img.crop(bbox)
scale = 96.0 / cropped.height
new_w = max(32, int(round(cropped.width * scale)))
resized = cropped.resize((new_w, 96), Image.Resampling.BILINEAR)

pad = 16
final_h = ((96 + pad * 2 + 7) // 8) * 8
final_w = max(64, ((new_w + pad * 2 + 7) // 8) * 8)

final_img = Image.new("L", (final_w, final_h), color=0)
final_img.paste(resized, (pad, pad))
final_img.save("scripts/test_column_matrix.png")

arr = np.array(final_img, dtype=np.float32) / 255.0
h, w = arr.shape

img_tensor = arr[np.newaxis, np.newaxis, :, :]
mask_tensor = np.zeros((1, h, w), dtype=bool)
feat = enc_sess.run(["feature"], {"img": img_tensor, "img_mask": mask_tensor})[0]

gen_tokens = [1]
for _ in range(80):
    tgt = np.array([gen_tokens], dtype=np.int64)
    logits = dec_sess.run(["logits"], {"src": feat, "tgt": tgt})[0]
    best_id = int(np.argmax(logits[0]))
    if best_id == 2:
        break
    gen_tokens.append(best_id)

out_words = [vocab[idx] for idx in gen_tokens[1:] if idx < len(vocab)]
raw = " ".join(out_words)
print("Raw prediction:", raw)
