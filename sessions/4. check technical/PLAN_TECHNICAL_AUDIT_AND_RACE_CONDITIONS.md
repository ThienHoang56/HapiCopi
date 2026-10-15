# KẾ HOẠCH KIỂM TRA RỦI RO KỸ THUẬT, XUNG ĐỘT MÁY & RACE CONDITION (TECHNICAL AUDIT PLAN)

> **TƯ DUY KỸ THUẬT CỐT LÕI (NGUYÊN TẮC BẤT DI BẤT DỊCH):**  
> *"Đo lường, định lượng được, thấy được sự đánh đổi tốt nhất cho tổng thể, không phải tốt nhất ở cục bộ. Có số liệu, rõ ràng, cách làm dễ hiểu, make sense. Không thể tránh hết mọi lỗi trên đời, nhưng tuyệt đối không được để xảy ra những lỗi cơ bản nhất: deadlock, crash do race condition, treo khay hệ thống, đè clipboard người dùng, hay xung đột môi trường Windows."*

---

## I. BỐI CẢNH & PHẠM VI KIỂM SOÁT KỸ THUẬT

HapiCopi là ứng dụng desktop chạy ngầm tầng sâu (Low-Level Windows System Hook, Real-Time Clipboard Listener, In-Process ONNX AI Engine, Embedded Chromium WebView2). Đặc thù của mô hình này là **tiếp xúc trực tiếp với các tài nguyên dùng chung của hệ điều hành** (Shared OS Resources).

Nếu không có kiến trúc phòng vệ nghiêm ngặt, ứng dụng sẽ rất dễ gặp phải các lỗi kinh điển:
1. **Clipboard Deadlock / Collision:** Nhiều ứng dụng khác (Excel, Word, IDM, Trình duyệt, Password Manager) cùng mở Clipboard một lúc (`OpenClipboard` fail).
2. **Vòng lặp tái kích hoạt vô tận (Clipboard Echo / Infinite Re-entrancy):** HapiCopi định dạng xong ghi vào Clipboard $\rightarrow$ Windows báo `WM_CLIPBOARDUPDATE` $\rightarrow$ HapiCopi lại tưởng có dữ liệu mới $\rightarrow$ Vòng lặp đệ quy làm tràn RAM và đơ máy.
3. **Race Condition khi sao chép dồn dập (Rapid Copy Race):** Người dùng copy mục A, 20ms sau copy mục B. Tác vụ định dạng không đồng bộ của mục A hoàn tất sau mục B $\rightarrow$ Ghi đè ngược dữ liệu cũ lên clipboard hoặc lưu sai thứ tự lịch sử.
4. **Xung đột Khóa File dữ liệu (File I/O Locking):** Quá trình ghi đĩa DPAPI `history.dat` diễn ra đồng thời với thao tác lưu cấu hình `appsettings.json` khi máy bị tắt đột ngột hoặc nhiều luồng ghi cùng lúc.
5. **Xung đột đồ họa DWM / High-DPI:** Xung đột giữa API cổ (`WS_EX_COMPOSITED`) và bộ quản lý cửa sổ hiện đại (DWM DirectComposition trên Windows 11), hoặc sai lệch tỷ lệ khi kéo app qua các màn hình khác DPI (100% $\leftrightarrow$ 150%).
6. **Xung đột tiến trình ngầm (Orphan Process Leak):** Tiến trình `HapiCopi.exe` hoặc các tiến trình con `msedgewebview2.exe` không thoát sạch khi người dùng nhấn Thoát, biến thành tiến trình zombie ngốn tài nguyên.

---

## II. 7 NHÓM NGUY CƠ KỸ THUẬT & CƠ CHẾ PHÒNG VỆ (DEFENSIVE ARCHITECTURE)

```
┌────────────────────────────────────────────────────────────────────────┐
│                   HAPICOPI DEFENSIVE ARCHITECTURE                      │
├────────────────────────────────────────────────────────────────────────┤
│ 1. CLIPBOARD SUBSYSTEM   │ Exponential Backoff Retry + Re-entrancy Guard│
│ 2. CONCURRENCY & ASYNC   │ Monotonic Generation Token + Mutex File I/O │
│ 3. MEMORY & GDI LEAKS    │ Zero-Alloc Paint + GC Trimming on Minimize  │
│ 4. HIGH-DPI & GRAPHICS   │ PerMonitorV2 Aware + No WS_EX_COMPOSITED   │
│ 5. WEBVIEW2 INTEROP      │ EnsureCoreWebView2 Ready + Bridge Quarantine │
│ 6. LOCAL ONNX INFERENCE  │ Thread-Safe Session.Run() + Model Cache     │
│ 7. LIFECYCLE & PROCESSES │ Mutex Single-Instance + Process Tree Cleanup │
└────────────────────────────────────────────────────────────────────────┘
```

