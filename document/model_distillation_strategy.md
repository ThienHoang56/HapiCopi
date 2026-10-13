# 🧠 Chiến Lược Nâng Cấp Model AI Nhận Diện Công Thức Toán

> **Nguyên tắc:** *"Model chưa ngon mà đòi cá nhân hóa thì rất dở"* — hoàn toàn đúng. Phải có nền tảng vững trước.

---

## 1. Chẩn Đoán Hiện Trạng (Vấn Đề Thật Sự Ở Đâu?)

### Model hiện tại: [MathNet](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/scripts/export_math_onnx.py#L41-L70)

| Chỉ số | Giá trị | Đánh giá |
|--------|---------|----------|
| Kiến trúc | 3-layer CNN (16→32→64 channels) + FC 128 | ❌ Quá nhỏ |
| Input | 32×32 grayscale | ✅ OK |
| Vocabulary | 54 classes | ⚠️ Thiếu nhiều ký hiệu |
| Kích thước | 633 KB | ✅ Cực nhẹ |
| Latency | 0.38ms | ✅ Cực nhanh |
| **Dữ liệu huấn luyện** | **~3,240 mẫu synthetic** (54 class × 60 mẫu) | ❌ **ĐÂY LÀ GỐC VẤN ĐỀ** |
| Nguồn dữ liệu | Render text bằng Windows fonts (Segoe, Arial, Calibri) | ❌ **Không giống nét viết tay** |
| Augmentation | Rotation ±14°, jitter ±1px | ⚠️ Quá ít biến thể |

### 🔴 Tại sao model yếu?

```
Vấn đề #1: DỮ LIỆU KHÔNG PHẢI NÉT VIẾT TAY
┌─────────────────────────────────────────────────────┐
│  Model train trên FONT (Arial, Segoe UI)             │
│  → Nhận diện TỐT: text render, đánh máy             │
│  → Nhận diện YẾU: nét viết tay thật (vì chưa từng   │
│    nhìn thấy nét viết tay trong lúc train)            │
└─────────────────────────────────────────────────────┘

Vấn đề #2: QUÁ ÍT DỮ LIỆU
┌─────────────────────────────────────────────────────┐
│  3,240 mẫu cho 54 lớp = 60 mẫu/lớp                  │
│  State-of-the-art cần: 500-3,000 mẫu/lớp            │
│  → Model chưa đủ "thấy" biến thể nét viết           │
└─────────────────────────────────────────────────────┘
```

> [!IMPORTANT]
> Cá nhân hóa hiện tại (`HandwritingProfileService`) đang cố **bù đắp** cho model yếu bằng template matching + topological features. Nó hoạt động, nhưng **nền tảng model quá mỏng** nên sức mạnh ensemble bị hạn chế.

---

## 2. Các Phương Án Teacher Model Để Distill

### Bảng So Sánh Nguồn Dữ Liệu

| Nguồn | Số mẫu | Loại | Ưu điểm | Nhược điểm | Phù hợp? |
|-------|--------|------|---------|------------|-----------|
| **HASYv2** | 168,236 | Offline (32×32 ảnh) | Dễ dùng nhất, format giống hệt model hiện tại (32×32 grayscale), 369 classes | Chất lượng ảnh thấp, nhiều class không liên quan toán | ⭐⭐⭐⭐ |
| **CROHME** | ~100K expressions | Online (InkML) + Offline | Standard benchmark, chất lượng cao, nét viết tay thật | Phức tạp format InkML, cần parse | ⭐⭐⭐ |
| **MathWriting** (Google 2024) | 230K human + 400K synthetic | Online (InkML) | Lớn nhất, 254 tokens, chất lượng cao nhất | Cần convert format, phức tạp | ⭐⭐⭐⭐⭐ |

---

## 3. Đề Xuất: Chiến Lược Distillation 2 Pha

### Pha A: Nâng cấp Student Model với HASYv2 (Nhanh nhất, hiệu quả nhất)

```
┌────────────────────────────────────────────────┐
│  TEACHER: HASYv2 Dataset (168K+ mẫu viết tay)  │
│  → Train trực tiếp trên DỮ LIỆU VIẾT TAY THẬT  │
│  → Không cần teacher model riêng               │
│  → Chỉ cần thay data, giữ kiến trúc           │
└─────────────────────┬──────────────────────────┘
                      │
                      ▼
┌────────────────────────────────────────────────┐
│  STUDENT: MathNet nâng cấp                      │
│  - Backbone: CNN giữ nguyên hoặc mở rộng nhẹ    │
│  - Data: 168K mẫu viết tay thật (vs 3.2K font)  │
│  - Augment: Elastic distortion, stroke thickness │
│  - Output: ONNX ~1-2 MB, latency < 2ms          │
└────────────────────────────────────────────────┘
```

**Lý do chọn HASYv2 làm bước đầu:**
1. **Format khớp hoàn hảo**: 32×32 grayscale — giống hệt input pipeline hiện tại, không cần đổi code C#
2. **168K mẫu viết tay thật** — gấp **52 lần** data hiện tại
3. **369 symbol classes** — phủ rộng hơn 54 classes hiện tại
4. **Download dễ**: 1 file từ Kaggle, không cần parse InkML phức tạp

### Pha B: Knowledge Distillation từ Teacher lớn (Tuần sau)

