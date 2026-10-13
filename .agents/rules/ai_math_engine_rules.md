# QUY TẮC KỸ THUẬT: AI NHẬN DIỆN CÔNG THỨC TOÁN (MATH HANDWRITING ENGINE)

> **TƯ DUY CỐT LÕI (NGUYÊN TẮC BẤT DI BẤT DỊCH):**  
> *"Tư duy của mình nói chung là đo lường, định lượng được, thấy được sự đánh đổi tốt nhất cho tổng thể, không phải tốt nhất ở cục bộ. Có số liệu, rõ ràng, cách làm dễ hiểu, make sense."*

---

## 1. Bản chất bài toán & Đánh đổi chiến lược (Strategic Trade-offs)

1. **Phục vụ duy nhất 1 người dùng (Single-User Domain):**
   - Chạy 100% offline cục bộ trên máy, **không** phục vụ đại trà với hàng triệu nét chữ khác nhau.
   - Giới hạn 1 người giúp thu hẹp không gian bài toán, cho phép mô hình siêu nhẹ mà vẫn cực kỳ chuẩn xác.
2. **Tiêu chí định lượng bắt buộc cho 1 người:**
   - **Độ chính xác đo lường được (Measurable Accuracy):** 90% – 95% trên tập dữ liệu thực nghiệm.
   - **Tỷ lệ báo giả rất thấp (Low False Alarm):** Không đoán mò, không nhận diện sai gây ức chế.
   - **Dung sai viết vội (Intra-Personal Generalization):** Nhận thức được cấu trúc nét cá nhân khi viết vội/viết ẩu, không overfit cứng nhắc vào pixel.
3. **Ràng buộc tài nguyên (Resource Constraints):**
   - **Kích thước mô hình:** Vài trăm KB đến vài MB (Hiện tại: `models/math_rec.onnx` chỉ 633.3 KB). Cấm đưa mô hình cồng kềnh hàng chục/hàng trăm MB vào repo.
   - **Độ trễ suy luận (Latency):** < 15 ms / ký tự trên CPU (Thực tế đạt ~0.35–0.45 ms / ký tự).
   - **RAM / CPU tiêu thụ:** Ước lượng 5% – 8% tài nguyên máy (Working set < 120 MB; thực tế đạt ~69 MB). Mọi khẳng định phải đo đạc (profiling) thực tế, không võ đoán.

---

## 2. Kiến trúc 3 Tầng phân cấp (Hierarchical Recognition Tiers)

1. **Tầng 1 — Nhận diện số cơ sở:** $0, 1, 2, \dots, 9$.
2. **Tầng 2 — Ký tự toán học & Toán tử:**
   - Biến số: $x, y, z, a, b, \dots$
   - Toán tử: $+$, $-$, $=$, $\times$, $/$, $\dots$
   - Ký hiệu Hy Lạp & hàm số: $\alpha, \beta, \pi, \sin, \cos, \log, \dots$
3. **Tầng 3 — Quan hệ cấu trúc 2D & Ngữ pháp không gian (Spatial Grammar):**
   - **Chỉ số trên/dưới (Superscript/Subscript):** $x^2, a_i$. Baseline tracking tự động đưa nét sau toán tử về trục chính (vd: $x^2 + y^2$).
   - **Phân số (Fractions):** Vạch ngang ngăn cách tử và mẫu số ($\frac{a}{b}$).
   - **Căn thức (Radicals / Square Root):** Bao bọc biểu thức con bên dưới vạch ngang căn ($\sqrt{x}$).
   - **Vector & Ma trận:** Mũi tên trên đầu ($\vec{v}$) và mảng 2 chiều lồng ngoặc ($\begin{pmatrix} a & b \\ c & d \end{pmatrix}$).
   - **Ngữ pháp ngữ cảnh (`MathSyntaxPrior`):** Sửa lỗi nhầm lẫn toán tử giữa các biến (vd: $x\,7\,y \rightarrow x + y$), khử toán tử lặp, tự cân bằng ngoặc mở/đóng.

---

## 3. Cơ chế Tự học Thích ứng Cá nhân hóa (Continuous Adaptation)

1. **Hybrid Blending:** Ưu tiên 75% trọng số cho nét người dùng đã dạy (`HandwritingProfileService`), hòa trộn 50-50 với mô hình ONNX đối với các ký hiệu phổ thông.
2. **Data Augmentation:** Tự sinh 7 biến thể hình học (Shear, Rotation, Scale, Jitter) cho mỗi nét người dùng huấn luyện.
3. **Contrastive Negative Repulsion:** Khi người dùng sửa kết quả sai (nhận nhầm A thành B), hệ thống tự động đẩy lùi vector đặc trưng sai (`PenalizeWrongPrediction`) và tăng trọng số cho nhãn đúng.

---

## 4. Quy tắc Kiểm thử & Báo cáo

- Mọi thay đổi thuật toán toán học bắt buộc phải chạy benchmark:
  ```powershell
  dotnet run -- --test-ai-math
  dotnet run -- --test-distill
  dotnet run -- --test-topo
  ```
- Kết quả benchmark phải ghi vào [`testing/reports/`](../../testing/reports/), **tuyệt đối không xả file ra thư mục gốc**.
