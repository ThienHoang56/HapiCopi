# AGENTS.md — HapiCopi Master Engineering Principles & Project Architecture

> **TƯ DUY CỐT LÕI CỦA NGƯỜI DÙNG (NGUYÊN TẮC BẤT DI BẤT DỊCH):**  
> *"Tư duy của mình nói chung là đo lường, định lượng được, thấy được sự đánh đổi tốt nhất cho tổng thể, không phải tốt nhất ở cục bộ. Có số liệu, rõ ràng, cách làm dễ hiểu, make sense."*

---

## 1. Bản đồ Cấu trúc Dự án (Directory Architecture Map)

Tuyệt đối tuân thủ phân vùng thư mục, **cấm xả file test, file rác, file nháp ra thư mục gốc**:

| Thư mục | Vai trò kỹ thuật | Quy chuẩn can thiệp |
|---------|------------------|---------------------|
| [`.agents/rules/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/.agents/rules) | Các quy chuẩn hoạt động tự động của Antigravity AI IDE | Đọc trước mọi thay đổi (`incident-and-working-rules`, `ai_math_engine_rules`, `music_engine_rules`). |
| [`.AGENT/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/.AGENT) | Tài liệu bàn giao, bài học sự cố và kiến trúc bộ nhớ Agent | Luôn đồng bộ với `.agents/rules/` và kiểm tra trước khi commit/push. |
| [`Core/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/Core) | Bộ máy xử lý lõi backend C# (.NET 10.0 x64 WinForms) | Quản lý clipboard, quét DLP nhạy cảm, suy luận ONNX, tự học nét cá nhân. |
| [`UI/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/UI) | Giao diện điều khiển WinForms & WebView2 Host | Đảm bảo High-DPI (PerMonitorV2), giữ RAM thấp, không đơ UI. |
| [`WebViewResources/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/WebViewResources) | Canvas HTML5/CSS/JS chạy trong WebView2 | Math Canvas vẽ nét thời gian thực, Music Editor, Clipboard History. |
| [`models/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/models) | Trọng số mạng nơ-ron cục bộ đã đóng gói (`math_rec.onnx`) | Tự động copy ra build output, kích thước cố định ~633 KB, không cần Python khi chạy. |
| [`scripts/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/scripts) | Pipeline Python hỗ trợ huấn luyện và export mô hình | Sử dụng uv, PyTorch CPU, xuất mô hình single-file (`dynamo=False`). |
| [`testing/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/testing) | Toàn bộ tài nguyên kiểm thử, fixtures và benchmark | Báo cáo lưu tại [`reports/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/testing/reports), fixtures tại [`diagrams/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/testing/diagrams), ảnh chụp tại [`shots/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/testing/shots). |
| [`document/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/document) | Báo cáo kỹ thuật, lộ trình phát triển và đặc tả nghiệp vụ | Báo cáo AI Toán học, Kế hoạch Music Engine chuẩn MuseScore, Báo cáo Logic Âm thanh. |

---

## 2. Ba Trụ cột Kỹ thuật của HapiCopi (Core Engineering Pillars)

### 2.1. Trụ cột 1: Local Clipboard DLP & Formatting Engine
- **Kiến trúc:** 100% Desktop Offline, chạy ngầm dưới System Tray, tuyệt đối không có máy chủ hay truyền dữ liệu ra Internet.
- **Bảo mật:** Quét PII / mã số thẻ / mật khẩu thời gian thực, lưu trữ cục bộ bảo vệ qua Windows DPAPI.
- **Tự động định dạng:** Chuyển đổi công thức Markdown/LaTeX sang định dạng Word/Office gốc với độ trễ tức thì (< 5 ms).

### 2.2. Trụ cột 2: AI Math Handwriting Engine (Single-User Domain)
- **Đánh đổi chiến lược (Strategic Trade-off):**
  - **Không phục vụ đại trà:** Học trên tập dữ liệu của **duy nhất 1 người dùng**, thu hẹp không gian bài toán cực lớn.
  - **Độ chính xác định lượng:** Đạt **90% – 95%** (đo đạc thực tế bằng benchmark, không võ đoán).
  - **Dung sai viết vội (Intra-Personal Generalization):** Nhận diện nét viết biến thể khi viết nhanh mà không bị overfit pixel.
- **Ràng buộc tài nguyên:**
  - Dung lượng mô hình $\le$ vài MB (Thực tế: `math_rec.onnx` = **633.3 KB**).
  - Độ trễ CPU $\le$ 15 ms/ký tự (Thực tế: **0.35 – 0.45 ms/ký tự**, throughput > 2,000 ký tự/giây).
  - Mức tiêu thụ RAM: Mục tiêu 5–8% tài nguyên (Thực tế: Working set **69.3 MB**).