```
┌────────────────────────────────────────────────┐
│  TEACHER MODEL (Heavy, chạy offline 1 lần)      │
│  - Kiến trúc: ResNet-18 hoặc DenseNet-121       │
│  - Train trên: HASYv2 + CROHME + MathWriting    │
│  - Accuracy: >96% top-1                         │
│  - Kích thước: ~40-80 MB (không cần deploy)      │
└─────────────────────┬──────────────────────────┘
                      │ Soft Labels (KL-Divergence)
                      │ + Feature Maps (MSE Loss)
                      ▼
┌────────────────────────────────────────────────┐
│  STUDENT MODEL (Deploy trong app)               │
│  - Kiến trúc: MobileNetV3-Small hoặc MathNet+   │
│  - INT8 Quantization                             │
│  - Kích thước: 1.5 - 2.5 MB                     │
│  - Latency: 2-8 ms                              │
│  - Accuracy: >93% top-1 (gần teacher)            │
└────────────────────────────────────────────────┘
```

---

## 4. Kế Hoạch Thực Thi Cụ Thể

### Bước 1: Chuẩn Bị Dữ Liệu HASYv2

```python
# Download từ Kaggle: https://www.kaggle.com/datasets/mystic123/hasyv2
# Hoặc GitHub: https://github.com/MartinThoma/HASY
# 
# Cấu trúc:
#   hasy-data-labels.csv  → mapping image_path → symbol_id → latex_label
#   hasy-data/            → 168,236 ảnh PNG 32×32
#
# Filter giữ lại ~80-100 classes toán học liên quan:
#   Số: 0-9
#   Chữ cái: a-z (lowercase), A-Z (uppercase) 
#   Toán tử: +, -, =, ×, ÷, <, >, ≤, ≥, ≠
#   Hy Lạp: α, β, γ, θ, π, σ, φ, ω
#   Cấu trúc: √, ∫, ∑, ∞, (, ), [, ], {, }
```

### Bước 2: Nâng Cấp Script Huấn Luyện

Thay đổi chính trong [export_math_onnx.py](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/scripts/export_math_onnx.py):

| Thay đổi | Hiện tại | Sau nâng cấp |
|----------|----------|-------------|
| Dữ liệu | Render font → 3,240 mẫu | HASYv2 real handwriting → ~80,000+ mẫu |
| Kiến trúc | CNN 16→32→64 (633 KB) | CNN 32→64→128 hoặc MobileNetV3-Small (~1.5-2 MB) |
| Augmentation | Rotation ±14°, jitter | + Elastic distortion, stroke width, shear, erasing |
| Epochs | 18 | 40-60 (với early stopping) |
| Optimizer | Adam lr=0.003 | AdamW lr=0.001 + CosineAnnealing scheduler |
| Regularization | Dropout 0.15 | Dropout 0.3 + Label smoothing 0.1 + Mixup |

### Bước 3: Export & Deploy

```bash
# Train
uv run scripts/train_math_hasyv2.py

# Quantize (tùy chọn, nếu model > 2MB)
python -m onnxruntime.quantization.quantize \
  --input models/math_rec_fp32.onnx \
  --output models/math_rec.onnx \
  --quantize_mode dynamic --per_channel

# Verify
dotnet run -- --test-ai-math
```

---

## 5. Bảng Đánh Đổi (Trade-off Matrix)

| Phương án | Kích thước | Accuracy dự kiến | Latency | Effort |
|-----------|-----------|-------------------|---------|--------|
| **A1**: Giữ MathNet, đổi data → HASYv2 | ~800 KB | 80-88% | <1ms | ⭐ Thấp |
| **A2**: MathNet mở rộng + HASYv2 | ~1.5 MB | 88-93% | 1-2ms | ⭐⭐ Trung bình |
| **A3**: MobileNetV3-Small + HASYv2 + CROHME | ~2 MB | 93-96% | 3-8ms | ⭐⭐⭐ Cao |
| **A4**: Distill từ ResNet teacher → MobileNet student | ~2 MB | 94-97% | 3-8ms | ⭐⭐⭐⭐ Rất cao |

> [!TIP]
> **Khuyến nghị bắt đầu với A2** — tăng accuracy đáng kể mà vẫn giữ model nhẹ, latency thấp, và effort vừa phải. Sau khi A2 ổn định, tiến lên A3/A4 nếu cần.

---

## 6. Tổng Kết & Câu Hỏi Cần Quyết Định

> [!IMPORTANT]
> **Gốc vấn đề không phải kiến trúc model, mà là DATA.** 
> Model hiện tại train trên 3,240 mẫu render font → nó chỉ biết nhận font, không biết nhận nét viết tay.
> Chỉ cần thay data bằng 168K+ mẫu viết tay thật (HASYv2) là model sẽ nhảy cấp ngay lập tức.

### Cần bạn quyết định:

1. **Phương án nào?** (A1 nhẹ nhất → A4 mạnh nhất)
2. **Ngân sách kích thước model?** Hiện tại 633KB, có chấp nhận lên 1.5-2 MB không? (Latency vẫn < 10ms)
3. **Vocabulary mở rộng?** Giữ 54 classes hay mở rộng lên ~80-100 classes?
4. **Bạn có download được HASYv2 từ Kaggle không?** (Nếu không, mình có thể viết script tự tải)
