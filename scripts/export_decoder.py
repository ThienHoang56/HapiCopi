import sys
import os
import math
import traceback

sys.path.insert(0, 'scripts/bttr_repo')
import torch
import torch.nn as nn
import torch.nn.functional as F
from bttr.lit_bttr import LitBTTR

try:
    print('Loading checkpoint...')
    model = LitBTTR.load_from_checkpoint('scripts/bttr_repo/pretrained-2014.ckpt', weights_only=False)
    model.eval()

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

            # 1. Self Attention with MatMul + Softmax + MatMul
            qkv = F.linear(x, self.orig.self_attn.in_proj_weight, self.orig.self_attn.in_proj_bias)
            q, k, v = torch.chunk(qkv, 3, dim=-1)
            q = q.view(B, L, self.H, self.d).transpose(1, 2)
            k = k.view(B, L, self.H, self.d).transpose(1, 2)
            v = v.view(B, L, self.H, self.d).transpose(1, 2)
            
            scores = torch.matmul(q, k.transpose(-2, -1)) * self.scale
            # causal mask: triu with -1e9
            causal_mask = torch.triu(torch.full((L, L), -1e9, dtype=x.dtype, device=x.device), diagonal=1)
            scores = scores + causal_mask.unsqueeze(0).unsqueeze(0)
            attn = F.softmax(scores, dim=-1)
            sa = torch.matmul(attn, v).transpose(1, 2).contiguous().view(B, L, D)
            sa = F.linear(sa, self.orig.self_attn.out_proj.weight, self.orig.self_attn.out_proj.bias)
            x = self.orig.norm1(x + sa)

            # 2. Cross Attention
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

    class CleanBTTRDecoder(nn.Module):
        def __init__(self, orig_decoder):
            super().__init__()
            self.word_embed = orig_decoder.word_embed
            self.pe = orig_decoder.pos_enc.pe
            self.layers = nn.ModuleList([CleanDecoderLayer(l) for l in orig_decoder.model.layers])
            self.proj = orig_decoder.proj

        def forward(self, src, tgt):
            L = tgt.shape[1]
            x = self.word_embed(tgt)
            x = x + self.pe[:L, :].unsqueeze(0)
            for layer in self.layers:
                x = layer(x, src)
            logits = self.proj(x)
            return logits[:, -1, :]

    clean_dec = CleanBTTRDecoder(model.bttr.decoder)
    clean_dec.eval()

    print('Exporting CleanBTTRDecoder to models/math_decoder.onnx...')
    dummy_src = torch.randn(1, 64, 256)
    dummy_tgt = torch.tensor([[1, 5]], dtype=torch.long)
    torch.onnx.export(
        clean_dec,
        (dummy_src, dummy_tgt),
        'models/math_decoder.onnx',
        input_names=['src', 'tgt'],
        output_names=['logits'],
        dynamic_axes={
            'src': {1: 'src_len'},
            'tgt': {1: 'tgt_len'},
        },
        opset_version=14,
        dynamo=False
    )
    print('Export success!')

    # Test with ONNX Runtime
    import onnxruntime as ort
    import numpy as np
    sess = ort.InferenceSession('models/math_decoder.onnx')
    for length in [1, 2, 5, 20]:
        tgt = np.ones((1, length), dtype=np.int64)
        src = np.zeros((1, 48, 256), dtype=np.float32)
        out = sess.run(None, {'src': src, 'tgt': tgt})[0]
        print(f'ONNX Runtime length {length}: output shape {out.shape}')

except Exception as e:
    print('EXCEPTION ENCOUNTERED:')
    traceback.print_exc()