---

### 1. Phân hệ Clipboard & Windows Message Loop

| Nguy cơ (Risk) | Cơ chế phát sinh | Hậu quả nếu không chặn | Giải pháp phòng vệ chuẩn mực |
|---|---|---|---|
| **Lỗi `CLIPBRD_E_CANT_OPEN` (0x800401D0)** | Ứng dụng khác đang giữ khóa Clipboard (Word, Excel render OLE, phần mềm gõ tiếng Việt Unikey/EVKey) | `ExternalException: OpenClipboard Failed` $\rightarrow$ Crash app hoặc mất clipboard | Triển khai **Retry với Exponential Backoff**: Thử lại 5 lần, mỗi lần cách nhau $10\text{ ms}, 20\text{ ms}, 40\text{ ms}, 80\text{ ms}$, nếu vẫn bận thì bỏ qua an toàn (Graceful Bailout), không crash. |
| **Vòng lặp vô tận (Re-entrancy Loop)** | Khi HapiCopi ghi nội dung đã format vào clipboard, Windows phát thông điệp `WM_CLIPBOARDUPDATE` mới | App tự kích hoạt chính mình hàng nghìn lần/phút $\rightarrow$ CPU 100%, RAM phình to | Sử dụng cờ nguyên tử `_suppressNextClipboardEvent` hoặc so sánh mã băm SHA-256 / Sequence Number của nội dung vừa ghi. Nếu trùng khớp $\rightarrow$ Bỏ qua lập tức. |
| **Sai lệch STA Thread** | Thao tác Clipboard Windows yêu cầu luồng đơn căn hộ (Single-Threaded Apartment) | `ThreadStateException: Current thread must be set to STA` | Toàn bộ lệnh đọc/ghi Clipboard bắt buộc chạy trên UI Thread thông qua `SynchronizationContext` hoặc `Control.Invoke()`. Tuyệt đối không gọi trực tiếp từ `Task.Run()`. |

---

### 2. Race Condition trong Xử lý Bất đồng bộ (Async Pipeline)

| Nguy cơ (Risk) | Cơ chế phát sinh | Hậu quả nếu không chặn | Giải pháp phòng vệ chuẩn mực |
|---|---|---|---|
| **Đè lấn kết quả sao chép cũ (Out-of-Order Completion)** | Copy A (cần render SVG mất 300ms), ngay sau đó copy B (text thuần mất 5ms). Tác vụ B xong trước, tác vụ A xong sau ghi đè lên B | Dữ liệu B vừa copy bị biến mất, clipboard nhảy ngược về A | Sử dụng **Monotonic Generation Counter (Mã thế hệ tuần tự)**: Mỗi sự kiện copy tăng biến đếm `_currentCopyId++`. Khi tác vụ async hoàn thành, chỉ ghi kết quả nếu `capturedId == _currentCopyId`. |
| **Ghi đĩa DPAPI đồng thời (`history.dat`)** | Người dùng copy liên tục 5 mục trong 200ms, 5 luồng async cùng ghi đè file lịch sử mã hóa | `IOException: The process cannot access the file because it is being used by another process` | Gom các thao tác ghi lịch sử qua cơ chế **Debounce / Channel Batching** hoặc bảo vệ bằng `SemaphoreSlim(1, 1)` / `ReaderWriterLockSlim`. |
| **Tự học AI xung đột với Suy luận** | Quá trình học nét cá nhân (Personalization Distillation) cập nhật vector mẫu trong lúc luồng UI đang gọi `Recognize()` | `InvalidOperationException: Collection was modified during enumeration` | Đồng bộ hóa danh sách mẫu nhận diện bằng `lock` hoặc sử dụng cấu trúc bất biến (Immutable Collections / Copy-on-Write). |

---

### 3. Xung đột Môi trường Màn hình (High-DPI & DWM)

