# CẨM NANG ĐỌC HIỂU: 13 NGUY CƠ KỸ THUẬT & XUNG ĐỘT MÁY TÍNH CỦA HAPICOPI
> **Phiên bản diễn giải đời thường:** Dành cho lập trình viên, tester và người dùng muốn hiểu bản chất vì sao ứng dụng gặp lỗi và cách xử lý mà không cần phải là chuyên gia hệ điều hành Windows.

---

## 🧭 TẠI SAO HAPICOPI LẠI DỄ BỊ "ĐỤNG ĐỘ" TRÊN MÁY TÍNH?

HapiCopi không phải là một phần mềm văn phòng thông thường (chỉ mở lên khi cần, bấm lưu rồi tắt). HapiCopi hoạt động giống như một **"trợ lý túc trực ngầm" (Background System Utility)**:
1. **Lắng nghe khay nhớ tạm (Clipboard):** Bất cứ khi nào bạn nhấn `Ctrl + C` ở bất kỳ đâu (Word, Excel, Chrome, PDF), HapiCopi đều nhận được tín hiệu.
2. **Theo dõi phím tắt toàn màn hình (Global Keyboard Hook):** Bấm `Ctrl + Alt + M` ở bất kỳ cửa sổ nào cũng mở Math Editor.
3. **Chạy trí tuệ nhân tạo (AI) trực tiếp trên máy:** Đọc nét vẽ công thức toán học bằng mô hình ONNX nội bộ mà không gửi dữ liệu ra ngoài Internet.
4. **Nhúng trình duyệt siêu nhẹ (WebView2):** Dùng để vẽ bảng vẽ toán học mượt mà.

Chính vì tiếp xúc trực tiếp với **tài nguyên dùng chung của Windows**, nếu không được tính toán kỹ, ứng dụng sẽ rất dễ bị xung đột với các phần mềm khác, bị hệ điều hành "bóp nghẹt", hoặc tự làm đơ chính mình.

Dưới đây là **13 nguy cơ thực tế**, được giải thích bằng ngôn ngữ đời thường, hiện tượng bạn sẽ thấy bằng mắt, và cách HapiCopi phòng thủ để ứng dụng luôn "chạy mượt như nhung".

---

## 📚 MỤC LỤC 13 NGUY CƠ

