import os
import sys
import json
import math
import time

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "bttr_repo"))

import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import Dataset, DataLoader
import numpy as np

from bttr.lit_bttr import LitBTTR

DATA_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "matrix_data")
MANIFEST_PATH = os.path.join(DATA_DIR, "dataset_manifest.json")
CACHE_PATH = os.path.join(DATA_DIR, "cached_features.npz")
OLD_VOCAB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "models", "math_e2e_vocab.json")
OUTPUT_DECODER_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "models", "math_decoder.onnx")
CKPT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bttr_repo", "pretrained-2014.ckpt")

print("==================================================")
print("   HAPICOPI: FINE-TUNING DECODER CHO MA TRẬN & VECTOR")
print("==================================================")

# 1. MỞ RỘNG TỪ ĐIỂN (VOCABULARY EXTENSION)
old_vocab_data = json.load(open(OLD_VOCAB_PATH, encoding="utf-8"))
old_tokens = old_vocab_data["tokens"]
old_vocab_set = set(old_tokens)

manifest = json.load(open(MANIFEST_PATH, encoding="utf-8"))
cached_features = np.load(CACHE_PATH)

new_tokens_set = set()
for item in manifest:
    for t in item["tokens"]:
        if t not in old_vocab_set:
            new_tokens_set.add(t)

sorted_new_tokens = sorted(list(new_tokens_set))
print(f"--> Từ điển cũ: {len(old_tokens)} tokens")
print(f"--> Số lượng token mới cần bổ sung: {len(sorted_new_tokens)}")
print(f"--> Danh sách token mới: {sorted_new_tokens}")

extended_tokens = list(old_tokens) + sorted_new_tokens
token2id = {t: idx for idx, t in enumerate(extended_tokens)}
PAD_IDX = token2id["<pad>"]
SOS_IDX = token2id["<sos>"]
EOS_IDX = token2id["<eos>"]
vocab_size = len(extended_tokens)
print(f"--> Tổng từ điển mới: {vocab_size} tokens")

