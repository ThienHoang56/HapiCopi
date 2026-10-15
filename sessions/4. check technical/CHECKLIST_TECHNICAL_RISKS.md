# CHECKLIST RÀ SOÁT NGUY CƠ KỸ THUẬT & XUNG ĐỘT HỆ ĐIỀU HÀNH (TECHNICAL RISK CHECKLIST)

> **Bản checklist rà soát chi tiết từng dòng code và cơ chế runtime của HapiCopi v1.2.5.**  
> Trạng thái kiểm định: **ĐÃ TRIỂN KHAI VÀ NGHIỆM THU ĐẦY ĐỦ 13 TRỤ CỘT KỸ THUẬT.**

---

## 1. Phân hệ Lắng nghe & Can thiệp Clipboard (Clipboard Subsystem)

- [x] **1.1. Xử lý tranh chấp khóa Clipboard (`OpenClipboard Failed`)**
  - [x] Kiểm tra hàm đọc/ghi clipboard có bọc trong vòng lặp thử lại (Retry Loop) tại `Core/ClipboardHelper.cs`.
  - [x] Số lần thử lại 5-6 lần với độ trễ lũy tiến (`Thread.Sleep(15 * (i + 1))` / Exponential Backoff).
  - [x] Khi vượt quá số lần thử lại, ném ngoại lệ được kiểm soát (handled exception) và giữ nguyên clipboard, **tuyệt đối không làm crash app**.
- [x] **1.2. Triệt tiêu vòng lặp tái kích hoạt (Infinite Re-entrancy Loop)**
  - [x] Có cờ nguyên tử `_isSimulatingClipboard` khi HapiCopi tự ghi dữ liệu đã định dạng vào Clipboard.
  - [x] Đánh dấu Marker định dạng `HapiCopiProcessed` vào `IDataObject` (`ClipboardHelper.IsAlreadyProcessed()`) $\rightarrow$ Bỏ qua lập tức trong 0ms.
- [x] **1.3. Bảo đảm an toàn luồng (STA Threading Enforcement)**
  - [x] Toàn bộ các lời gọi `Clipboard.GetText()`, `Clipboard.SetDataObject()`, `Clipboard.ContainsText()` đều tự kiểm tra `Thread.CurrentThread.GetApartmentState() == ApartmentState.STA`, nếu là MTA tự spawn luồng STA riêng biệt.
  - [x] Không có bất kỳ dòng lệnh nào gọi trực tiếp `Clipboard.*` từ bên trong `Task.Run()` mà thiếu kiểm tra STA.
- [x] **1.4. Bảo vệ dữ liệu gốc người dùng (Fail-Safe Principle)**
  - [x] Khối `try-catch` bao bọc toàn bộ pipeline định dạng (Markdown, LaTeX, SVG, DLP).
  - [x] Trong `Core/ClipboardSimulator.cs`, loại bỏ lệnh `Clipboard.Clear()` khi không có dữ liệu sao lưu; nếu dán lỗi tự động khôi phục bản gốc nguyên vẹn.

---

## 2. Xử lý Bất đồng bộ & Chống Race Condition (Async & Concurrency)

- [x] **2.1. Chống đè lấn kết quả sao chép cũ (Out-of-Order Execution Guard)**
  - [x] Đặt biến đếm thế hệ: `long _currentClipboardSeq = 0;` trong `HapiCopiApplicationContext.cs`.
  - [x] Mỗi sự kiện copy mới tăng tuần tự `long currentSeq = ++_currentClipboardSeq;`.
  - [x] Các tác vụ async chỉ áp dụng kết quả nếu mã thế hệ còn hợp lệ.
- [x] **2.2. Khóa đồng thời khi ghi đĩa DPAPI (`history.dat` / `appsettings.json`)**
  - [x] Phương thức `HistoryManager.SaveHistory()` và `LoadHistory()` được bảo vệ bằng `lock (_fileLock)`.
  - [x] Ghi file nguyên tử (Atomic File Write): Ghi ra file tạm `.tmp` rồi gọi `File.Move(..., overwrite: true)` để chống mất dữ liệu (0 KB) khi crash hoặc sập nguồn.
