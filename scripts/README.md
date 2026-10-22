# HapiCopi — Python Machine Learning & Export Pipelines

Thư mục này chứa toàn bộ các pipeline Python phục vụ chuẩn bị dữ liệu, huấn luyện mô hình, kiểm duyệt dữ liệu crowdsourced và xuất mô hình sang định dạng ONNX phục vụ ứng dụng C# HapiCopi chạy 100% offline.

---

## 1. Cấu trúc thư mục (Directory Layout)

```
scripts/
├── export_bttr_onnx.py         # Xuất bộ mô hình BTTR End-to-End (math_encoder & math_decoder) sang ONNX
├── export_math_onnx.py         # Xuất mô hình CNN 3-tầng sang models/math_rec.onnx
├── train_math_a2.py            # Huấn luyện mô hình CNN ký tự toán cơ sở (Tầng 1 & Tầng 2)
├── train_matrix_decoder.py     # Huấn luyện / fine-tune decoder ma trận và vector
├── prepare_matrix_dataset.py   # Tải và tiền xử lý dataset ma trận từ MathWriting-human
├── cache_encoder_features.py   # Tiền trích xuất đặc trưng encoder lưu vào cached_features.npz
├── validate_crowd_data.py      # Pipeline thẩm định nét vẽ crowdsourced bằng Gemini Flash (LLM-Judge)
├── test_matrix_inference.py    # Kiểm thử suy luận ma trận viết tay thực tế trên Python ONNX
├── generate_cs_vocab.py        # Sinh mảng Vocabulary mã nguồn C# từ math_vocab.json
├── compare_vocab.py            # Kiểm tra đồng bộ 100% giữa JSON từ điển và C# code
├── matrix_data/                # Dữ liệu ma trận mẫu (dataset_manifest.json, images/, cache npz)
└── bttr_repo/                  # Module BTTR gốc và weights PyTorch (pretrained-2014.ckpt)
```

---

## 2. Phân loại Pipeline

### A. Xuất mô hình sang ONNX (`models/`)
- **`export_bttr_onnx.py`**:
  Nạp checkpoint PyTorch `scripts/bttr_repo/pretrained-2014.ckpt`, bóc tách thành 2 file ONNX độc lập:
  - `models/math_encoder.onnx`: Trích xuất đặc trưng ảnh công thức viết tay (~12 MB).
  - `models/math_decoder.onnx`: Tự hồi quy dự đoán chuỗi LaTeX (~13 MB, causal dynamic attention).
  - `models/math_e2e_vocab.json`: Từ điển 111 token LaTeX chuẩn hóa.
  ```powershell
  python scripts/export_bttr_onnx.py
  ```

- **`export_math_onnx.py`**:
  Xuất mô hình CNN 3-tầng nhận diện ký tự đơn và topo sang `models/math_rec.onnx` (kích thước ~1.4 MB).
  ```powershell
  python scripts/export_math_onnx.py
  ```

### B. Huấn luyện & Tinh chỉnh mô hình (Training & Fine-tuning)
- **`train_math_a2.py`**:
  Pipeline PyTorch huấn luyện mạng CNN trên tập dữ liệu ký tự toán học, tự động đánh giá độ trễ và độ chính xác.
  ```powershell
  python scripts/train_math_a2.py
  ```

- **`prepare_matrix_dataset.py` & `cache_encoder_features.py`**:
  Chuẩn bị 240+ mẫu công thức ma trận thực tế từ dataset `MathWriting-human` (Hugging Face) và tính toán trước đặc trưng encoder để tăng tốc huấn luyện:
  ```powershell
  python scripts/prepare_matrix_dataset.py
  python scripts/cache_encoder_features.py
  ```

- **`train_matrix_decoder.py`**:
  Fine-tune decoder BTTR để chuyên biệt hóa khả năng nhận diện ma trận ($2 \times 2, 3 \times 3$), vector cột và cấu trúc nhiều dòng.

### C. Kiểm thử & Đồng bộ Từ điển
- **`test_matrix_inference.py`**:
  Chạy kiểm thử suy luận nhanh trên các mẫu ảnh ma trận trong `scripts/matrix_data/images/`.
- **`compare_vocab.py`**:
  Đối chiếu từng token giữa `models/math_vocab.json` và mảng C# trong `Core/Math/OnnxMathVisionEngine.cs`.
- **`validate_crowd_data.py`**:
  Thẩm định nét vẽ do người dùng đóng góp bằng mô hình Gemini Flash để bảo vệ dataset khỏi nét rác/vẽ bậy.

---

## 3. Quy chuẩn Kỹ thuật
- **CPU First:** Mọi script export ONNX và suy luận phải chạy ổn định trên CPU mà không đòi hỏi GPU.
- **Single-File ONNX:** ONNX xuất ra phải tự chứa trọng số (`dynamo=False`), tương thích hoàn toàn với Microsoft ONNX Runtime trên C# .NET 10.
- **Không xả file tạm:** Toàn bộ ảnh test tạm thời hoặc kết quả trung gian phải lưu vào `testing/` hoặc dọn dẹp sạch sẽ sau khi hoàn tất.