| Nguy cơ (Risk) | Cơ chế phát sinh | Hậu quả nếu không chặn | Giải pháp phòng vệ chuẩn mực |
|---|---|---|---|
| **Chớp nháy màn hình (DWM Strobing / White Flash)** | Sử dụng cờ vẽ cổ `WS_EX_COMPOSITED (0x02000000)` trên Windows 11 | Cửa sổ bị chớp tắt liên tục khi di chuột hoặc lăn bánh xe | **Cấm 100% việc dùng `WS_EX_COMPOSITED`**. Áp dụng phân trang (Pagination) để không cần scrollbar, dùng `DoubleBuffered = true` chuẩn WinForms. |
| **Méo giao diện khi chuyển màn hình (PerMonitorV2)** | Kéo cửa sổ từ màn hình laptop (150% DPI) sang màn hình ngoài (100% DPI) | Chữ bị nhòe, icon bị lệch, nút bị cắt mép | Khai báo `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` trong `.csproj`. Mọi kích thước động tính qua `LogicalToDeviceUnits()`, sử dụng `ClientSize` thay vì `Size`. |
| **Mất cửa sổ ngoài màn hình (Off-screen Orphan)** | Lưu tọa độ khi cắm 2 màn hình, lần sau rút màn hình phụ ra mở app | Cửa sổ hiện ở tọa độ ảo ngoài màn hình, người dùng không bấm được | Trước khi `Show()`, luôn kiểm tra `Screen.FromPoint(loc)` và `SystemInformation.VirtualScreen.Contains(loc)`. Nếu nằm ngoài $\rightarrow$ Tự động reset về `StartPosition = CenterScreen`. |

---

### 4. Quản lý Vòng đời Tiến trình & Bộ nhớ (Lifecycle & Leak Prevention)

| Nguy cơ (Risk) | Cơ chế phát sinh | Hậu quả nếu không chặn | Giải pháp phòng vệ chuẩn mực |
|---|---|---|---|
| **Chạy đè nhiều bản (Multiple Instances Collision)** | Người dùng click mở app nhiều lần hoặc click liên tục vào file exe | Nhiều bản cùng bắt Clipboard hook $\rightarrow$ Tranh chấp và xung đột phím tắt | Sử dụng `Named Mutex` (`HapiCopi_SingleInstance_Mutex`). Nếu instance đã tồn tại $\rightarrow$ Kích hoạt cửa sổ đang có lên Foreground rồi thoát instance mới ngay lập tức. |
| **Tiến trình ma WebView2 (Orphan msedgewebview2.exe)** | Ứng dụng cha bị End Task hoặc đóng mà không gọi `Dispose()` WebView2 | Hàng loạt tiến trình Edge WebView2 chạy ngầm ăn hàng trăm MB RAM | Đăng ký sự kiện `Application.ApplicationExit` và `AppDomain.CurrentDomain.ProcessExit` để đóng sạch `WebView2Environment` và giải phóng WebView Host. |
| **Tràn RAM do GDI Handles** | Hàm vẽ `DrawItem` tạo mới `Font`, `Brush`, `Pen` mà không Dispose | Quá 10,000 GDI Handles $\rightarrow$ Crash ứng dụng với lỗi `Out of Memory` | Chuyển toàn bộ Brush, Pen, Font thành tài nguyên `static readonly`. Gọi `MemoryOptimizer.OptimizeMemory()` dọn dẹp Working Set khi đóng cửa sổ con. |

---

### 5. Giao tiếp WebView2 & Web Canvas (Interop Boundary)

| Nguy cơ (Risk) | Cơ chế phát sinh | Hậu quả nếu không chặn | Giải pháp phòng vệ chuẩn mực |
|---|---|---|---|
| **Gửi lệnh khi WebView2 chưa khởi tạo xong** | Gọi `ExecuteScriptAsync()` trước khi sự kiện `CoreWebView2InitializationCompleted` nổ ra | `NullReferenceException` hoặc `InvalidOperationException` | Tạo cờ `_isWebViewReady` và xếp hàng đợi (Queue) các thông điệp. Chỉ xả hàng đợi khi CoreWebView2 sẵn sàng 100%. |
| **Chèn lặp công thức do Duplicate DOM Events** | Nút bấm HTML/JS gắn cả `mousedown` lẫn `click` | Công thức bị nhân đôi, lồng nhau như lỗi phân số vừa qua | Chỉ lắng nghe duy nhất `mousedown` kèm `e.preventDefault()`, khóa debounce $250\text{ ms}$ tại hàm `handleFormulaInsert()`. |

---

## III. 6 NGUY CƠ MÔI TRƯỜNG THỰC CHIẾN HỆ ĐIỀU HÀNH WINDOWS (OS ECOSYSTEM PITFALLS)

Ngoài các nguy cơ nội tại bên trong code, trong môi trường triển khai thực tế trên máy người dùng, ứng dụng bắt buộc phải phòng thủ trước 6 "cạm bẫy môi trường":

