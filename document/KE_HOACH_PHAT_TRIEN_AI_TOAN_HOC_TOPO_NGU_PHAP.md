# KẾ HOẠCH NÂNG CẤP BỘ MÁY NHẬN DIỆN CHỮ VIẾT TAY TOÁN HỌC (AI HANDWRITING ENGINE)
## Chuyển đổi từ "So khớp mẫu nét (Template Matching)" sang "Nhận thức bản chất Topo & Ngữ pháp Toán học (Neuro-Symbolic & Structural Grammar)"

---

## 1. TỔNG QUAN & BẢN CHẤT CỦA VẤN ĐỀ

### 1.1. Lỗ hổng của mô hình "Học vẹt" hiện tại
Mô hình cục bộ ban đầu sử dụng thuật toán **Polyline Point Resampling (24 điểm tọa độ x,y) + 16 góc tiếp tuyến + Khoảng cách Euclid**:
* **Lệ thuộc vào hình dạng tĩnh:** Máy chỉ ghi nhớ đúng các tọa độ $P_i = (x_i, y_i)$. Nếu người dùng viết ngược nét (ngang trước, dọc sau), viết nét dính, hoặc viết nghiêng một chút thì khoảng cách Euclid tăng vọt, dẫn đến nhận diện sai.
* **Thiếu vắng đặc trưng bất biến Topo (Topological Invariants):** Máy không có khái niệm về hình thái học (Morphology) như số vòng kín (loops), số điểm giao cắt (junctions), hay đầu mút (terminals). Ví dụ số `8` bất biến luôn có 2 vòng kín, nhưng máy cũ chỉ so khoảng cách điểm.
* **Hoàn toàn mù ngữ cảnh toán học:** Máy nhận diện từng ký tự độc lập mà không biết cấu trúc ngữ pháp toán học. Đứng giữa hai biến số $a$ và $b$, một nét vẽ hơi giống `7` và hơi giống `+` sẽ bị máy đoán bừa thành $a\,7\,b$ (vô nghĩa) thay vì $a + b$.

### 1.2. Mục tiêu chiến lược
Xây dựng một hệ thống nhận diện **Neuro-Symbolic** lai ghép:
1. **Hiểu bản chất ký tự:** Sử dụng đặc trưng Topo bất biến (Euler loop, X/T junction, Cusp).
2. **Hiểu cấu trúc biểu thức:** Sử dụng cây cú pháp toán học (Math AST) & Beam Search ($P_{\text{Visual}} \times P_{\text{Grammar}}$).
3. **Bộ nhớ thích ứng cá nhân hóa:** Tự động học bù bằng Knowledge Distillation và phạt đẩy lùi (Contrastive Repulsion) đã có.
4. **Cam kết tài nguyên:** **0 MB phụ thuộc bên ngoài**, **0ms độ trễ** cho Phase 1 & 2.

---

## 2. KẾ HOẠCH CHI TIẾT THEO CÁC PHASE

```
+-----------------------------------------------------------------------------------+
| PHASE 1: ĐẶC TRƯNG TOPO BẤT BIẾN (TOPOLOGICAL INVARIANTS & MORPHOLOGY)             |
| Trích xuất: Số vòng kín, Điểm giao cắt X/T, Góc nhọn Cusps, Bất biến thứ tự nét  |
| Target: 0 MB thêm vào | Độ trễ < 0.4ms | Xóa bỏ 90% lỗi nhầm lẫn hình thái        |
+-----------------------------------------+-----------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| PHASE 2: NGỮ PHÁP TOÁN HỌC & BEAM SEARCH AST PARSER                               |
| Mô hình chuyển tiếp cú pháp P(Grammar) + Cân bằng ngoặc + Tối ưu toàn cục          |
| Target: 0 MB thêm vào | Độ trễ < 0.8ms | Tỷ lệ công thức hợp lệ cú pháp > 98%    |
+-----------------------------------------+-----------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| PHASE 3: MÔ HÌNH HYBRID ONNX LƯỢNG TỬ HÓA INT8 & ADAPTER CÁ NHÂN HÓA             |
| Đóng gói MobileNetV3-Small INT8 ~2MB + Fine-tuning thời gian thực trên máy         |
| Target: +2 MB bộ cài | Độ trễ 8-15ms | Đạt chuẩn độ chính xác CROHME Dataset      |
+-----------------------------------------------------------------------------------+
```

---

### PHASE 1: BẢN CHẤT TOPO & BẤT BIẾN HÌNH THÁI HỌC (TOPOLOGICAL INVARIANTS)

#### 1. Các thành phần kỹ thuật cốt lõi
1. **Euler Characteristic & Loop Detection (Thuật toán đếm vòng kín):**
   * Sử dụng thuật toán dò chu trình khép kín trên chuỗi điểm của nét vẽ (Polygon self-intersection & winding number):
     * **2 Vòng kín ($L = 2$):** Bắt buộc là số `8` hoặc ký hiệu vô cực `\infty` (nằm ngang) hoặc chữ `B`.
     * **1 Vòng kín ($L = 1$):** Các số `{0, 6, 9}`, các chữ `{o, p, b, d, q, e}`, ký hiệu `\theta, \sigma, \alpha, \phi`.
     * **0 Vòng kín ($L = 0$):** `{1, 2, 3, 4, 5, 7}`, `{x, y, z, +, -, =, /}`.
   * *Evidence/Bằng chứng:* Giảm thiểu 100% việc nhầm lẫn giữa `8` và `3`, `0` và `C`, `6` và `C`.