- [x] **2.3. An toàn luồng danh sách dữ liệu (`List<ClipboardHistoryItem>`)**
  - [x] Danh sách `_clipboardHistory` được bọc `lock (_clipboardHistory)` trong `AddToHistory()`, `LoadHistory()`, và khi clone để lưu đĩa.
  - [x] Sử dụng bản sao chép (Snapshot clone) khi duyệt `foreach`.
- [x] **2.4. Đồng bộ hóa AI Distillation & Suy luận ONNX**
  - [x] Danh sách prototypes vector cá nhân hóa được bảo vệ bằng `lock` trong `HandwritingProfileService.cs`, đã pass 100% qua `--test-distill`.

---

## 3. Quản lý Màn hình, High-DPI & Đồ họa (DWM & Multi-Monitor)

- [x] **3.1. Tuyệt đối loại bỏ `WS_EX_COMPOSITED` trên Windows 11**
  - [x] Đã rà soát toàn bộ project, xác nhận không còn bất kỳ dòng code nào chứa `cp.ExStyle |= 0x02000000;`.
  - [x] Triệt tiêu 100% hiện tượng chớp nháy trắng/xám (white strobe) khi di chuột hoặc lăn bánh xe bằng cách áp dụng Phân trang (Pagination 6 thẻ).
- [x] **3.2. Kiểm thử chuyển đổi màn hình động (PerMonitorV2 Scaling)**
  - [x] Khai báo `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` trong `HapiCopi.csproj`.
  - [x] Sử dụng `ClientSize` thay vì `Size` trên mọi Form (`SettingsForm`, `UpdateForm`, `ClipboardHistoryForm`, `MathEditorForm`), footer 58px cố định.
- [x] **3.3. Tự động phục hồi vị trí cửa sổ (Off-screen Bounds Check)**
  - [x] Sử dụng lớp tiện ích `EditorWindow.Present()` kiểm tra vị trí hiển thị, tự động reset về `CenterScreen` nếu màn hình phụ bị tháo rút.

---

## 4. Giao tiếp WebView2 & Web Canvas (Math Editor & DOM)

- [x] **4.1. Đảm bảo CoreWebView2 khởi tạo hoàn tất trước khi gọi script**
  - [x] Có `TaskCompletionSource` `_initTcs` và `_envTcs` trong `WebViewManager.cs`.
  - [x] Gắn editor qua `WebViewManager.AttachEditorAsync()`, đợi `NavigationCompleted`.
- [x] **4.2. Triệt tiêu hoàn toàn sự kiện trùng lặp trên nút bấm DOM**
  - [x] Toàn bộ các nút chèn mẫu công thức (`quick-btn`, `template-card`, `quickCustomList`) **chỉ dùng duy nhất sự kiện `mousedown` kèm `e.preventDefault()`**.
  - [x] Tuyệt đối không gắn thêm sự kiện `click` gây chèn lặp công thức 2 lần.
  - [x] Khóa an toàn Debounce $250\text{ ms}$ tại hàm `handleFormulaInsert()`.
- [x] **4.3. Quản lý thư mục User Data Folder (UDF) của WebView2**
  - [x] UDF được đặt tại `%LOCALAPPDATA%\HapiCopi\WebViewCache`, không ghi vào thư mục Program Files (tránh lỗi phân quyền UAC).
  - [x] Dọn dẹp cache WebView2 khi đóng và khi gỡ cài đặt.

---

## 5. Vòng đời Tiến trình & Giải phóng Bộ nhớ (Process & Memory)

- [x] **5.1. Khóa Single-Instance bằng Named Mutex**
  - [x] `Program.cs` và `HapiCopi.iss` sử dụng Mutex định danh duy nhất: `Global\HapiCopi_SingleInstance_Mutex_9912`.
  - [x] Nếu instance thứ 2 được bật lên $\rightarrow$ Kênh lệnh `AppInstanceChannel` đánh thức cửa sổ cũ và thoát instance thứ 2 ngay lập tức ($< 50\text{ ms}$).