| Nguy cơ (Risk) | Cơ chế phát sinh | Hậu quả nếu không chặn | Giải pháp phòng vệ chuẩn mực |
|---|---|---|---|
| **1. Bị Antivirus / SmartScreen chặn nhầm (False Positive)** | App không có chứng chỉ số EV ($400/năm), lại dùng Hook phím tắt và đọc `WM_CLIPBOARDUPDATE` liên tục | Windows SmartScreen hiện cảnh báo xanh "Unknown Publisher", một số AV (Bkav, Avast) chặn app chạy | Điền đầy đủ metadata file `.exe` (Company, Product, Version, Icon, Description). Chuẩn hóa manifest UAC (`asInvoker`). Viết tài liệu hướng dẫn người dùng bấm *"More info → Run anyway"*. |
| **2. Xung đột gõ tiếng Việt Unikey / EVKey** | Unikey/EVKey ở một số ứng dụng dùng cơ chế copy ký tự có dấu vào clipboard rồi paste ra màn hình | Hàng loạt ký tự rác 1 ký tự (`a`, `ê`, `o`) tràn vào Lịch sử Clipboard, gây trễ nhịp gõ phím | Bỏ qua mọi chuỗi clipboard siêu ngắn ($< 2$ ký tự) hoặc phát sinh quá nhanh ($< 15\text{ ms}$) từ các tiến trình bộ gõ. |
| **3. Tên tài khoản Windows có dấu tiếng Việt / khoảng trắng** | Người dùng đặt tên máy dạng `C:\Users\Nguyễn Văn A\...` | Các hàm C++ của ONNX Runtime hoặc thư mục WebView2 nếu xử lý đường dẫn ANSI sẽ báo `File Not Found` (thành `Nguy?n Van A`) | Toàn bộ đường dẫn nạp model và profile bắt buộc chuẩn hóa Unicode UTF-16 qua .NET `Path.Combine()`, tuyệt đối không dùng ép kiểu byte/ANSI. |
| **4. Máy tính thiếu WebView2 Runtime** | Windows 10 đời cũ (trước 2021) hoặc máy công ty dùng Windows 10 Enterprise/LTSC bị tắt cập nhật Edge | Khi mở Math Editor, app bị crash do `DllNotFoundException` | Bộ cài Inno Setup (`HapiCopi.iss`) kiểm tra khóa Registry của WebView2. Nếu thiếu $\rightarrow$ Tự động tải hoặc hướng dẫn tải Evergreen Bootstrapper. |
| **5. Rào cản quyền hạn Windows (UIPI Isolation)** | Người dùng copy dữ liệu từ cửa sổ chạy quyền Admin (CMD Admin, Task Manager, Visual Studio Admin) | HapiCopi (chạy quyền Standard) bị Windows chặn thông điệp, không nhận được clipboard | Bẫy ngoại lệ phân quyền êm ái, ghi log cảnh báo, không làm gián đoạn luồng chính của app. |
| **6. Xung đột Phím tắt Toàn cục (Global Hotkey Conflict)** | Phím tắt của HapiCopi (`Ctrl+Alt+M`, `Ctrl+Shift+V`) đã bị Discord, OBS, Photoshop, Zalo chiếm trước | Hàm Win32 `RegisterHotKey` trả về `false`, bấm phím tắt không phản hồi | Khi đăng ký phím tắt thất bại, phát Toast nhắc nhở nhẹ và cho phép người dùng mở bằng chuột từ System Tray hoặc đổi phím tắt. |

---

## IV. NGUYÊN TẮC VÀNG TRONG VẬN HÀNH ỨNG DỤNG (GOLDEN RULES)

1. **Nguyên tắc "Fail-Safe Clipboard":** Clipboard là tài sản của người dùng. Nếu bất kỳ thao tác định dạng nào của HapiCopi bị lỗi hoặc timeout (> 200ms), phải giữ nguyên vẹn dữ liệu gốc của người dùng. Tuyệt đối không bao giờ làm rỗng clipboard (Clear clipboard) khi gặp exception.
2. **Nguyên tắc "Zero Ghost Process":** Bấm nút Thoát (Exit) ở khay hệ thống là toàn bộ tiến trình C# và tiến trình con WebView2 phải tắt sạch trong vòng $500\text{ ms}$, không để lại bất kỳ tiến trình chạy ngầm nào.
3. **Nguyên tắc "No Unhandled Crashes":** Đăng ký đầy đủ `Application.ThreadException` và `AppDomain.CurrentDomain.UnhandledException`. Ghi nhật ký lỗi vào file log offline, không được để app văng hộp thoại crash của Windows.

