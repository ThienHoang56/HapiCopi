# DANH MỤC CÔNG VIỆC TINH GỌN & ĐÁNH BÓNG (TASKS)

- [x] **Mục 1: Tinh gọn Math Editor & Khắc phục lặp nút Ribbon / Sidebar (`matheditor.html`)**
  - [x] 1.1. Chuyển đổi các thẻ ở Sidebar bên phải từ các ký hiệu lặp (`a/b`, `√`, `∑`, `∫`, `lim`) thành **6 Mẫu công thức toán hoàn chỉnh (Equation Presets)**:
    - Phương trình bậc hai: $x = \frac{-b \pm \sqrt{b^2-4ac}}{2a}$
    - Tích phân từng phần: $\int u \, dv = uv - \int v \, du$
    - Hệ 2 phương trình: $\begin{cases} a_1 x + b_1 y = c_1 \\ a_2 x + b_2 y = c_2 \end{cases}$
    - Chuỗi Taylor: $\sum_{n=0}^{\infty} \frac{f^{(n)}(a)}{n!}(x-a)^n$
    - Định lý Pythagoras: $a^2 + b^2 = c^2$
    - Phân phối Gauss / Xác suất thống kê
  - [x] 1.2. Đảm bảo Ribbon trên cùng phụ trách toàn bộ ký hiệu nguyên tử (toán tử, ma trận, quan hệ, hy lạp, ngoặc), không còn trùng lặp vai trò.

- [x] **Mục 2: Xây dựng Tính năng Tạo Template Mẫu riêng Tự động (Auto Template Generator)**
  - [x] 2.1. Xóa hoàn toàn badge `Coming Soon`, biến thẻ `+ Thêm mẫu riêng` và nút `+ Thêm` ở Mini thành tính năng thực tế.
  - [x] 2.2. Modal lưu mẫu tinh gọn: Xem trước công thức, ô nhập tên mẫu, nút `Tự động thay số thành placeholder (#?)`.
  - [x] 2.3. Cơ chế lưu trữ 2 lớp bền vững: `localStorage` (client) + `custom_templates.json` qua C# backend bridge.
  - [x] 2.4. Render động template cá nhân vào Sidebar và Quick Row: chèn 1-click, có nút icon thùng rác để xóa mẫu không còn dùng.

- [x] **Mục 3: Chuẩn hóa Modal Hướng dẫn & Phím tắt (`matheditor.html`)**
  - [x] 3.1. Đổi icon emoji 📖 sang icon SVG nét rỗng (outline/stroke), chuẩn đồng nhất toàn app.
  - [x] 3.2. Xóa tiêu đề phụ dài dòng vô nghĩa: *"Gõ phím chuẩn Microsoft Word Equation & MathType"*.
  - [x] 3.3. Dọn dẹp bảng phím tắt: xóa cú pháp raw `(\(\times\))`, chia 2 cột rõ ràng, chuẩn xác với cách gõ phím của MathLive.
  - [x] 3.4. Rút gọn phím tắt điều khiển và mẹo AI thành các dòng ngắn gọn, dễ hiểu trong 3 giây.
  - [x] 3.5. Đổi footer thành `HapiCopi v1.2.5`.

- [x] **Mục 4: Tái thiết kế Clipboard History dạng Mini Popup (`ClipboardHistoryForm.cs`)**
  - [x] 4.1. Thu nhỏ kích thước từ $1000 \times 640\text{ px}$ xuống $480 \times 440\text{ px}$.
  - [x] 4.2. Chuyển sang bố cục Card List 1 cột hiện đại: hiển thị thời gian, nguồn app, trích đoạn nội dung, badge bảo mật.
  - [x] 4.3. Thao tác dán/copy 1-click hoặc Enter, tự đóng form.
  - [x] 4.4. Ẩn/loại bỏ bảng Inspector nhị phân cồng kềnh khỏi UI người dùng.

- [x] **Mục 5: Tinh gọn Settings Form (`SettingsForm.cs`)**
  - [x] 5.1. Thu nhỏ kích thước về $480 \times 480\text{ px}$, giao diện không cần cuộn trang dài.
  - [x] 5.2. Xóa bỏ các đoạn văn giải thích dài dòng 3–4 dòng, chuyển thành checkbox/toggle tự giải thích (self-explanatory).
  - [x] 5.3. Ô nhập từ khóa tùy chỉnh gọn gàng, placeholder trực quan.

- [x] **Mục 6: Tinh gọn Update Form (`UpdateForm.cs`)**
  - [x] 6.1. Thu nhỏ kích thước về $460 \times 380\text{ px}$.
  - [x] 6.2. Chuyển sang Card trạng thái phiên bản tinh gọn: Badge v1.2.5, Trạng thái (đã mới nhất / có bản mới), nút Kiểm tra / Cập nhật.
  - [x] 6.3. Bỏ các control quản trị phức tạp (Rollback, LAN feed...) khỏi màn hình chính.

- [x] **Mục 7: Kiểm thử, Đo đạc định lượng & Sẵn sàng Deploy**
  - [x] 7.1. Build & chạy 4 bộ benchmark: `--test-e2e-math`, `--test-ai-math`, `--test-distill`, `--test-topo`.
  - [x] 7.2. Kiểm tra biên dịch 0 Error, độ trễ và tài nguyên RAM đáp ứng tiêu chuẩn khắt khe.
  - [x] 7.3. Xác nhận UI đạt chuẩn mượt mà, responsive, không rò rỉ RAM, sẵn sàng deploy.