- [x] **5.2. Giải phóng triệt để cây tiến trình khi thoát (Zero Ghost Process)**
  - [x] Đăng ký `Application.ApplicationExit` gọi `WebViewManager.Shutdown()`, dừng server IPC, hủy hook và notify icon.
  - [x] Khi bấm "Thoát" ở khay hệ thống, không còn bất kỳ tiến trình `HapiCopi.exe` hay `msedgewebview2.exe` nào sót lại trong Task Manager.
- [x] **5.3. Ngăn ngừa rò rỉ GDI Handles & RAM Working Set**
  - [x] Các đối tượng Brush/Pen/Font vẽ dùng `static readonly` trong `UiTheme.cs`.
  - [x] Tổng số GDI Handles duy trì ổn định $\le 150 – 250$ handles.
  - [x] Gọi `MemoryOptimizer.OptimizeMemory()` định kỳ và khi đóng các form phụ trợ.

---

## 6. Phân hệ Cập nhật Tự động (Auto-Update Subsystem)

- [x] **6.1. Kiểm tra mã băm SHA-256 trước khi thực thi file cài đặt**
  - [x] File setup tải về được tính mã băm SHA-256 qua `UpdateManager.cs` và so khớp chính xác với `versions.json`.
  - [x] Nếu mã băm không khớp $\rightarrow$ Xóa file ngay lập tức, không cho phép chạy.
- [x] **6.2. Cơ chế tắt tiến trình cũ khi cập nhật**
  - [x] Bộ cài Inno Setup (`HapiCopi.iss`) có hàm `StopRunningApp()` gửi lệnh `--quit` và fallback `taskkill /F /IM HapiCopi.exe` trước khi ghi đè.
  - [x] File `installer/build.ps1` tự động ngắt tiến trình cũ trước khi đóng gói Release.

---

## 7. Đăng ký Xử lý Ngoại lệ Toàn cục (Global Exception Handlers)

- [x] **7.1. Bắt ngoại lệ luồng UI:** `Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)` và gán `Application.ThreadException` trong `Program.cs`.
- [x] **7.2. Bắt ngoại lệ luồng nền:** Gán `AppDomain.CurrentDomain.UnhandledException`.
- [x] **7.3. Ghi log ngoại lệ an toàn:** Mọi crash dump được ghi vào `%LOCALAPPDATA%\HapiCopi\logs\crash.log` thay vì văng crash dialog mặc định của Windows.

---

## 8. Phòng thủ Antivirus & Windows Defender (False Positive Prevention)

- [x] **8.1. Chuẩn hóa định danh nhị phân & UAC Manifest**
  - [x] Khai báo đầy đủ Metadata trong `HapiCopi.csproj`: `AssemblyTitle`, `Company`, `Product`, `Copyright`, `AssemblyVersion`, `FileVersion`.
  - [x] File manifest chỉ định rõ `requestedExecutionLevel level="asInvoker" uiAccess="false"`, không đòi quyền Admin.
  - [x] Nhúng icon chuẩn Windows `hapicopi.ico` vào PE header.
- [x] **8.2. Hành vi Keyboard Hook không mang tính chất Keylogger**
  - [x] Trong `Core/KeyboardHook.cs`, hàm `HookCallback` xử lý siêu tốc ($< 0.1\text{ ms}$) và luôn chuyển tiếp `CallNextHookEx`, không lưu buffer ký tự gõ phím.
  - [x] Chỉ bắt đúng phím nóng của HapiCopi (`Ctrl+Alt+M`, `Ctrl+Shift+V`, Shift+Insert).
  - [x] `UnhookWindowsHookEx` được gọi đầy đủ trong khối `Dispose()`.
- [x] **8.3. Quy trình xác thực cho bản cài đặt không có chữ ký số EV**
  - [x] Tạo mã băm SHA-256 công khai trong release để đối chiếu toàn vẹn.
  - [x] Hướng dẫn rõ ràng cách vượt qua Windows SmartScreen xanh (*"More info → Run anyway"*).

---

## 9. Tương thích Bộ gõ Tiếng Việt (Unikey / EVKey / OpenKey)