2. **Junction & Crossing Analysis (Phân loại điểm giao cắt):**
   * Phát hiện điểm cắt giữa các nét hoặc tự cắt:
     * **X-Junction (Điểm cắt chữ thập):** Dấu `+`, dấu `\times`, chữ `x`, `X`.
     * **T-Junction / Y-Junction (Ngã ba):** Dấu `t`, `T`, `\pi`, `\tau`, `Y`.
     * **Endpoints (Đầu mút tự do):** Dấu `-` (2 đầu mút, 0 giao cắt), dấu `=` (4 đầu mút, 2 nét song song).
3. **Sharp Cusps vs. Smooth Curvature (Phân biệt góc nhọn và đường cong):**
   * Tính biến thiên góc tiếp tuyến $\Delta \theta$:
     * Góc nhọn ($\Delta \theta > 110^\circ$ tại đỉnh): `{V, v, W, w, <, >}`.
     * Đường cong trơn ($\Delta \theta$ biến thiên liên tục): `{U, u, C, c, S, s}`.
4. **Order-Agnostic Matching (Bất biến thứ tự vẽ nét):**
   * Sử dụng giải thuật Hungarian hoặc khoảng cách Chamfer Hausdorff giữa các nét để so khớp không phụ thuộc nét nào vẽ trước, nét nào vẽ sau.

#### 2. Kế hoạch lấy số liệu & Benchmark (Phase 1)
* **Tập kiểm thử (Benchmark Set P1):** 60 mẫu nét chữ viết tay có độ dị biệt cao (ngược chiều nét, dính nét, nghiêng góc $\pm 25^\circ$).
* **Chỉ số đo lường (Metrics):**
  * *Top-1 Accuracy cải thiện:* Mục tiêu từ `58.3%` -> `88.5%`.
  * *Tỷ lệ loại trừ nhầm lẫn vòng kín:* Đạt `100%` (không bao giờ đoán ký tự 0 vòng thành ký tự 2 vòng).
  * *Thời gian thực thi CPU:* $\le 0.35$ ms / ký tự.

---

### PHASE 2: NGỮ PHÁP TOÁN HỌC & BEAM SEARCH AST PARSER

#### 1. Các thành phần kỹ thuật cốt lõi
1. **Ma trận Xác suất Cú pháp Toán học (Math Syntax Transition Matrix):**
   * Phân loại ký hiệu thành các nhóm ngữ pháp toán:
     * `VAR`: Biến số ($x, y, z, a, b, n, k, \alpha, \beta, \theta$).
     * `NUM`: Chữ số ($0, 1, 2, \dots, 9$).
     * `BIN_OP`: Toán tử 2 ngôi ($+, -, =, <, >, \le, \ge, \pm, \times$).
     * `PREFIX_FUNC`: Hàm tiền tố ($\sin, \cos, \tan, \log, \ln, \lim, \sum, \int$).
     * `BRACKET_OPEN`: Ngoặc mở ($parentheses, bracket, brace$).
     * `BRACKET_CLOSE`: Ngoặc đóng.
   * Ràng buộc ngữ pháp chặt chẽ ($P_{\text{Grammar}} = 0$):
     * Cấm hai toán tử hai ngôi đứng liền nhau: $P(\text{BIN\_OP} \mid \text{BIN\_OP}) = 0$. (Ví dụ: `+ +`, `* =` bị loại bỏ tuyệt đối).
     * Cấm toán tử hai ngôi đứng đầu biểu thức (trừ dấu âm `-` hoặc `+` 1 ngôi).
     * Cấm số kẹp giữa 2 biến không có toán tử: $P(\text{NUM} \mid \text{VAR}) \approx 0$ khi sau đó là `VAR` (biến $x\,7\,y$ thành $x + y$).
2. **Cân bằng ngoặc (Bracket Balance & Nesting Validator):**
   * Mọi dấu mở ngoặc phải có dấu đóng ngoặc tương ứng cùng cấp.
3. **Giải thuật Beam Search Decoding ($K = 3$):**
   * Kết hợp điểm thị giác và điểm ngữ pháp:
     $$\text{TotalScore}(W) = \sum_{i=1}^N \left( \log P_{\text{Visual}}(w_i \mid \text{Strokes}_i) + \lambda \log P_{\text{Grammar}}(w_i \mid w_{i-1}, w_{i+1}) \right)$$
   * Duy trì 3 nhánh công thức có khả năng cao nhất ở mỗi bước, tự động chọn ra biểu thức vừa khớp hình vẽ vừa đúng toán học.