# Lưu từ điển mới
json.dump({"tokens": extended_tokens}, open(OLD_VOCAB_PATH, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
print(f"--> Đã cập nhật file từ điển tại: {OLD_VOCAB_PATH}")

# 2. XÂY DỰNG KIẾN TRÚC DECODER THUẦN ONNX TƯƠNG THÍCH
class CleanDecoderLayer(nn.Module):
    def __init__(self, orig_layer):
        super().__init__()
        self.orig = orig_layer
        self.H = 8
        self.d = 32
        self.scale = 1.0 / math.sqrt(32)

    def forward(self, x, memory):
        B, L, D = x.shape
        S = memory.shape[1]

        # 1. Self Attention with Causal Mask
        qkv = F.linear(x, self.orig.self_attn.in_proj_weight, self.orig.self_attn.in_proj_bias)
        q, k, v = torch.chunk(qkv, 3, dim=-1)
        q = q.view(B, L, self.H, self.d).transpose(1, 2)
        k = k.view(B, L, self.H, self.d).transpose(1, 2)
        v = v.view(B, L, self.H, self.d).transpose(1, 2)
        
        scores = torch.matmul(q, k.transpose(-2, -1)) * self.scale
        causal_mask = torch.triu(torch.full((L, L), -1e9, dtype=x.dtype, device=x.device), diagonal=1)
        scores = scores + causal_mask.unsqueeze(0).unsqueeze(0)
        attn = F.softmax(scores, dim=-1)
        sa = torch.matmul(attn, v).transpose(1, 2).contiguous().view(B, L, D)
        sa = F.linear(sa, self.orig.self_attn.out_proj.weight, self.orig.self_attn.out_proj.bias)
        x = self.orig.norm1(x + sa)

        # 2. Cross Attention với Memory từ Encoder
        w_q, w_k, w_v = torch.chunk(self.orig.multihead_attn.in_proj_weight, 3, dim=0)
        b_q, b_k, b_v = torch.chunk(self.orig.multihead_attn.in_proj_bias, 3, dim=0)
        q = F.linear(x, w_q, b_q).view(B, L, self.H, self.d).transpose(1, 2)
        k = F.linear(memory, w_k, b_k).view(B, S, self.H, self.d).transpose(1, 2)
        v = F.linear(memory, w_v, b_v).view(B, S, self.H, self.d).transpose(1, 2)
        
        c_scores = torch.matmul(q, k.transpose(-2, -1)) * self.scale
        c_attn = F.softmax(c_scores, dim=-1)
        ca = torch.matmul(c_attn, v).transpose(1, 2).contiguous().view(B, L, D)
        ca = F.linear(ca, self.orig.multihead_attn.out_proj.weight, self.orig.multihead_attn.out_proj.bias)
        x = self.orig.norm2(x + ca)

        # 3. Feed Forward
        ffn = F.linear(F.relu(F.linear(x, self.orig.linear1.weight, self.orig.linear1.bias)), self.orig.linear2.weight, self.orig.linear2.bias)
        x = self.orig.norm3(x + ffn)
        return x

class FineTuneBTTRDecoder(nn.Module):
    def __init__(self, orig_decoder, new_vocab_size):
        super().__init__()
        d_model = 256
        orig_emb = orig_decoder.word_embed[0]
        orig_norm = orig_decoder.word_embed[1]
        old_size = orig_emb.num_embeddings
        
        # Mở rộng Embedding: giữ nguyên trọng số cũ
        new_emb = nn.Embedding(new_vocab_size, d_model)
        with torch.no_grad():
            new_emb.weight[:old_size].copy_(orig_emb.weight)
            nn.init.normal_(new_emb.weight[old_size:], mean=0.0, std=0.02)
            
        self.word_embed = nn.Sequential(new_emb, orig_norm)
        self.pe = orig_decoder.pos_enc.pe
        self.layers = nn.ModuleList([CleanDecoderLayer(l) for l in orig_decoder.model.layers])
        
        # Mở rộng Linear Projection Layer: giữ nguyên trọng số cũ
        self.proj = nn.Linear(d_model, new_vocab_size)
        with torch.no_grad():
            self.proj.weight[:old_size].copy_(orig_decoder.proj.weight)
            self.proj.bias[:old_size].copy_(orig_decoder.proj.bias)
            nn.init.normal_(self.proj.weight[old_size:], mean=0.0, std=0.02)
            nn.init.zeros_(self.proj.bias[old_size:])
            
        self.is_inference = False

    def forward(self, src, tgt):
        B, L = tgt.shape
        x = self.word_embed(tgt)
        x = x + self.pe[:L, :].unsqueeze(0)
        for layer in self.layers:
            x = layer(x, src)
        logits = self.proj(x)
        if self.is_inference:
            return logits[:, -1, :]
        return logits

# Nạp pretrained model
print("--> Nạp pretrained checkpoint từ scripts/bttr_repo/pretrained-2014.ckpt...")
orig_model = LitBTTR.load_from_checkpoint(CKPT_PATH, weights_only=False)
model = FineTuneBTTRDecoder(orig_model.bttr.decoder, vocab_size)

# 3. CHUẨN BỊ DATASET & DATALOADER
class MatrixCachedDataset(Dataset):
    def __init__(self, manifest, cached_features, token2id):
        self.samples = []
        for item in manifest:
            fname = item["id"]
            if fname in cached_features:
                token_ids = [token2id[t] for t in item["tokens"] if t in token2id]
                if len(token_ids) > 0 and len(token_ids) <= 120:
                    self.samples.append({
                        "fname": fname,
                        "feature": torch.from_numpy(cached_features[fname]).float(),
                        "token_ids": token_ids
                    })
        print(f"--> Đã nạp {len(self.samples)} mẫu huấn luyện hợp lệ.")

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        return self.samples[idx]

def collate_fn(batch):
    B = len(batch)
    # Tìm chiều dài lớn nhất của feature và token
    max_feat_len = max(s["feature"].shape[0] for s in batch)
    max_tok_len = max(len(s["token_ids"]) for s in batch) + 2  # + sos + eos
    
    src = torch.zeros(B, max_feat_len, 256, dtype=torch.float32)
    tgt_in = torch.full((B, max_tok_len), PAD_IDX, dtype=torch.long)
    tgt_out = torch.full((B, max_tok_len), PAD_IDX, dtype=torch.long)
    
    for i, s in enumerate(batch):
        f = s["feature"]
        src[i, :f.shape[0], :] = f
        
        # Teacher forcing: tgt_in = [<sos>, t1, t2, ..., tn]
        #                 tgt_out = [t1, t2, ..., tn, <eos>]
        seq = s["token_ids"]
        in_seq = [SOS_IDX] + seq
        out_seq = seq + [EOS_IDX]
        tgt_in[i, :len(in_seq)] = torch.tensor(in_seq, dtype=torch.long)
        tgt_out[i, :len(out_seq)] = torch.tensor(out_seq, dtype=torch.long)
        
    return src, tgt_in, tgt_out

dataset = MatrixCachedDataset(manifest, cached_features, token2id)
dataloader = DataLoader(dataset, batch_size=16, shuffle=True, collate_fn=collate_fn)

# 4. FINE-TUNING LOOP (HUẤN LUYỆN TRÊN CPU)
optimizer = torch.optim.AdamW(model.parameters(), lr=1.5e-4, weight_decay=1e-4)
criterion = nn.CrossEntropyLoss(ignore_index=PAD_IDX)

epochs = 18
print(f"--> Bắt đầu fine-tune {epochs} epochs trên CPU...")
model.train()

start_train_time = time.time()
for ep in range(epochs):
    ep_loss = 0.0
    ep_tokens = 0
    ep_start = time.time()
    
    for src, tgt_in, tgt_out in dataloader:
        optimizer.zero_grad()
        logits = model(src, tgt_in)  # [B, L, VocabSize]
        
        loss = criterion(logits.view(-1, vocab_size), tgt_out.view(-1))
        loss.backward()
        torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=1.0)
        optimizer.step()
        
        num_valid = (tgt_out != PAD_IDX).sum().item()
        ep_loss += loss.item() * num_valid
        ep_tokens += num_valid
        
    avg_loss = ep_loss / max(1, ep_tokens)
    ep_elapsed = time.time() - ep_start
    print(f"  [Epoch {ep+1:02d}/{epochs}] Loss: {avg_loss:.4f} | Thời gian: {ep_elapsed:.2f}s")

total_train_elapsed = time.time() - start_train_time
print(f"[HUẤN LUYỆN HOÀN TẤT] Tổng thời gian fine-tune: {total_train_elapsed:.1f}s")

# 5. XUẤT ONNX DECODER MỚI
print(f"--> Xuất ONNX Decoder sang: {OUTPUT_DECODER_PATH}...")
model.eval()
model.is_inference = True

dummy_src = torch.randn(1, 64, 256)
dummy_tgt = torch.tensor([[1, 5]], dtype=torch.long)

torch.onnx.export(
    model,
    (dummy_src, dummy_tgt),
    OUTPUT_DECODER_PATH,
    input_names=["src", "tgt"],
    output_names=["logits"],
    dynamic_axes={
        "src": {1: "src_len"},
        "tgt": {1: "tgt_len"}
    },
    opset_version=14,
    dynamo=False
)

dec_bytes = os.path.getsize(OUTPUT_DECODER_PATH)
print(f"[XUẤT ONNX THÀNH CÔNG] Dung lượng math_decoder.onnx mới: {dec_bytes / (1024*1024):.2f} MB")