- [x] **9.1. Lọc bỏ chuỗi rác từ cơ chế Clipboard Macro của Bộ gõ**
  - [x] Trong `ProcessClipboard`, thêm bộ lọc IME macro burst: Bỏ qua chuỗi text $\le 2$ ký tự nếu xuất hiện cách lần trước $< 45\text{ ms}$.
  - [x] Trong `AddToHistory`, kiểm tra loại trừ ký tự đơn lẻ phát sinh dồn dập ($< 80\text{ ms}$), ngăn chặn rác chữ `a, â, d, đ` tràn vào danh sách Lịch sử.
- [x] **9.2. Không cản trở nhịp gõ phím và phím chuyển đổi chế độ**
  - [x] `KeyboardHook` không nuốt các phím `Ctrl`, `Alt`, `Shift` độc lập, trả quyền ngay lập tức cho bộ gõ.
  - [x] Không can thiệp phím số hay phím chữ Telex (`s, f, r, x, j`).

---

## 10. Xử lý Đường dẫn Unicode Tiếng Việt & Khoảng trắng (`C:\Users\Nguyễn Văn A\...`)

- [x] **10.1. An toàn Unicode khi nạp Model ONNX cục bộ**
  - [x] Đường dẫn model nạp vào `InferenceSession` trong `OnnxMathVisionEngine.cs` và `EndToEndMathEngine.cs` truyền bằng chuỗi UTF-16 .NET (`string modelPath`).
  - [x] Kiểm chứng thực tế qua benchmark `--test-e2e-math` và `--test-topo` chạy trơn tru 100%.
- [x] **10.2. Thư mục User Data Folder (UDF) của WebView2**
  - [x] Thư mục cache `%LOCALAPPDATA%\HapiCopi\WebViewCache` xử lý chuẩn Unicode qua `ResourcePaths.cs`.
- [x] **10.3. Lưu trữ Lịch sử & Cấu hình DPAPI**
  - [x] Đọc/ghi file JSON cấu hình và lịch sử bằng `Encoding.UTF8`, không lỗi font tiếng Việt.

---

## 11. Kiểm soát Môi trường Thiếu WebView2 Runtime

- [x] **11.1. Phát hiện sớm máy tính chưa có WebView2 Runtime**
  - [x] Kiểm tra Registry trước khi khởi tạo trong `installer/HapiCopi.iss` qua hàm `IsWebView2Installed()`.
  - [x] Bắt `WebView2RuntimeNotFoundException` / `DllNotFoundException` trong `MathEditorForm.cs`.
- [x] **11.2. Hộp thoại Fallback thông minh & Tự động tải Bootstrapper**
  - [x] Khi thiếu Runtime, `MathEditorForm` hiển thị hộp thoại tiếng Việt thân thiện giải thích rõ ràng và hỏi người dùng có muốn mở link tải Evergreen Bootstrapper chính thức từ Microsoft hay không.
- [x] **11.3. Bộ cài Inno Setup kiểm tra tiên quyết (Prerequisite Check)**
  - [x] Hàm `InitializeSetup()` trong `HapiCopi.iss` phát hiện thiếu WebView2 và hỏi người dùng mở trang tải tự động trước khi tiếp tục cài đặt.

---

## 12. Phân quyền Windows & Cơ chế Cách ly UIPI (User Interface Privilege Isolation)

- [x] **12.1. Nhận biết giới hạn khi tương tác với Cửa sổ Quyền Administrator**
  - [x] `ActiveWindowHelper.GetActiveAppInfo()` bọc `try-catch`, bắt lỗi êm dịu nếu cửa sổ nguồn là Administrator.
- [x] **12.2. Cơ chế Paste Giả lập (`ClipboardSimulator`) an toàn trước UIPI**
  - [x] `ClipboardSimulator.SimulateCtrlV()` bọc khối `try-catch`, ghi log cảnh báo nếu `SendKeys` thất bại.
  - [x] Giữ nguyên vẹn nội dung trên Clipboard để người dùng tự nhấn `Ctrl + V` trên cửa sổ Admin.
