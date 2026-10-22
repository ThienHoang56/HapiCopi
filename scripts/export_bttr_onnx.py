import os
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')
import json
import time

sys.path.insert(0, 'scripts/bttr_repo')
import torch
import torch.nn as nn
import torch.nn.functional as F
from PIL import Image
import numpy as np

from bttr.lit_bttr import LitBTTR
from bttr.datamodule import vocab

print('=' * 70)
print('🚀 EXPORTING BTTR END-TO-END MATH FORMULA RECOGNIZER TO ONNX')
print('=' * 70)

ckpt_path = 'scripts/bttr_repo/pretrained-2014.ckpt'
if not os.path.exists(ckpt_path):
    print(f'Error: Checkpoint not found at {ckpt_path}')
    sys.exit(1)

# 1. Load model weights
print('[1/5] Loading PyTorch Lightning BTTR checkpoint...')
model = LitBTTR.load_from_checkpoint(ckpt_path, weights_only=False)
model.eval()

# 2. Build Clean Decoder Module (100% Dynamic ONNX Compatible)
class CleanDecoderLayer(nn.Module):
    def __init__(self, orig_layer):
        super().__init__()
        self.orig = orig_layer
        self.H = 8
        self.d = 32

    def forward(self, x, memory, causal_mask=None, memory_mask=None):
        B, L, D = x.shape
        S = memory.shape[1]

        # 1. Self Attention
        qkv = F.linear(x, self.orig.self_attn.in_proj_weight, self.orig.self_attn.in_proj_bias)
        q, k, v = torch.chunk(qkv, 3, dim=-1)
        q = q.view(B, L, self.H, self.d).transpose(1, 2)
        k = k.view(B, L, self.H, self.d).transpose(1, 2)
        v = v.view(B, L, self.H, self.d).transpose(1, 2)
        sa = F.scaled_dot_product_attention(q, k, v, attn_mask=causal_mask)
        sa = sa.transpose(1, 2).contiguous().view(B, L, D)
        sa = F.linear(sa, self.orig.self_attn.out_proj.weight, self.orig.self_attn.out_proj.bias)
        x = self.orig.norm1(x + sa)

        # 2. Cross Attention
        w_q, w_k, w_v = torch.chunk(self.orig.multihead_attn.in_proj_weight, 3, dim=0)
        b_q, b_k, b_v = torch.chunk(self.orig.multihead_attn.in_proj_bias, 3, dim=0)
        q = F.linear(x, w_q, b_q).view(B, L, self.H, self.d).transpose(1, 2)
        k = F.linear(memory, w_k, b_k).view(B, S, self.H, self.d).transpose(1, 2)
        v = F.linear(memory, w_v, b_v).view(B, S, self.H, self.d).transpose(1, 2)
        ca = F.scaled_dot_product_attention(q, k, v, attn_mask=memory_mask)
        ca = ca.transpose(1, 2).contiguous().view(B, L, D)
        ca = F.linear(ca, self.orig.multihead_attn.out_proj.weight, self.orig.multihead_attn.out_proj.bias)
        x = self.orig.norm2(x + ca)

        # 3. Feed Forward
        ffn = F.linear(F.relu(F.linear(x, self.orig.linear1.weight, self.orig.linear1.bias)), self.orig.linear2.weight, self.orig.linear2.bias)
        x = self.orig.norm3(x + ffn)
        return x

class CleanBTTRDecoder(nn.Module):
    def __init__(self, orig_decoder):
        super().__init__()
        self.word_embed = orig_decoder.word_embed
        self.pe = orig_decoder.pos_enc.pe
        self.layers = nn.ModuleList([CleanDecoderLayer(l) for l in orig_decoder.model.layers])
        self.proj = orig_decoder.proj

    def forward(self, src, src_mask, tgt):
        # src: [1, S, 256]
        # src_mask: [1, S] bool
        # tgt: [1, L] long
        L = tgt.shape[1]
        S = src.shape[1]

        # Embeddings + Positional
        x = self.word_embed(tgt)
        emb = self.pe[:L, :]
        x = x + emb[None, :, :]

        # Causal mask: [L, L] (True where attention is masked out, or boolean mask for SDPA)
        # Note: In PyTorch 2.0 SDPA with bool attn_mask: True means keep, False means mask,
        # OR boolean upper-triangular where True means attend.
        # Let's use lower-triangular causal mask:
        causal_mask = torch.tril(torch.ones(L, L, dtype=torch.bool, device=tgt.device))
        cross_mask = src_mask.view(1, 1, 1, S) if src_mask is not None else None

        for layer in self.layers:
            x = layer(x, src, causal_mask=causal_mask, memory_mask=cross_mask)

        logits = self.proj(x)
        return logits[:, -1, :] # return only next token logits [1, vocab_size]

# Build Clean Decoder
clean_decoder = CleanBTTRDecoder(model.bttr.decoder)
clean_decoder.eval()

# 3. Export Encoder to ONNX
print('[2/5] Exporting DenseNet Encoder to models/math_encoder.onnx...')
os.makedirs('models', exist_ok=True)
encoder = model.bttr.encoder
encoder.eval()

dummy_img = torch.randn(1, 1, 96, 128)
dummy_mask = torch.zeros(1, 96, 128, dtype=torch.bool)