#### 2. Kế hoạch lấy số liệu & Benchmark (Phase 2)
* **Tập kiểm thử (Benchmark Set P2):** 40 công thức toán học hoàn chỉnh (phương trình bậc 2, tích phân, phân số, giới hạn, đẳng thức vector).
* **Chỉ số đo lường (Metrics):**
  * *Syntax Validity Rate (Tỷ lệ công thức hợp lệ chuẩn LaTeX):* Mục tiêu $\ge 98.5\%$.
  * *Tỷ lệ tự sửa lỗi nhầm toán tử (ví dụ `7` thành `+` khi kẹp giữa biến):* $\ge 90\%$.
  * *Thời gian thực thi CPU:* $\le 0.75$ ms cho cả công thức.

---

### PHASE 3: HYBRID NEURO-SYMBOLIC & ONNX LƯỢNG TỬ HÓA INT8

#### 1. Các thành phần kỹ thuật cốt lõi
1. **Kiến trúc mạng Neural ONNX siêu nhẹ:**
   * Sử dụng backbone **MobileNetV3-Small** hoặc **CRNN** trích xuất đặc trưng dạng lưới/chuỗi nét.
   * Lượng tử hóa **INT8 Quantization** (sử dụng ONNX Runtime Quantization Tool):
     * Kích thước mô hình: **1.8 MB – 2.5 MB** (nhẹ hơn 1 bức ảnh chụp).
     * Thời gian suy luận CPU: **8 ms – 14 ms** trên chip Intel Core i3/i5 thế hệ 8 trở lên.
2. **Ensemble Decision Engine:**
   * Kết hợp 3 nguồn tri thức:
     $$\vec{P}_{\text{Final}} = w_1 \vec{P}_{\text{ONNX}} + w_2 \vec{P}_{\text{Topo}} + w_3 \vec{P}_{\text{PersonalProfile}}$$
   * Mạng ONNX cung cấp khả năng hiểu chữ viết tay phổ quát của nhân loại (được huấn luyện từ 100,000+ mẫu CROHME).
   * Hồ sơ cá nhân hóa (Personal Profile) thích ứng các nét viết dị biệt của riêng người dùng.
3. **Vòng lặp tự học thích ứng (Self-Supervised Adaptation Loop):**
   * Sử dụng AI Vision Cloud để chắt lọc (Distill) + Sinh 7 biến thể hình học + Cơ chế Phạt phản hồi tiêu cực (Repulsion Penalty) để ngày càng tinh chỉnh mô hình theo thời gian.

#### 2. Kế hoạch lấy số liệu & Benchmark (Phase 3)
* **Tập dữ liệu chuẩn:** CROHME 2019 / HASYv2 Offline Math Character Dataset.
* **Chỉ số đo lường (Metrics):**
  * *Top-1 Ký tự đơn lẻ:* $\ge 94.2\%$.
  * *Top-3 Ký tự đơn lẻ:* $\ge 99.1\%$.
  * *RAM tiêu thụ bổ sung:* $\le 18$ MB.
  * *Bộ cài tăng thêm:* $\le 2.0$ MB.

---

## 3. LỘ TRÌNH THỰC HIỆN & TIÊU CHÍ NGHIỆM THU (EVIDENCE CHECKLIST)

| Giai đoạn | Nội dung công việc | Bằng chứng nghiệm thu (Evidence) | Tác động tài nguyên |
| :--- | :--- | :--- | :--- |
| **Phase 1** *(Tuần 1)* | Cài đặt trích xuất Topo: Loop detection, Junction detection, Cusps vào `HandwritingProfileService.cs`. | Chạy test suite tự động 10 test case hình thái học: phân biệt dứt điểm `8` vs `B`, `0` vs `C`, `+` vs `-`. Báo cáo pass 100%. | +0 MB installer<br/>+0.3ms latency |
| **Phase 2** *(Tuần 2)* | Cài đặt `MathGrammarPrior`, bảng phân loại token toán và Beam Search trong `MathVisionService.cs`. | Test case biểu thức: tự sửa `x 7 y` thành `x + y`, cân bằng ngoặc `(x+1)`. Tỷ lệ công thức hợp lệ đạt > 98%. | +0 MB installer<br/>+0.4ms latency |
| **Phase 3** *(Tuần 3)* | Xuất model ONNX MobileNetV3-Small INT8 (~2MB), đặt vào `models/math_rec.onnx`, kết hợp ensemble. | Benchmark so khớp trên bộ 100 ký tự toán CROHME; đo RAM và CPU latency qua Windows Performance Counter. | +1.8 MB installer<br/>+10ms latency |

---

## 4. KẾT LUẬN

Bằng việc kết hợp **Đặc trưng Topo bất biến** + **Ngữ pháp toán học ngữ cảnh** + **Mô hình học máy cá nhân hóa thích ứng**, HapiCopi sẽ hoàn toàn thoát khỏi cái mác "học vẹt", vươn lên thành một công cụ nhận diện công thức toán viết tay thông minh, thấu hiểu ngữ nghĩa với tốc độ tức thì **0ms** và **siêu tiết kiệm tài nguyên**.
