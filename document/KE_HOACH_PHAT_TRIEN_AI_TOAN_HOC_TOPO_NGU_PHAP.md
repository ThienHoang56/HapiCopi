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

### PHASE 1: TRÍCH XUẤT ĐẶC TRƯNG HÌNH HỌC & THẨM ĐỊNH CHỐNG ĐẦU ĐỘC (LLM-AS-A-JUDGE)

> [!IMPORTANT]
> **BÀI HỌC VỀ RULE-BASE HÌNH THÁI HỌC CỨNG NHẮC (DEPRECATED):**  
> Việc dùng các luật cứng (`L = 2 bắt buộc là số 8`, `L = 1 bắt buộc là số 0, 6, 9`) là **ngây thơ (naive)** và rất nguy hiểm:
> 1. **Gạt bỏ nét thật:** Người dùng viết nhanh thì số `0` hoặc `8` thường bị hở đuôi 2-3 pixel $\rightarrow$ Rule-base đếm ra 0 vòng và loại bỏ nhầm mẫu nét tự nhiên.
> 2. **Đầu độc dữ liệu (Data Poisoning):** Người dùng có thể vẽ vòng tròn nhưng gõ nhãn là số `1` hoặc vẽ bậy. Rule-base không có nhận thức thị giác ngữ nghĩa toàn cục nên không thể phát hiện.
> 
> **CHIẾN LƯỢC MỚI (MAKE SENSE & ĐỊNH LƯỢNG):**  
> - **Client (Local):** Chỉ trích xuất vector đặc trưng hình học thuần túy (Polyline Resampling + Chamfer distance) phục vụ so khớp mềm, không dùng rule-base gạt bỏ nét.
> - **Server / Pipeline Thẩm định:** Sử dụng **Google Gemini Flash (LLM-as-a-Judge)** duyệt theo batch (`scripts/validate_crowd_data.py`).
> - **Chi phí cực thấp:** Gom 20 mẫu nét thành 1 ảnh, chi phí thẩm định chưa tới **0.83 VNĐ / mẫu**, trích trực tiếp từ tiền lãi bản quyền Pro để tạo ra tập **Gold Dataset** sạch 100%, loại bỏ triệt để troll và spam.

#### 1. Các thành phần kỹ thuật cốt lõi
1. **Trích xuất đặc trưng hình học bất biến:**
   * Lấy mẫu đều điểm (Resampling 24 points), chuẩn hóa khung bao và góc tiếp tuyến.
   * Tính toán khoảng cách Hausdorff Chamfer không phụ thuộc thứ tự vẽ nét.
2. **Hàng đợi Telemetry phi tập trung:**
   * Bản Free tự động lưu tạm nét vẽ vào hàng đợi offline (`telemetry_queue.json`).
   * Đồng bộ ngầm lên Cloudflare Worker khi có mạng, không ảnh hưởng UI.
3. **Pipeline thẩm định chống đầu độc (`scripts/validate_crowd_data.py`):**
   * Đọc dữ liệu từ hàng đợi / database.
   * Gọi Gemini Flash kiểm định thị giác: Phát hiện vẽ bậy, gán nhãn sai, tự động chuẩn hóa nhãn sang mã LaTeX chuẩn.
   * Xuất tập dữ liệu sạch đưa vào pipeline huấn luyện mô hình ONNX.

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