torch.onnx.export(
    encoder,
    (dummy_img, dummy_mask),
    'models/math_encoder.onnx',
    input_names=['img', 'img_mask'],
    output_names=['feature', 'mask'],
    dynamic_axes={
        'img': {2: 'height', 3: 'width'},
        'img_mask': {1: 'height', 2: 'width'},
        'feature': {1: 'time'},
        'mask': {1: 'time'}
    },
    opset_version=14,
    dynamo=False
)
enc_size = os.path.getsize('models/math_encoder.onnx') / (1024 * 1024)
print(f'  -> models/math_encoder.onnx: {enc_size:.2f} MB')

# 4. Export Decoder to ONNX
print('[3/5] Exporting Clean Transformer Decoder to models/math_decoder.onnx...')
dummy_src = torch.randn(1, 64, 256)
dummy_smask = torch.zeros(1, 64, dtype=torch.bool)
dummy_tgt = torch.tensor([[1, 5]], dtype=torch.long)

torch.onnx.export(
    clean_decoder,
    (dummy_src, dummy_smask, dummy_tgt),
    'models/math_decoder.onnx',
    input_names=['src', 'src_mask', 'tgt'],
    output_names=['logits'],
    dynamic_axes={
        'src': {1: 'src_len'},
        'src_mask': {1: 'src_len'},
        'tgt': {1: 'tgt_len'},
    },
    opset_version=14,
    dynamo=False
)
dec_size = os.path.getsize('models/math_decoder.onnx') / (1024 * 1024)
print(f'  -> models/math_decoder.onnx: {dec_size:.2f} MB')
print(f'  -> TỔNG DUNG LƯỢNG 2 MODEL ONNX: {enc_size + dec_size:.2f} MB')

# 5. Export Vocabulary
print('[4/5] Exporting vocabulary mapping to models/math_e2e_vocab.json...')
dict_file = 'scripts/bttr_repo/bttr/datamodule/dictionary.txt'
vocab_words = ['<pad>', '<sos>', '<eos>']
with open(dict_file, 'r', encoding='utf-8') as f:
    vocab_words.extend([line.strip() for line in f if line.strip()])

vocab_meta = {
    'pad_idx': 0,
    'sos_idx': 1,
    'eos_idx': 2,
    'vocab_size': len(vocab_words),
    'tokens': vocab_words
}
with open('models/math_e2e_vocab.json', 'w', encoding='utf-8') as f:
    json.dump(vocab_meta, f, ensure_ascii=False, indent=2)
print(f'  -> Đã lưu {len(vocab_words)} tokens vào models/math_e2e_vocab.json')

# 6. Verify with Pure ONNX Runtime
print('[5/5] Kiểm thử đo đạc thực nghiệm với ONNX Runtime...')
import onnxruntime as ort

enc_session = ort.InferenceSession('models/math_encoder.onnx')
dec_session = ort.InferenceSession('models/math_decoder.onnx')

def predict_formula_onnx(img_path):
    t0 = time.perf_counter()
    img = Image.open(img_path).convert('L')
    arr = np.array(img, dtype=np.float32) / 255.0
    h, w = arr.shape
    img_tensor = arr.reshape(1, 1, h, w)
    mask_tensor = np.zeros((1, h, w), dtype=bool)

    t_enc_start = time.perf_counter()
    enc_out = enc_session.run(None, {'img': img_tensor, 'img_mask': mask_tensor})
    feat, mask = enc_out[0], enc_out[1]
    enc_time = (time.perf_counter() - t_enc_start) * 1000.0

    tgt = np.array([[1]], dtype=np.int64) # [<sos>]
    tokens = []
    t_dec_start = time.perf_counter()
    for _ in range(60):
        logits = dec_session.run(None, {'src': feat, 'src_mask': mask, 'tgt': tgt})[0]
        next_tok = int(np.argmax(logits[0]))
        if next_tok == 2: # <eos>
            break
        tokens.append(next_tok)
        tgt = np.concatenate([tgt, np.array([[next_tok]], dtype=np.int64)], axis=1)
    dec_time = (time.perf_counter() - t_dec_start) * 1000.0
    total_time = (time.perf_counter() - t0) * 1000.0

    latex = ' '.join([vocab_words[idx] for idx in tokens])
    return latex, enc_time, dec_time, total_time, len(tokens)

# Test 1: Formula from example
latex1, enc_t1, dec_t1, tot_t1, n1 = predict_formula_onnx('scripts/bttr_repo/example/18_em_1.bmp')
print(f'\n[KẾT QUẢ TEST 1 - MẪU CROHME 18_em_1.bmp]:')
print(f'  LaTeX: {latex1}')
print(f'  Encoder: {enc_t1:.1f}ms | Decoder ({n1} tokens): {dec_t1:.1f}ms | Tổng độ trễ: {tot_t1:.1f}ms')

# Test 2: User formula from testing fixtures
test_fixture = 'testing/fixtures/user_integral.png'
if os.path.exists(test_fixture):
    latex2, enc_t2, dec_t2, tot_t2, n2 = predict_formula_onnx(test_fixture)
    print(f'\n[KẾT QUẢ TEST 2 - CÔNG THỨC VIẾT TAY (∫ 2 x dx)] :')
    print(f'  LaTeX: {latex2}')
    print(f'  Encoder: {enc_t2:.1f}ms | Decoder ({n2} tokens): {dec_t2:.1f}ms | Tổng độ trễ: {tot_t2:.1f}ms')

print('\n' + '=' * 70)
print('✅ HOÀN THÀNH BƯỚC 1: ONNX EXPORT & VERIFICATION')
print('=' * 70)
