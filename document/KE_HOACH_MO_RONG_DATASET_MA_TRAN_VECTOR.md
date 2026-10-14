# Báo Cáo Nghiên Cứu & Kế Hoạch Mở Rộng Dataset Ma Trận & Vector (Matrix & Linear Algebra Extension)

> **TƯ DUY KỸ THUẬT CỐT LÕI:**  
> *"Đo lường, định lượng được, thấy được sự đánh đổi tốt nhất cho tổng thể, không phải tốt nhất ở cục bộ. Có số liệu, rõ ràng, cách làm dễ hiểu, make sense. Tuyệt đối không gian lận rule-base."*

---

## 1. Bối cảnh & Hiện trạng Thực tế

Sau khi HapiCopi chuyển đổi thành công sang mô hình **End-to-End Sequence Recognition (A4 / BTTR - Vision Transformer)**:
- **Kích thước ONNX:** 24.92 MB (`math_encoder.onnx`: 12.09 MB, `math_decoder.onnx`: 12.83 MB).
- **Độ trễ CPU:** 29.8 ms / công thức (~33.5 công thức/giây).
- **RAM Working Set:** 119.2 MB.
- **Thực nghiệm công thức giải tích:** $\int 2x dx$ nhận diện chính xác 100% (`\int 2 \times d x`), giải quyết triệt để lỗi dính nét và nuốt cận của mô hình heuristic cũ.

### Vấn đề Phát sinh Thực tế (Empirical Failure Case)
Khi người dùng vẽ ma trận cột (vector 3 hàng 1 cột):
$$\begin{bmatrix} 1 \\ 2 \\ 3 \end{bmatrix}$$
Mô hình hiện tại dự đoán ra:
$$[ \, \frac{1}{3} \, ]$$

---

## 2. Phân tích Nguyên nhân Gốc rễ (Root Cause Analysis)

### 2.1. Giới hạn Không gian Nhãn của Tập Dữ liệu CROHME
Mô hình BTTR được huấn luyện trên tập dữ liệu chuẩn **CROHME 2014/2016** (Competition on Recognition of On-line Handwritten Mathematical Expressions).  
Kiểm tra trực tiếp file từ điển [`models/math_e2e_vocab.json`](../models/math_e2e_vocab.json) (113 tokens):

| Nhóm ký tự | Token hỗ trợ trong CROHME / BTTR | Token còn thiếu |
| :--- | :--- | :--- |
| **Đại số & Giải tích** | `0-9`, `a-z`, `\alpha-\Delta`, `\sin`, `\cos`, `\tan`, `\log`, `\int`, `\sum`, `\lim`, `\sqrt`, `\frac` | Đầy đủ |
| **Môi trường Ma trận** | *Không có* | `\begin`, `\end`, `matrix`, `pmatrix`, `bmatrix`, `vmatrix`, `cases` |
| **Cú pháp Lưới 2D** | `_` (subscript), `^` (superscript) | `&` (phân cách cột), `\\` (xuống dòng ma trận) |
| **Ký hiệu Vector** | `\rightarrow` (mũi tên giới hạn $\lim_{x \to 0}$) | `\vec` (dấu vector trên đầu ký tự), `\widehat` (cung góc) |

### 2.2. Cơ chế Fallback của Attention Decoder
Khi người dùng vẽ ma trận:
1. Encoder nhận diện được 2 nét bao ngoài là `[` và `]`.
2. Vùng trung tâm có 3 số xếp theo phương đứng ($1$ ở trên, $2$ ở giữa, $3$ ở dưới).
3. Do **không gian xác suất đầu ra (Output Logits) không có token `\\` hay `matrix`**, cấu trúc 2 tầng theo phương đứng duy nhất mà Attention Decoder biết biểu diễn là **phân số (`\frac{tử}{mẫu}`)**.
4. Decoder gán số $1$ vào tử, số $3$ vào mẫu, và lướt qua nét số $2$ $\to$ sinh ra `[ \frac{1}{3} ]`.

---

## 3. Khảo sát Dataset Quốc tế Chuyên biệt cho Ma trận & Vector

Khảo sát trên Hugging Face và các bài báo hội nghị (arXiv / CVPR / ICDAR) xác định **2 bộ dataset chuẩn quốc tế** giải quyết trọn vẹn bài toán này:

### 3.1. `deepcopy/MathWriting-human` (Google Research — Tháng 4/2024)
* **Kho lưu trữ:** [Hugging Face — deepcopy/MathWriting-human](https://huggingface.co/datasets/deepcopy/MathWriting-human)
* **Bài báo công bố:** *"MathWriting: A Dataset For Handwritten Mathematical Expression Recognition"* (arXiv:2404.10690).
* **Quy mô:** ~**230,000 công thức** viết tay thực tế của con người (bao gồm tọa độ nét vector online và ảnh render offline) + tập synthetic bổ trợ.
* **Đặc tính kỹ thuật về Ma trận:**
  - Thiết kế có chủ đích nhằm mở rộng CROHME sang lĩnh vực **Đại số tuyến tính (Linear Algebra)** và **Cơ học lượng tử**.
  - Hỗ trợ đầy đủ cú pháp: `\begin{matrix} ... \end{matrix}`, `&`, `\\`.
  - Chuẩn hóa delimiters: ví dụ $\begin{bmatrix} a & b \\ c & d \end{bmatrix}$ được biểu diễn chuẩn là `[ \begin{matrix} a & b \\ c & d \end{matrix} ]`.

### 3.2. `ConstantHao/HME100K` / `SoyVitou/Latex-Math-HME100K` (HME100K Dataset)
* **Kho lưu trữ:** [Hugging Face — ConstantHao/HME100K](https://huggingface.co/datasets/ConstantHao/HME100K) / [SoyVitou/Latex-Math-HME100K](https://huggingface.co/datasets/SoyVitou/Latex-Math-HME100K)
* **Quy mô:** **100,000 ảnh** công thức viết tay thực tế từ bài tập, bài thi, tài liệu học tập của học sinh/sinh viên.
* **Đặc tính kỹ thuật về Ma trận:**
  - Đầy đủ nhãn ground-truth cho ma trận vuông, ma trận cột, ma trận dòng, hệ phương trình nhiều dòng (`\begin{cases}`).
  - Là benchmark tiêu chuẩn để đánh giá khả năng bóc tách cấu trúc dạng lưới 2 chiều (2D Grid Parsing).

---

## 4. Bảng So Sánh & Đánh Đổi Chiến Lược (Trade-off Analysis)

| Tiêu chí | Mô hình Hiện tại (CROHME Pretrained) | Hướng nâng cấp bằng `MathWriting` / `HME100K` |
| :--- | :--- | :--- |
| **Quy mô Dữ liệu** | ~10,000 công thức | 100,000 – 230,000 công thức |
| **Số lượng Token (Vocab)** | 113 tokens | ~180 – 250 tokens (bổ sung `\begin`, `\end`, `matrix`, `&`, `\\`, `\vec`) |
| **Khả năng nhận diện Ma trận** | 0% (ép về phân số) | Hỗ trợ đầy đủ ma trận $n \times m$, vector, hệ phương trình |
| **Dung lượng Weights ONNX** | 24.92 MB (Encoder: 12.09 MB, Decoder: 12.83 MB) | Dự kiến: ~25.5 – 28 MB (chỉ tăng nhẹ ở tầng Linear Projection cuối) |
| **Độ trễ CPU dự kiến** | ~30 ms / công thức ngắn | ~40 – 80 ms / công thức ma trận nhiều phần tử |
| **Ràng buộc phần cứng để train** | Đã hoàn tất (dùng weights đóng gói sẵn) | Cần môi trường GPU (Colab Pro / Kaggle GPU / Local RTX) để fine-tune Decoder |

---

## 5. Lộ Trình Tích Hợp Đề Xuất (3 Giai đoạn)

1. **Giai đoạn 1 — Chuẩn bị Dữ liệu & Mở rộng Vocabulary:**
   - Tải subset chuyên về Ma trận và Vector từ `MathWriting` và `HME100K`.
   - Mở rộng vocabulary từ 113 tokens lên ~180 tokens (thêm các token môi trường ma trận và cú pháp xuống hàng/cột).
2. **Giai đoạn 2 — Transfer Learning / Fine-tuning Decoder:**
   - Giữ nguyên (Freeze) phần lớn các tầng feature extraction của DenseNet Encoder (vì đặc trưng nét vẽ toán học cơ bản đã học tốt).
   - Huấn luyện tiếp (Fine-tune) tầng Attention Decoder trên tập dữ liệu ma trận mới.
   - Xuất ONNX đồ thị động mới bằng pipeline `export_decoder.py` đã hoàn thiện.
3. **Giai đoạn 3 — Đo lường Định lượng & Cập nhật App:**
   - Đo lường thực nghiệm: Latency (ms), RAM Working Set (MB), File size (MB).
   - Kiểm thử thực tế trên các mẫu ma trận viết tay: $\begin{bmatrix}1\\2\\3\end{bmatrix}$, $\begin{pmatrix}a&b\\c&d\end{pmatrix}$, $\vec{v}$.
   - Cập nhật bộ cài đặt và tài liệu kỹ thuật.