- **Kiến trúc phân tầng:**
  - **Tầng 1 — Số cơ sở:** $0, 1, 2, \dots, 9$.
  - **Tầng 2 — Ký tự toán học:** Biến số ($x, y, z$), toán tử ($+, -, =, \times$), hàm ($\sin, \cos$), ký hiệu ($\alpha, \pi$).
  - **Tầng 3 — Quan hệ cấu trúc 2D:** Phân số ($\frac{a}{b}$), căn thức ($\sqrt{x}$), số mũ với Baseline Tracking ($x^2 + y^2$), vector ($\vec{v}$), ma trận ($\begin{pmatrix}\dots\end{pmatrix}$), ngữ pháp sửa lỗi `MathSyntaxPrior`.
  - **Cơ chế tự thích ứng:** Tự sinh 7 biến thể hình học cho nét mới, ưu tiên 75% nét cá nhân hóa, phạt đẩy lùi (Contrastive Repulsion) khi nhận diện sai.

### 2.3. Trụ cột 3: Music Engine (ĐÓNG BĂNG TẠM THỜI / ARCHIVED)
- **Trạng thái:** Đã đóng băng và lưu trữ tài liệu vào `.AGENT/archive/music/` và `document/archive/music/`.
- **Định hướng:** Toàn bộ nguồn lực kỹ thuật hiện tại **tập trung 100% vào Trụ cột 2: AI Math Engine**, phần Music Engine sẽ kích hoạt lại sau.

---

## 3. Nguyên tắc Kỹ thuật Bất di Bất dịch (Non-Negotiable Rules)

1. **Đo lường & Định lượng rõ ràng (TRUNG THỰC 100% SỰ THẬT):**
   - Mọi kết luận về hiệu năng, tốc độ, bộ nhớ, độ chính xác phải có **số liệu thực nghiệm đo đạc thực tế**.
   - **TUYỆT ĐỐI CẤM BÁO CÁO LÁO & CẤM DÙNG RULE-BASE GIAN LẬN TEST CASE:** AI là hệ thống xác suất. Test FAIL thì ghi rõ là FAIL. Thà báo cáo kết quả thật bị trượt để cùng nhau tìm giải pháp kiến trúc, tuyệt đối cấm hardcode `if-else` trên nhãn ký tự để làm đẹp test case rồi báo cáo "100% pass giả dối".
   - Chưa đo đạc trên máy thật thì ghi rõ là chưa có số liệu, **tuyệt đối không võ đoán (No Assumptions)**.
2. **Tối ưu Tổng thể thay vì Cục bộ:**
   - Thấy rõ toàn bộ bài toán đánh đổi: Dung lượng $\leftrightarrow$ Độ trễ $\leftrightarrow$ RAM/CPU $\leftrightarrow$ Độ chính xác cá nhân.
   - Không được "vá tạm một lỗi nhỏ" mà làm sụp đổ kiến trúc chung hoặc gây rò rỉ RAM (vụ sự cố Peak RAM 2.58 GB).
3. **Cách làm dễ hiểu, Make Sense:**
   - Code phải tường minh, kiến trúc phân tầng rõ ràng, dễ bảo trì lâu dài.
4. **Quy trình Kiểm thử bắt buộc trước khi kết luận:**
   ```powershell
   dotnet run -- --test-e2e-math  # Kiểm tra mô hình End-to-End Sequence (A4 / Vision Transformer BTTR), RAM, Latency, Output
   dotnet run -- --test-ai-math   # Kiểm tra mô hình ONNX, độ trễ, RAM, 3 tầng toán học (TRUNG THỰC, KHÔNG CHE GIẤU)
   dotnet run -- --test-distill   # Kiểm tra học nét cá nhân, ma trận 2D, phạt nhãn sai
   dotnet run -- --test-topo      # Kiểm tra các bất biến hình thái học
   ```
   Tất cả báo cáo phải xuất vào thư mục [`testing/reports/`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/testing/reports).

---

## 4. Danh mục Quy chuẩn Kỹ thuật bắt buộc (Mandatory Reference Links)

Trước khi can thiệp bất kỳ mã nguồn nào, mọi Agent bắt buộc phải đọc:
* [`.agents/rules/incident-and-working-rules.md`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/.agents/rules/incident-and-working-rules.md): **BẢN GHI SỰ CỐ MẤT NIỀM TIN: CẤM ASSUME, CẤM CODE CHẮP VÁ, CẤM BÁO CÁO LÁO & DÙNG RULE-BASE ĐỐI PHÓ TEST CASE.**
* [`.agents/rules/ai_math_engine_rules.md`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/.agents/rules/ai_math_engine_rules.md): Quy chuẩn kỹ thuật AI Toán học.
* [`.AGENT/architecture.md`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/.AGENT/architecture.md): Kiến trúc HapiCopi Desktop DLP.
* [`document/KE_HOACH_PHAT_TRIEN_AI_TOAN_HOC_TOPO_NGU_PHAP.md`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/document/KE_HOACH_PHAT_TRIEN_AI_TOAN_HOC_TOPO_NGU_PHAP.md): Lộ trình chi tiết bộ máy nhận diện công thức toán.
* [`testing/README.md`](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/testing/README.md): Hướng dẫn kiểm thử và báo cáo định lượng.