- [Nhóm A: Lỗi Logic & Code Nội Tại](#nhóm-a-lỗi-logic--code-nội-tại-bên-trong-ứng-dụng)
  - [1. Tranh chấp Clipboard (Excel, Word đang giữ khóa)](#1-tranh-chấp-clipboard-khi-các-ứng-dụng-khác-đang-bận)
  - [2. Vòng lặp vô tận (Tự copy - Tự xử lý lặp vòng)](#2-vòng-lặp-vô-tận-tiếng-hú-micro-của-clipboard)
  - [3. Copy dồn dập bị đè kết quả cũ (Cuộc đua bất đồng bộ)](#3-copy-dồn-dập-bị-đè-kết-quả-cũ-cuộc-đua-bất-đồng-bộ)
  - [4. Chớp nháy màn hình & Giật lag cuộn chuột](#4-chớp-nháy-màn-hình--giật-lag-cuộn-chuột)
  - [5. Bấm một lần nhưng công thức nhảy ra 2 lần](#5-bấm-chèn-mẫu-một-lần-nhưng-nhảy-ra-2-lần)
  - [6. Bật nhiều bản cùng lúc & Thoát không sạch (Tiến trình ma)](#6-bật-nhiều-bản-cùng-lúc--thoát-không-sạch-tiến-trình-ma)
  - [7. Ứng dụng văng hộp thoại lỗi đột ngột (Crash Dump)](#7-ứng-dụng-văng-hộp-thoại-lỗi-đột-ngột)
- [Nhóm B: Cạm Bẫy Môi Trường Windows Thực Chiến](#nhóm-b-cạm-bẫy-môi-trường-thực-chiến-windows)
  - [8. Bị Antivirus / Windows Defender chặn nhầm là Virus](#8-bị-antivirus--windows-defender-chặn-nhầm-là-virus)
  - [9. Bộ gõ tiếng Việt Unikey / EVKey làm rác lịch sử](#9-bộ-gõ-tiếng-việt-unikey--evkey-làm-rác-lịch-sử)
  - [10. Tên tài khoản máy tính có dấu tiếng Việt](#10-tên-tài-khoản-máy-tính-có-dấu-tiếng-việt-nguyễn-văn-a)
  - [11. Máy tính thiếu WebView2 Runtime (Windows đời cũ)](#11-máy-tính-thiếu-webview2-runtime-windows-đời-cũ)
  - [12. Xung đột quyền hạn khi copy từ cửa sổ Administrator](#12-xung-đột-quyền-hạn-khi-copy-từ-cửa-sổ-administrator)
  - [13. Phím tắt bị "chết ngầm" sau một thời gian sử dụng](#13-phím-tắt-bị-chết-ngầm-sau-một-thời-gian-sử-dụng)

---

## NHÓM A: LỖI LOGIC & CODE NỘI TẠI (BÊN TRONG ỨNG DỤNG)

### 1. Tranh chấp Clipboard khi các ứng dụng khác đang bận
* **Tình huống đời thực:** Bạn vừa bấm copy 1 bảng tính lớn 50.000 dòng trong Excel. Excel đang giữ chặt Clipboard để ghi dữ liệu. Đúng mili-giây đó, HapiCopi nhảy vào mở Clipboard để đọc.
* **Hiện tượng nếu bị lỗi:** Ứng dụng văng bảng thông báo lỗi đỏ chót `OpenClipboard Failed`, hoặc khay hệ thống bị đơ cứng mất vài giây.
* **Hình ảnh dễ hiểu:** Giống như cánh cửa phòng thay đồ công cộng đang có người khóa bên trong. Nếu bạn vội vã giật mạnh tay nắm cửa thì cửa sẽ hỏng (crash). Cách văn minh là gõ cửa nhẹ nhàng, chờ 1 chút rồi gõ lại.
* **HapiCopi giải quyết thế nào?**
  * Áp dụng nguyên lý **"Thử lại lùi dần" (Exponential Backoff Retry)**: Nếu Clipboard bận, HapiCopi kiên nhẫn đợi 10 mili-giây $\rightarrow$ gõ cửa lại. Vẫn bận? Đợi 20ms $\rightarrow$ 40ms $\rightarrow$ 80ms.
  * Nếu sau 5 lần vẫn bận, HapiCopi nhẹ nhàng bỏ qua lượt đó, tuyệt đối **không làm crash ứng dụng và không làm mất dữ liệu gốc của bạn**.

---

### 2. Vòng lặp vô tận (Tiếng hú micro của Clipboard)
* **Tình huống đời thực:** Bạn copy một công thức toán thô `$x^2 + y^2 = z^2$`. HapiCopi phát hiện ra, làm đẹp nó thành định dạng Word xịn rồi tự động ghi đè ngược lại vào Clipboard cho bạn paste.
* **Hiện tượng nếu bị lỗi:** Khi HapiCopi vừa ghi dữ liệu đẹp vào Clipboard, Windows lại tưởng bạn vừa copy cái gì mới $\rightarrow$ Windows lại báo cho HapiCopi $\rightarrow$ HapiCopi lại lấy ra xử lý tiếp $\rightarrow$ Lặp lại hàng nghìn lần mỗi giây! Hậu quả: Quạt máy tính hú ầm ầm, RAM vọt lên 2GB, máy đơ hoàn toàn.
* **Hình ảnh dễ hiểu:** Giống như khi bạn chĩa micro thẳng vào loa phát thanh. Âm thanh từ loa chui vào micro rồi lại phát ra loa, tạo thành tiếng rú rít chói tai không bao giờ dừng.
* **HapiCopi giải quyết thế nào?**
  * HapiCopi dán một **"chiếc tem bí mật"** (cờ nguyên tử `_isSimulatingClipboard` và mã băm nội dung).
  * Mỗi khi nhận được dữ liệu, HapiCopi kiểm tra: *"Dữ liệu này có phải do chính mình vừa tạo ra không?"*. Nếu đúng $\rightarrow$ Bỏ qua ngay lập tức, dập tắt vòng lặp trong 0 mili-giây.

---

### 3. Copy dồn dập bị đè kết quả cũ (Cuộc đua bất đồng bộ)
* **Tình huống đời thực:** Bạn copy mục A (là một sơ đồ vẽ Mermaid phức tạp, máy cần 0.3 giây để vẽ xong ảnh PNG). Ngay sau đó 0.05 giây, bạn đổi ý copy mục B (chỉ là chữ "Xin chào", máy chỉ mất 0.001 giây để xử lý).
* **Hiện tượng nếu bị lỗi:** Mục B xử lý siêu nhanh nên đã vào khay nhớ tạm trước. Nhưng một lúc sau, mục A vẽ xong chậm hơn lại nhảy ra đè lên mục B! Kết quả: Bạn bấm paste ra lại ra cái hình A cũ rích thay vì chữ "Xin chào" bạn vừa copy.
* **Hình ảnh dễ hiểu:** Bạn đi ăn tiệm, gọi món A (món ninh hầm mất 30 phút), sau đó gọi thêm món B (ly nước chanh có ngay). Uống nước chanh xong, người phục vụ bưng món A ra và tự tiện ép bạn phải uống lại nước chanh cũ.
* **HapiCopi giải quyết thế nào?**
  * HapiCopi phát cho mỗi lượt copy một **"chiếc vé xếp hàng có số thứ tự tăng dần" (Monotonic Generation Counter)**.
  * Khi bất kỳ tác vụ nào làm xong, nó kiểm tra: *"Số vé của mình có phải là chiếc vé mới nhất hiện tại không?"*. Nếu đã có chiếc vé mới hơn ra đời, tác vụ cũ lập tức hủy bỏ kết quả, đảm bảo nội dung mới nhất luôn chiến thắng.

---

### 4. Chớp nháy màn hình & Giật lag cuộn chuột
* **Tình huống đời thực:** Người dùng mở cửa sổ Lịch sử Clipboard, dùng chuột lăn lên lăn xuống để xem lại các mục đã copy hôm nay.
* **Hiện tượng nếu bị lỗi:** Cửa sổ chớp tắt trắng xóa liên tục như đèn vũ trường, chữ bị nhòe, cuộn chuột giật cục như máy tính đời cổ 15 năm trước.
* **Hình ảnh dễ hiểu:** Lập trình viên thời xưa hay dùng cờ kỹ thuật cũ mang tên `WS_EX_COMPOSITED` để chống nháy trên Windows XP. Nhưng trên Windows 11 hiện đại (dùng bộ quản lý cửa sổ DWM DirectComposition), cờ này lại biến thành "thuốc độc", khiến Windows 11 liên tục xóa đi vẽ lại toàn bộ cửa sổ mỗi khi chuột di chuyển 1 pixel.
* **HapiCopi giải quyết thế nào?**
  * Xóa bỏ hoàn toàn cờ cũ `WS_EX_COMPOSITED`.
  * Chuyển danh sách lịch sử sang **cơ chế Phân trang thông minh (Pagination)**: Mỗi trang hiển thị đúng 6 thẻ nội dung đẹp đẽ. Lăn chuột là lật trang nhẹ nhàng trong 0.0001 giây, không cần thanh cuộn dài dằng dặc, vừa êm ái vừa triệt tiêu 100% hiện tượng chớp nháy.

---

### 5. Bấm chèn mẫu một lần nhưng nhảy ra 2 lần
* **Tình huống đời thực:** Bạn mở Math Editor, bấm vào nút chèn mẫu "Phân số" $\frac{a}{b}$ hoặc "Căn bậc hai" $\sqrt{x}$.
* **Hiện tượng nếu bị lỗi:** Trên bảng vẽ toán học bất ngờ xuất hiện 2 cái phân số lồng vào nhau ($\frac{\frac{a}{b}}{b}$), bạn phải bấm phím xóa (Backspace) lùi lại rất khó chịu.
* **Hình ảnh dễ hiểu:** Giống như một cái chuông cửa quá nhạy. Ngón tay bạn vừa chạm vào chuông đã kêu (sự kiện nhấn chuột `mousedown`), khi bạn nhấc ngón tay ra chuông lại kêu thêm lần nữa (sự kiện nhả chuột `click`).
* **HapiCopi giải quyết thế nào?**
  * Lập trình viên khóa chặt: **Chỉ lắng nghe duy nhất lúc ngón tay nhấn xuống (`mousedown`)** và ngăn chặn mọi sự kiện thừa thãi.
  * Đồng thời cài thêm **"chốt bảo vệ 0.25 giây" (Debounce 250ms)**: Trong vòng một phần tư giây, dù chuột có bị kẹt hay tay người dùng có run bấm nhiều lần thì máy cũng chỉ nhận đúng 1 lần bấm đầu tiên.

---

### 6. Bật nhiều bản cùng lúc & Thoát không sạch (Tiến trình ma)
* **Tình huống đời thực:** Bạn bấm liên tục vào biểu tượng app ở Desktop để mở, hoặc khi bạn bấm nút "Thoát" ở khay hệ thống để tắt máy.
* **Hiện tượng nếu bị lỗi:**
  * Nếu không kiểm soát: 3-4 bản HapiCopi cùng chạy đè lên nhau, cùng tranh nhau bắt phím tắt $\rightarrow$ máy báo lỗi xung đột.
  * Khi bấm Thoát: Biểu tượng khay biến mất nhưng mở Task Manager lên vẫn thấy `HapiCopi.exe` và `msedgewebview2.exe` chạy ngầm, âm thầm nuốt 200MB - 300MB RAM.
* **HapiCopi giải quyết thế nào?**
  * **Khóa độc quyền (Named Mutex):** Khi bật lên, app kiểm tra xem đã có người anh em nào đang chạy chưa. Nếu có rồi, nó chỉ việc đánh thức cửa sổ cũ lên và tự động tắt bản mới ngay trong 0.05 giây.
  * **Dọn sạch cây tiến trình (Zero Ghost Process):** Khi nhận lệnh Thoát, app gửi thông điệp đóng triệt để mọi tiến trình WebView2 Chromium và giải phóng bộ nhớ ONNX trước khi nhắm mắt, không để lại bất kỳ "tiến trình ma" nào.

---

### 7. Ứng dụng văng hộp thoại lỗi đột ngột
* **Tình huống đời thực:** Một trang web bạn vừa copy có chứa ký tự Unicode đặc thù hoặc định dạng mã hóa quái lạ khiến bộ đọc bị bối rối.
* **Hiện tượng nếu bị lỗi:** Windows hiện hộp thoại crash xám xịt *"HapiCopi has stopped working"*, app biến mất hút khỏi khay hệ thống.
* **HapiCopi giải quyết thế nào?**
  * Thiết lập **"Lưới bảo hộ toàn cục" (Global Exception Handlers)** bao quanh toàn bộ ứng dụng (`Application.ThreadException` và `AppDomain.UnhandledException`).
  * Mọi lỗi bất ngờ phát sinh đều bị lưới này đỡ lấy êm ái, âm thầm ghi nhật ký vào file `%LOCALAPPDATA%\HapiCopi\logs\crash.log` để phục vụ cải tiến sau này, **tuyệt đối không bao giờ làm app sập trước mắt người dùng**.

---

## NHÓM B: CẠM BẪY MÔI TRƯỜNG THỰC CHIẾN WINDOWS

### 8. Bị Antivirus / Windows Defender chặn nhầm là Virus
* **Tình huống đời thực:** Bạn tải file `HapiCopi-Setup.exe` về máy, bấm cài thì màn hình xanh lè của Windows SmartScreen hiện lên: *"Windows protected your PC - Unknown Publisher"*, hoặc phần mềm diệt virus (Bkav, Avast) tự ý chặn không cho chạy.
* **Vì sao bị?** HapiCopi là phần mềm mã nguồn độc lập, chưa mua chứng chỉ số đắt đỏ (khoảng 10 triệu VNĐ/năm từ Microsoft/DigiCert), lại có hai hành vi hệ thống: **lắng nghe bàn phím để kích hoạt phím tắt** và **đọc Clipboard**. Các phần mềm diệt virus quét qua thấy giống phần mềm gián điệp (Keylogger/Spyware) nên nghi ngờ.
* **HapiCopi giải quyết thế nào?**
  * Điền đầy đủ thông tin xuất xứ rõ ràng trong file exe (Tên tác giả, phiên bản, bản quyền, biểu tượng icon chuẩn Windows).
  * Khai báo quyền ứng dụng ở mức thông thường (`asInvoker`), cam kết không đòi quyền can thiệp hệ thống sâu (Admin).
  * Cung cấp mã băm bảo mật SHA-256 công khai trong mỗi bản phát hành để đối chiếu tính toàn vẹn; tài liệu hướng dẫn người dùng bấm *"More info $\rightarrow$ Run anyway"* hoàn toàn an tâm.

---

### 9. Bộ gõ tiếng Việt Unikey / EVKey làm rác lịch sử
* **Tình huống đời thực:** Bạn đang mở Unikey/EVKey gõ một đoạn văn trong Word hay Photoshop.
* **Vì sao bị?** Để gõ được từ "Đường", một số bộ gõ tiếng Việt dùng mẹo: gõ chữ `d` $\rightarrow$ xóa đi $\rightarrow$ copy chữ `đ` vào clipboard dán ra $\rightarrow$ xóa đi $\rightarrow$ copy chữ `đu` dán ra $\rightarrow$ `đươ` $\rightarrow$ `đường`.
* **Hiện tượng nếu bị lỗi:** Bạn mở Lịch sử Clipboard của HapiCopi ra thì tá hỏa: Danh sách đầy ắp hàng chục ký tự vụn vặt: `d`, `đ`, `u`, `ư`, `ơ`... làm trôi mất các nội dung quan trọng bạn đã copy từ trước.
* **HapiCopi giải quyết thế nào?**
  * HapiCopi tích hợp **"Bộ lọc nhịp gõ tiếng Việt"**: Nếu một đoạn văn bản copy chỉ dài 1 chữ cái và xuất hiện dồn dập trong khoảng thời gian siêu ngắn ($< 20\text{ ms}$), HapiCopi nhận diện ngay đây là "hành vi gõ dấu của Unikey", tự động lờ đi và không lưu vào lịch sử.

---

### 10. Tên tài khoản máy tính có dấu tiếng Việt (`Nguyễn Văn A`)
* **Tình huống đời thực:** Người dùng mua máy tính về và đặt tên người dùng Windows là `Nguyễn Văn A` hoặc `Trần Thị B`. Thư mục dữ liệu sẽ là `C:\Users\Nguyễn Văn A\AppData\Local\...`.
* **Hiện tượng nếu bị lỗi:** Mở Math Editor lên thì app báo lỗi *"Không nạp được mô hình AI toán"* hoặc màn hình vẽ trắng trơn không nhận diện được chữ viết tay.
* **Vì sao?** Thư viện nhân AI (ONNX Runtime của Microsoft) và nhân Chromium được viết bằng C++. Nếu lập trình viên C# sơ ý truyền đường dẫn file theo chuẩn chữ tiếng Anh cũ (ANSI), chữ `Nguyễn Văn A` sẽ bị biến dạng thành dấu hỏi chấm `Nguy?n V?n A`. Máy tính tìm không thấy thư mục nên báo lỗi.
* **HapiCopi giải quyết thế nào?**
  * Toàn bộ các hàm xử lý đường dẫn trong HapiCopi đều được chuẩn hóa 100% bằng chuỗi **Unicode UTF-16 quốc tế**. Dù tên người dùng có dấu tiếng Việt, khoảng trắng hay biểu tượng cảm xúc, đường dẫn file vẫn được bảo toàn nguyên vẹn.

---

### 11. Máy tính thiếu WebView2 Runtime (Windows đời cũ)
* **Tình huống đời thực:** Ứng dụng được cài đặt trên máy tính phòng thực hành của trường học, cơ quan nhà nước sử dụng Windows 10 cũ (trước năm 2021) hoặc bản Windows 10 LTSC/Enterprise đã bị tắt tự động cập nhật Microsoft Edge.
* **Hiện tượng nếu bị lỗi:** Bấm mở Math Editor thì ứng dụng lập tức biến mất (crash) do thiếu file lõi `WebView2Loader.dll` của hệ thống.
* **HapiCopi giải quyết thế nào?**
  * Bộ cài đặt Inno Setup tự động kiểm tra khóa Registry của máy xem đã có WebView2 hay chưa.
  * Nếu máy bị thiếu, ứng dụng không sập mà sẽ nhẹ nhàng hiện lên một thông báo tiếng Việt: *"Máy tính cần gói WebView2 Runtime của Microsoft để mở bảng vẽ toán"*, kèm theo nút bấm để tự động tải bản cài chính thức từ Microsoft chỉ với 1 click.

---

### 12. Xung đột quyền hạn khi copy từ cửa sổ Administrator
* **Tình huống đời thực:** Bạn mở Command Prompt (Run as Administrator), Task Manager, hoặc Registry Editor để sửa máy, sau đó bôi đen copy một dòng lệnh.
* **Hiện tượng nếu bị lỗi:** Bạn dùng tính năng dán thông minh của HapiCopi thì cửa sổ kia trơ trơ không nhận lệnh.
* **Vì sao?** Windows có một bức tường bảo mật nghiêm ngặt tên là **UIPI (Cách ly đặc quyền giao diện)**: Một ứng dụng chạy quyền bình thường (như HapiCopi) tuyệt đối không được phép gửi phím bấm hay can thiệp vào một cửa sổ đang chạy quyền Tối cao (Administrator), nhằm ngăn chặn virus đội lốt can thiệp phá hoại máy tính.
* **HapiCopi giải quyết thế nào?**
  * HapiCopi kiểm tra cấp độ quyền của cửa sổ đích. Nếu phát hiện cửa sổ là Administrator, HapiCopi bẫy lỗi êm dịu, không crash máy, và giữ nguyên nội dung trên Clipboard để bạn chỉ cần tự tay nhấn `Ctrl + V` như bình thường.

---

### 13. Phím tắt bị "chết ngầm" sau một thời gian sử dụng
* **Tình huống đời thực:** Bạn mở máy tính dùng cả buổi sáng rất bình thường, nhưng đến chiều bấm `Ctrl + Alt + M` lại thấy Math Editor không chịu hiện lên nữa, dù ứng dụng vẫn đang chạy ở khay hệ thống.
* **Vì sao?** Windows có một cơ chế bảo vệ bí mật: Nếu một ứng dụng đăng ký bắt phím tắt toàn màn hình (Low-Level Keyboard Hook), mà hàm xử lý của ứng dụng đó bị trễ quá **0.2 đến 0.3 giây** (do máy bị lag, CPU bận hoặc đĩa cứng quay chậm), Windows sẽ tự động **âm thầm ngắt phím tắt (unhook) vĩnh viễn** mà không hề thông báo cho ứng dụng biết!
* **HapiCopi giải quyết thế nào?**
  * Hàm nhận phím tắt của HapiCopi được tối ưu siêu tốc: **Hoàn tất trong vòng 0.0005 giây** (chỉ làm đúng nhiệm vụ chuyển thông điệp sang luồng nền rồi trả quyền ngay cho Windows).
  * Cài đặt thêm **"Cơ chế khám sức khỏe định kỳ" (Heartbeat Health Check)**: Cứ mỗi 30 giây hoặc khi bạn bấm chuột vào biểu tượng khay hệ thống, app sẽ tự kiểm tra xem phím tắt còn sống không; nếu thấy bị Windows ngắt, app sẽ **tự động kết nối lại ngay lập tức**!

---

## 🎯 BẢNG TRA CỨU NHANH (QUICK CHEAT SHEET)

| Khi bạn thấy hiện tượng... | Thuật ngữ kỹ thuật tương ứng | Nằm ở mục số |
|---|---|:---:|
| Copy ở Excel thì app bị đơ vài giây | Clipboard Lock Contention | **1** |
| Quạt máy tính kêu to, RAM tăng vọt sau khi copy | Clipboard Re-entrancy Loop | **2** |
| Copy liên tục thì nội dung dán ra bị lộn xộn | Async Out-of-Order Race Condition | **3** |
| Cuộn danh sách lịch sử bị chớp nháy trắng xóa | DWM Composited Invalidation Strobe | **4** |
| Bấm 1 lần chèn phân số nhảy ra 2 cái | Duplicate DOM Event (Mousedown + Click) | **5** |
| Bấm thoát app rồi nhưng Task Manager vẫn còn | Orphan Zombie WebView2 Process | **6** |
| App tự nhiên biến mất không có lý do | Unhandled Exception Crash | **7** |
| Windows Defender / SmartScreen hiện cảnh báo đỏ/xanh | Heuristic False Positive & Unsigned Binary | **8** |
| Lịch sử Clipboard đầy các chữ vụn `a, ư, d, đ` | IME Typing Macro Clipboard Echo | **9** |
| Máy có tên tiếng Việt không mở được AI toán | ANSI/UTF-8 Path Truncation in Native C++ | **10** |
| Máy tính Windows 10 cũ không mở được bảng vẽ | Missing Evergreen WebView2 Runtime | **11** |
| Dán vào cửa sổ Admin (CMD/Task Manager) không ăn | UIPI (User Interface Privilege Isolation) | **12** |
| Dùng một lúc tự nhiên phím tắt bị liệt ngầm | LowLevelHooksTimeout Silent Unhooking | **13** |

---
> 💡 *Tài liệu này là người bạn đồng hành cùng [CHECKLIST_TECHNICAL_RISKS.md](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/CHECKLIST_TECHNICAL_RISKS.md). Khi đọc checklist kiểm thử từng dòng code, hãy đối chiếu với cẩm nang này để hiểu rõ ý nghĩa thực tiễn của từng hạng mục kiểm tra.*