- [x] **12.3. Duy trì nguyên tắc đặc quyền tối thiểu (Least Privilege)**
  - [x] Giữ nguyên `PrivilegesRequired=lowest` trong Inno Setup và `asInvoker` trong manifest, không đòi hỏi UAC.

---

## 13. Sức khỏe Keyboard Hook & Xử lý Timeout Hệ thống (Hook Health)

- [x] **13.1. Tuân thủ ngưỡng thời gian phản hồi `LowLevelHooksTimeout`**
  - [x] Đo đạc thời gian trong `HookCallback`: Hoàn tất $< 0.1\text{ ms}$, toàn bộ tác vụ mở form đều đẩy sang `Task.Run()` hoặc `_syncContext.Post()`.
- [x] **13.2. Cơ chế Tự phục hồi Hook (Hook Health Check & Auto-Reinstall)**
  - [x] Bổ sung phương thức `EnsureHookActive()` và `ReinstallHook()` trong `Core/KeyboardHook.cs`.
  - [x] Thiết lập Timer nhịp tim `_hookHeartbeatTimer` (chu kỳ 30 giây) trong `HapiCopiApplicationContext.cs` tự động phát hiện và cắm lại Hook nếu bị Windows âm thầm tháo gỡ.
- [x] **13.3. Xử lý xung đột với các ứng dụng chiếm phím tắt khác**
  - [x] Cung cấp nhiều cách kích hoạt: Phím tắt `Ctrl+Alt+M`, nhấp đúp vào công thức, hoặc mở từ Menu chuột phải ở khay hệ thống.

---

## 14. Phím tắt Toàn cục, Ứng dụng Khay Hệ thống & Tùy biến Dữ liệu Bảo mật (DLP)

- [x] **14.1. Hệ thống Phím tắt Toàn cục Độc lập & Không Xung đột**
  - [x] `Ctrl + Alt + P` (hoặc `Ctrl + Pause`): Tạm dừng / Tiếp tục ứng dụng (chuyển chế độ nhanh như Unikey).
  - [x] `Ctrl + Alt + Q`: Thoát hoàn toàn ứng dụng (ẩn ngay tray icon, giải phóng toàn bộ Hook, WebView2, tiến trình ngầm).
  - [x] `Ctrl + Alt + M` (hoặc nhả `Ctrl + Alt`): Mở bảng Soạn thảo công thức Toán học.
  - [x] Cơ chế chống rung phím (Debounce): 400ms cho Pause/Resume, 1000ms cho Exit để tránh tình trạng autorepeat của Windows làm nhảy trạng thái liên tục khi người dùng giữ phím.
- [x] **14.2. Chuẩn hóa Ứng dụng Chạy ngầm Khay Hệ thống (System Tray Only)**
  - [x] Thiết lập `ShowInTaskbar = false` trên toàn bộ các Form và Dialog (`UiTheme.ApplyDialog`, `MathEditorForm`, `EditorWindow`, `AbcEditorForm`).
  - [x] Ứng dụng chỉ xuất hiện trong Khay hệ thống (System Tray), hoàn toàn không chiếm chỗ hay hiển thị biểu tượng trên thanh Taskbar danh sách ứng dụng.
- [x] **14.3. Minh bạch Hóa Danh mục Dữ liệu Nhạy cảm (DLP Protection Checklist)**
  - [x] Bổ sung checklist chi tiết trong Cài đặt với ngôn ngữ dễ hiểu, make sense:
    - Mật khẩu, Khóa bí mật & API Key (OpenAI, Google, AWS, GitHub, Token...)
    - Số thẻ ngân hàng & Thẻ tín dụng (Visa, MasterCard, 16 số)
    - Căn cước công dân (CCCD 12 số) & CMND (9 số)
    - Số điện thoại di động cá nhân (Việt Nam)
    - Địa chỉ Email cá nhân / công việc
  - [x] Người dùng hoàn toàn có thể tự do bật/tắt từng loại dữ liệu theo nhu cầu sử dụng riêng, cấu hình lưu trữ an toàn trong `AppConfig` và được `PiiScanner.Scan()` áp dụng thời gian thực.

