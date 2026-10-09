# HapiCopi Lite - Minimal Core Prototype

> **Bản prototype cô đọng toàn bộ tinh hoa cơ chế lõi của HapiCopi trong ~400 dòng code C#**.  
> Được thiết kế đặc biệt để bạn dễ dàng đọc hiểu, giải thích cấu trúc cho giảng viên/hội đồng, hoặc dùng làm nền tảng phát triển tiếp.

---

## 1. Cấu trúc thư mục

Toàn bộ dự án nằm gọn trong 4 file C# tinh gọn:

| File | Số dòng | Chức năng cốt lõi |
| :--- | :--- | :--- |
| [`Program.cs`](./Program.cs) | ~180 dòng | Entry point, System Tray Icon, Win32 Clipboard Listener (`AddClipboardFormatListener`), Low-Level Keyboard Hook (`WH_KEYBOARD_LL`). |
| [`DlpScanner.cs`](./DlpScanner.cs) | ~120 dòng | Bộ quét an ninh cục bộ: Regex (API Key, CCCD, Phone, Email) + Shannon Entropy (Mật khẩu/Secret) + Thuật toán Luhn (Thẻ tín dụng) + Che dữ liệu tự động. |
| [`AutoFormatter.cs`](./AutoFormatter.cs) | ~110 dòng | Tự động nhận diện Markdown, tạo bảng biểu kẻ ô chuẩn Office, đóng gói `CF_HTML` (tính toán offset byte), nối dòng PDF gãy. |
| [`ActiveAppDetector.cs`](./ActiveAppDetector.cs) | ~60 dòng | Gọi Win32 API (`GetForegroundWindow`) để nhận diện ứng dụng đang mở (Word, Docs, VS Code, Overleaf) phục vụ cho cơ chế JIT Paste. |

---

## 2. Các cơ chế lõi tương đương HapiCopi

1. **Chạy ngầm khay hệ thống (System Tray)**:
   - Không mở cửa sổ thừa thãi, có icon khiên bảo vệ và menu chuột phải (Tạm dừng, Xem log, Thoát).
   - Kiểm tra `Mutex` đảm bảo duy nhất 1 ứng dụng chạy tại một thời điểm.

2. **Bắt sự kiện Clipboard không hao pin (0% CPU)**:
   - Sử dụng Windows API `AddClipboardFormatListener` trên cửa sổ ẩn `HWND_MESSAGE`, chỉ kích hoạt khi có sự kiện copy thực sự, tuyệt đối không dùng vòng lặp `while(true)` hay timer.

3. **Bảo vệ rò rỉ dữ liệu (Local DLP - Zero Network)**:
   - Quét và che giấu tức thì trên RAM:
     - OpenAI API Key (`sk-...`), Gemini (`AIzaSy...`), GitHub (`ghp_...`), AWS (`AKIA...`), Bearer tokens.
     - Số điện thoại VN (`03x, 05x, 07x, 08x, 09x`), Email, CCCD 12 số.
     - Số thẻ ngân hàng (đã kiểm tra thuật toán Luhn).
     - Mật khẩu & Token bí mật ngẫu nhiên theo công thức **Shannon Entropy**.
   - Bắn thông báo Windows Balloon Tip cảnh báo người dùng ngay khi phát hiện.

4. **Biến đổi định dạng sang Office (Auto-Format CF_HTML)**:
   - Chuyển đổi Markdown chứa bảng biểu, heading, code block sang định dạng clipboard chuẩn `CF_HTML` của Windows.
   - Khi dán vào Microsoft Word hay Google Docs, tự động tạo thành **bảng kẻ ô viền đẹp mắt, typography chuẩn Office**.

5. **Tự động vá lỗi bẻ dòng PDF (PDF Line Healer)**:
   - Nhận diện văn bản copy từ file PDF, tự động nối lại các câu bị bẻ gãy dòng vô cớ, loại bỏ số trang rác và sửa lỗi ký tự dính phông (`fi`, `fl`).

6. **Nhìn mặt gửi vàng khi dán (JIT Context Adaptation)**:
   - Khi người dùng bấm `Ctrl + V`, ứng dụng soi cửa sổ tiền cảnh:
     - Dán vào **MS Word / Google Docs**: Nạp Rich HTML bảng kẻ ô.
     - Dán vào **VS Code / Notepad**: Nạp Plain-Text thuần để không làm hỏng mã nguồn code.
     - Dán vào **Overleaf / TeXmaker**: Tự động chuyển bảng HTML thành mã bảng `\begin{table}\begin{tabular}...`.

---

## 3. Cách chạy thử nghiệm ngay

Bản biên dịch sẵn (Publish) đã được tạo sẵn tại thư mục [`publish/`](./publish/):

1. Mở thư mục `publish/` và chạy file:
   ```cmd
   HapiCopiLite.exe
   ```
2. Bạn sẽ thấy biểu tượng chiếc khiên xanh xuất hiện dưới góc phải màn hình (System Tray).
3. **Thử nghiệm 1 (DLP)**: Copy chuỗi sau:
   ```text
   sk-proj-abc12345678901234567890def
   ```
   $\rightarrow$ Dán vào Notepad, bạn sẽ thấy nó đã bị che thành `sk-p****def` kèm thông báo bảo vệ.
4. **Thử nghiệm 2 (Markdown sang Word)**: Copy bảng Markdown sau:
   ```markdown
   | Mục | Số lượng | Đơn giá |
   | :--- | :--- | :--- |
   | Laptop Dell | 2 | 18.000.000đ |
   | Bàn phím cơ | 5 | 1.200.000đ |
   ```
   $\rightarrow$ Dán vào Microsoft Word hoặc Google Docs, bạn sẽ thấy nó lập tức biến thành một bảng biểu kẻ ô chuyên nghiệp!
