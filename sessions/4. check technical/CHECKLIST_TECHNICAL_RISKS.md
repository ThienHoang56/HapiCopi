# CHECKLIST RÀ SOÁT NGUY CƠ KỸ THUẬT & XUNG ĐỘT HỆ ĐIỀU HÀNH (TECHNICAL RISK CHECKLIST)

> Bản checklist rà soát chi tiết từng dòng code và cơ chế runtime của HapiCopi v1.2.5 trước khi xuất xưởng.

---

## 1. Phân hệ Lắng nghe & Can thiệp Clipboard (Clipboard Subsystem)

- [ ] **1.1. Xử lý tranh chấp khóa Clipboard (`OpenClipboard Failed`)**
  - [ ] Kiểm tra hàm đọc/ghi clipboard có bọc trong vòng lặp thử lại (Retry Loop) hay không.
  - [ ] Số lần thử lại tối thiểu 3–5 lần với độ trễ lũy tiến (Exponential Backoff: 10ms, 20ms, 40ms, 80ms).
  - [ ] Khi vượt quá số lần thử lại, ném ngoại lệ được kiểm soát (handled exception) và giữ nguyên clipboard, **tuyệt đối không làm crash app**.
- [ ] **1.2. Triệt tiêu vòng lặp tái kích hoạt (Infinite Re-entrancy Loop)**
  - [ ] Có cờ nguyên tử `_suppressNextClipboardEvent` (hoặc kiểm tra Sequence Number) khi HapiCopi tự ghi dữ liệu đã định dạng vào Clipboard.
  - [ ] Kiểm tra mã băm nội dung (Hash check): Nếu nội dung vừa nhận từ `WM_CLIPBOARDUPDATE` giống 100% với nội dung app vừa phát ra $\rightarrow$ Bỏ qua lập tức.
- [ ] **1.3. Bảo đảm an toàn luồng (STA Threading Enforcement)**
  - [ ] Toàn bộ các lời gọi `Clipboard.GetText()`, `Clipboard.SetDataObject()`, `Clipboard.ContainsText()` đều diễn ra trên luồng UI chính (STA).
  - [ ] Không có bất kỳ dòng lệnh nào gọi trực tiếp `Clipboard.*` từ bên trong `Task.Run()` mà thiếu `Invoke` / `BeginInvoke`.
- [ ] **1.4. Bảo vệ dữ liệu gốc người dùng (Fail-Safe Principle)**
  - [ ] Khối `try-catch` bao bọc toàn bộ pipeline định dạng (Markdown, LaTeX, SVG, DLP).
  - [ ] Nếu quá trình parse/render bị timeout hoặc throw exception $\rightarrow$ Giữ nguyên bản sao chép gốc của người dùng.

---

## 2. Xử lý Bất đồng bộ & Chống Race Condition (Async & Concurrency)

- [ ] **2.1. Chống đè lấn kết quả sao chép cũ (Out-of-Order Execution Guard)**
  - [ ] Đặt biến đếm thế hệ nguyên tử: `long _currentClipboardGen = 0;`.
  - [ ] Khi bắt đầu task async (ví dụ render SVG hoặc convert Word OLE HTML), lưu lại `long myGen = Interlocked.Increment(ref _currentClipboardGen);`.
  - [ ] Khi task async hoàn tất, kiểm tra `if (myGen == Interlocked.Read(ref _currentClipboardGen))` mới được ghi vào Clipboard hoặc nạp vào đầu lịch sử.
- [ ] **2.2. Khóa đồng thời khi ghi đĩa DPAPI (`history.dat` / `appsettings.json`)**
  - [ ] Phương thức `HistoryManager.SaveHistory()` có cơ chế khóa luồng `lock (_fileLock)` hoặc `SemaphoreSlim`.
  - [ ] Ghi file nguyên tử (Atomic File Write): Ghi ra file tạm `.tmp` rồi gọi `File.Move(..., overwrite: true)` để tránh tình trạng file bị rỗng (0 KB) khi máy bị mất điện hoặc crash đột ngột.
- [ ] **2.3. An toàn luồng danh sách dữ liệu (`List<ClipboardHistoryItem>`)**
  - [ ] Danh sách `_historyList` được bảo vệ khi vừa có sự kiện copy mới nạp vào đuôi, vừa có người dùng bấm "Xóa tất cả" hoặc tìm kiếm trên UI.
  - [ ] Sử dụng bản sao chép (Snapshot) hoặc khóa đồng bộ khi duyệt `foreach`.
- [ ] **2.4. Đồng bộ hóa AI Distillation & Suy luận ONNX**
  - [ ] Danh sách prototypes vector cá nhân hóa được bảo vệ bằng `lock` khi vừa tự sinh 7 biến thể hình học vừa có luồng UI gọi nhận diện nét vẽ.

---

## 3. Quản lý Màn hình, High-DPI & Đồ họa (DWM & Multi-Monitor)

- [ ] **3.1. Tuyệt đối loại bỏ `WS_EX_COMPOSITED` trên Windows 11**
  - [ ] Rà soát toàn bộ project, xác nhận không còn bất kỳ dòng code nào chứa `cp.ExStyle |= 0x02000000;`.
  - [ ] Xác nhận không còn hiện tượng chớp nháy trắng/xám (white strobe) khi di chuột hoặc lăn bánh xe.
- [ ] **3.2. Kiểm thử chuyển đổi màn hình động (PerMonitorV2 Scaling)**
  - [ ] Đặt cấu hình `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` trong `.csproj`.
  - [ ] Kéo cửa sổ từ màn hình Laptop 150% sang màn hình ngoài 100% (hoặc ngược lại) $\rightarrow$ Kiểm tra kích thước font, icon, và chiều cao nút không bị vỡ.
  - [ ] Sử dụng `ClientSize` thay vì `Size` trên mọi Form (`SettingsForm`, `UpdateForm`, `ClipboardHistoryForm`, `MathEditorForm`).
- [ ] **3.3. Tự động phục hồi vị trí cửa sổ (Off-screen Bounds Check)**
  - [ ] Khi mở form ở vị trí nhớ từ phiên trước, kiểm tra xem tọa độ đó có nằm trong `SystemInformation.VirtualScreen` hay không.
  - [ ] Nếu nằm ngoài (do đã rút màn hình phụ) $\rightarrow$ Tự động đưa về giữa màn hình chính (`CenterScreen`).

---

## 4. Giao tiếp WebView2 & Web Canvas (Math Editor & DOM)

- [ ] **4.1. Đảm bảo CoreWebView2 khởi tạo hoàn tất trước khi gọi script**
  - [ ] Có cờ `_isWebViewReady` hoặc `await EnsureCoreWebView2Async()`.
  - [ ] Mọi lệnh `ExecuteScriptAsync` hoặc gửi message từ C# xuống JS khi khởi động đều phải đợi sự kiện `NavigationCompleted`.
- [ ] **4.2. Triệt tiêu hoàn toàn sự kiện trùng lặp trên nút bấm DOM**
  - [ ] Toàn bộ các nút chèn mẫu công thức (`quick-btn`, `template-card`, `quickCustomList`) **chỉ dùng duy nhất sự kiện `mousedown` kèm `e.preventDefault()`**.
  - [ ] Tuyệt đối không gắn thêm sự kiện `click` gây chèn lặp công thức 2 lần.
  - [ ] Khóa an toàn Debounce tối thiểu $200 – 250\text{ ms}$ tại hàm `handleFormulaInsert()`.
- [ ] **4.3. Quản lý thư mục User Data Folder (UDF) của WebView2**
  - [ ] UDF được đặt tại `%LOCALAPPDATA%\HapiCopi\EBWebView`, không ghi vào thư mục Program Files (tránh lỗi phân quyền UAC).
  - [ ] Kiểm tra dọn dẹp cache WebView2 định kỳ, không để phình to ổ cứng sau nhiều tháng sử dụng.

---

## 5. Vòng đời Tiến trình & Giải phóng Bộ nhớ (Process & Memory)

- [ ] **5.1. Khóa Single-Instance bằng Named Mutex**
  - [ ] `Program.cs` sử dụng `Mutex` với tên định danh duy nhất (`Global\HapiCopi_SingleInstance_Mutex`).
  - [ ] Nếu instance thứ 2 được bật lên $\rightarrow$ Đánh thức cửa sổ instance thứ nhất lên Foreground và thoát instance thứ 2 ngay lập tức ($< 100\text{ ms}$).
- [ ] **5.2. Giải phóng triệt để cây tiến trình khi thoát (Zero Ghost Process)**
  - [ ] Đăng ký `Application.ApplicationExit` để đóng tất cả cửa sổ con, Dispose `InferenceSession` của ONNX và Dispose WebView2.
  - [ ] Khi bấm "Thoát" ở khay hệ thống, không còn bất kỳ tiến trình `HapiCopi.exe` hay `msedgewebview2.exe` nào sót lại trong Task Manager.
- [ ] **5.3. Ngăn ngừa rò rỉ GDI Handles & RAM Working Set**
  - [ ] Các hàm vẽ `DrawItem` sử dụng các đối tượng Brush/Pen/Font tĩnh (`static readonly`), không `new` đối tượng GDI trong vòng lặp vẽ.
  - [ ] Tổng số GDI Handles đo đạc qua Task Manager luôn duy trì ổn định $\le 150 – 250$ handles, không tăng tịnh tiến khi cuộn/lăn chuột.
  - [ ] Gọi `MemoryOptimizer.OptimizeMemory()` (thu gọn Working Set) khi đóng các form phụ trợ.

---

## 6. Phân hệ Cập nhật Tự động (Auto-Update Subsystem)

- [ ] **6.1. Kiểm tra mã băm SHA-256 trước khi thực thi file cài đặt**
  - [ ] File setup tải về được tính mã băm SHA-256 và so khớp chính xác với `versions.json`.
  - [ ] Nếu mã băm không khớp (do tải đứt đoạn hoặc file bị hỏng) $\rightarrow$ Xóa file ngay lập tức, không cho phép chạy.
- [ ] **6.2. Cơ chế tắt tiến trình cũ khi cập nhật**
  - [ ] Bộ cài Inno Setup (`HapiCopi.iss`) có chỉ thị `CloseApplications=yes` để thông báo đóng HapiCopi trước khi ghi đè file.
  - [ ] File `build.ps1` có lệnh tắt tiến trình `HapiCopi` trước khi đóng gói Release.

---

## 7. Đăng ký Xử lý Ngoại lệ Toàn cục (Global Exception Handlers)

- [ ] **7.1. Bắt ngoại lệ luồng UI:** `Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)` và gán `Application.ThreadException`.
- [ ] **7.2. Bắt ngoại lệ luồng nền:** Gán `AppDomain.CurrentDomain.UnhandledException`.
- [ ] **7.3. Ghi log ngoại lệ an toàn:** Mọi crash dump được ghi vào `%LOCALAPPDATA%\HapiCopi\logs\crash.log` thay vì văng crash dialog mặc định của Windows.

---

## 8. Phòng thủ Antivirus & Windows Defender (False Positive Prevention)

- [ ] **8.1. Chuẩn hóa định danh nhị phân & UAC Manifest**
  - [ ] Khai báo đầy đủ Metadata trong `.csproj`: `AssemblyTitle`, `Company`, `Product`, `Copyright`, `AssemblyVersion`, `FileVersion`.
  - [ ] File `app.manifest` chỉ định rõ `requestedExecutionLevel level="asInvoker" uiAccess="false"`, tuyệt đối không yêu cầu quyền Admin để tránh kích hoạt cờ heuristic nguy hiểm của Defender.
  - [ ] Nhúng file icon hợp lệ `hapicopi.ico` vào PE header để tránh bị bộ lọc của Windows gắn cờ tệp không rõ nguồn gốc.
- [ ] **8.2. Hành vi Keyboard Hook không mang tính chất Keylogger**
  - [ ] Trong `Core/KeyboardHook.cs`, hàm `HookCallback` xử lý siêu tốc ($< 0.5\text{ ms}$) và luôn chuyển tiếp `CallNextHookEx`, không ghi nhận nhật ký gõ phím của người dùng vào đĩa.
  - [ ] Toàn bộ hành vi lắng nghe chỉ nhằm bắt đúng tổ hợp phím tắt của HapiCopi (`Ctrl+Alt+M`, `Ctrl+Shift+V`), tuyệt đối không lưu buffer chuỗi ký tự.
  - [ ] Đảm bảo `UnhookWindowsHookEx` được gọi đầy đủ trong khối `Dispose()` để không để lại hook treo trong hệ điều hành.
- [ ] **8.3. Quy trình xác thực cho bản cài đặt không có chữ ký số EV**
  - [ ] Bộ cài Inno Setup tạo mã băm SHA-256 công khai trong release để người dùng và các công cụ bảo mật đối chiếu toàn vẹn.
  - [ ] Có tài liệu/thông báo hướng dẫn người dùng cách vượt qua cảnh báo Windows SmartScreen xanh (*"More info → Run anyway"*) một cách an toàn và minh bạch.

---

## 9. Tương thích Bộ gõ Tiếng Việt (Unikey / EVKey / OpenKey)

- [ ] **9.1. Lọc bỏ chuỗi rác từ cơ chế Clipboard Macro của Bộ gõ**
  - [ ] Khi gõ tiếng Việt có dấu trong một số ứng dụng (Game, Photoshop, Terminal), bộ gõ gửi Backspace và paste chuỗi ký tự thay thế qua Clipboard.
  - [ ] Kiểm tra trong `ProcessClipboard`: Tự động bỏ qua các chuỗi văn bản siêu ngắn ($\le 1$ ký tự) phát sinh trong khoảng thời gian siêu ngắn ($< 20\text{ ms}$) từ các tiến trình bộ gõ.
  - [ ] Ngăn chặn việc các chữ cái đơn lẻ (`a`, `â`, `o`, `ơ`) bị lưu tràn ngập vào danh sách Lịch sử Clipboard.
- [ ] **9.2. Không cản trở nhịp gõ phím và phím chuyển đổi chế độ**
  - [ ] Xác nhận `KeyboardHook` không nuốt (swallow) các phím điều khiển `Ctrl`, `Alt`, `Shift` khi người dùng đang chuyển chế độ gõ (ví dụ `Ctrl + Shift` chuyển E/V).
  - [ ] Không can thiệp vào các phím số (hàng phím số dùng gõ dấu VNI) hoặc các phím ký tự gõ dấu Telex (`s`, `f`, `r`, `x`, `j`).

---

## 10. Xử lý Đường dẫn Unicode Tiếng Việt & Khoảng trắng (`C:\Users\Nguyễn Văn A\...`)

- [ ] **10.1. An toàn Unicode khi nạp Model ONNX cục bộ**
  - [ ] Kiểm tra hàm khởi tạo `InferenceSession` trong `OnnxMathVisionEngine.cs`: Đường dẫn file `.onnx` phải truyền bằng chuỗi UTF-16 chuẩn .NET (`string modelPath`).
  - [ ] Tuyệt đối không qua trung gian chuyển đổi ANSI byte array hoặc `Marshal.StringToHGlobalAnsi` (tránh biến `Nguyễn` thành `Nguy?n`).
  - [ ] Thử nghiệm nạp model từ đường dẫn chứa ký tự tiếng Việt có dấu và khoảng trắng để kiểm chứng ONNX Runtime C++ nạp thành công 100%.
- [ ] **10.2. Thư mục User Data Folder (UDF) của WebView2**
  - [ ] Thư mục cache `%LOCALAPPDATA%\HapiCopi\WebViewCache` nằm dưới profile người dùng có dấu tiếng Việt phải đảm bảo `CoreWebView2Environment.CreateAsync` xử lý đúng chuỗi Unicode.
  - [ ] Kiểm tra xem WebView2 có khởi tạo thành công khi đường dẫn chứa khoảng trắng và ký tự có dấu.
- [ ] **10.3. Lưu trữ Lịch sử & Cấu hình DPAPI**
  - [ ] Đường dẫn file `history.dat`, `appsettings.json`, `handwriting_profile.json` sử dụng `Path.Combine` với `Environment.SpecialFolder.ApplicationData`.
  - [ ] Mọi thao tác đọc/ghi file sử dụng `FileStream` và `StreamWriter(..., Encoding.UTF8)` để đảm bảo mã hóa UTF-8 chuẩn mực, không lỗi font tiếng Việt.

---

## 11. Kiểm soát Môi trường Thiếu WebView2 Runtime

- [ ] **11.1. Phát hiện sớm máy tính chưa có WebView2 Runtime**
  - [ ] Kiểm tra khóa Registry của WebView2 Runtime trước khi cố gắng mở cửa sổ Web:
    - `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-9E47-BBBE-9E3E-7E48F834E014}`
    - `HKCU\Software\Microsoft\EdgeUpdate\Clients\{F3017226-9E47-BBBE-9E3E-7E48F834E014}`
  - [ ] Bắt ngoại lệ `WebView2RuntimeNotFoundException` hoặc `DllNotFoundException` trong khối `try-catch` khi gọi `EnsureCoreWebView2Async()`.
- [ ] **11.2. Hộp thoại Fallback thông minh & Tự động tải Bootstrapper**
  - [ ] Khi phát hiện máy chưa cài WebView2 Runtime (Windows 10 LTSC / Win Server):
    - Không để app bị crash văng ra Desktop.
    - Hiển thị hộp thoại thân thiện giải thích tính năng Math Editor cần WebView2 Runtime.
    - Cung cấp nút bấm "Tải và cài đặt tự động" (Evergreen Bootstrapper từ Microsoft) hoặc mở link tải chính thức.
- [ ] **11.3. Bộ cài Inno Setup kiểm tra tiên quyết (Prerequisite Check)**
  - [ ] Bổ sung hàm kiểm tra Registry của WebView2 trong mục `[Code]` của `installer/HapiCopi.iss`.
  - [ ] Nếu máy thiếu WebView2, hiển thị cảnh báo ngay khi cài đặt và gợi ý tải gói Runtime.

---

## 12. Phân quyền Windows & Cơ chế Cách ly UIPI (User Interface Privilege Isolation)

- [ ] **12.1. Nhận biết giới hạn khi tương tác với Cửa sổ Quyền Administrator**
  - [ ] Khi người dùng copy từ cửa sổ Admin (CMD Administrator, Regedit, Visual Studio as Admin), Windows UIPI chặn các thông điệp Win32 gửi từ tiến trình Standard User (HapiCopi chạy `asInvoker`).
  - [ ] Bọc khối kiểm tra `ActiveWindowHelper.GetActiveAppInfo()`: Nếu không lấy được handle hoặc cửa sổ có quyền cao hơn $\rightarrow$ Bắt ngoại lệ êm dịu, không crash.
- [ ] **12.2. Cơ chế Paste Giả lập (`ClipboardSimulator`) an toàn trước UIPI**
  - [ ] Khi HapiCopi gửi phím tắt `Ctrl + V` giả lập (`SendInput`), nếu cửa sổ đích là Admin mà HapiCopi là Standard, `SendInput` sẽ bị UIPI chặn âm thầm.
  - [ ] Bẫy lỗi và ghi log cảnh báo: `[UIPI] Không thể gửi phím tắt dán tới ứng dụng chạy quyền Administrator`.
  - [ ] Dữ liệu vẫn được giữ nguyên vẹn trên Clipboard hệ thống để người dùng có thể tự tay nhấn `Ctrl + V` trên cửa sổ Admin mà không bị mất.
- [ ] **12.3. Duy trì nguyên tắc đặc quyền tối thiểu (Least Privilege)**
  - [ ] Giữ nguyên `PrivilegesRequired=lowest` trong Inno Setup và `asInvoker` trong manifest, không đòi hỏi quyền Admin khi cài đặt và sử dụng, đảm bảo tính an toàn cao nhất cho máy người dùng.

---

## 13. Sức khỏe Keyboard Hook & Xử lý Timeout Hệ thống (Hook Health)

- [ ] **13.1. Tuân thủ ngưỡng thời gian phản hồi `LowLevelHooksTimeout`**
  - [ ] Windows có cơ chế `LowLevelHooksTimeout` (mặc định $200 – 300\text{ ms}$). Nếu callback của HapiCopi xử lý lâu hơn ngưỡng này, Windows sẽ âm thầm tháo gỡ (unhook) vĩnh viễn mà không hề báo lỗi.
  - [ ] Đo đạc thời gian thực thi trong `HookCallback`: Bắt buộc hoàn tất $< 0.5\text{ ms}$.
  - [ ] Tuyệt đối không thực hiện bất kỳ thao tác I/O, render, hoặc tính toán nặng nào trực tiếp trên luồng của Hook; lập tức chuyển giao qua `Task.Run()` hoặc `BeginInvoke()`.
- [ ] **13.2. Cơ chế Tự phục hồi Hook (Hook Health Check & Auto-Reinstall)**
  - [ ] Khi người dùng mở menu Tray hoặc định kỳ theo chu kỳ nhịp tim (Heartbeat Timer), kiểm tra xem `_hookId` còn hợp lệ hay không.
  - [ ] Nếu phát hiện Hook đã bị hệ điều hành tháo gỡ $\rightarrow$ Tự động gọi lại `InstallHook()` để đảm bảo phím tắt hoạt động liên tục, không bị "chết ngầm".
- [ ] **13.3. Xử lý xung đột với các ứng dụng chiếm phím tắt khác**
  - [ ] Cung cấp giao diện tùy chọn cấu hình phím tắt trong Cài đặt (`Ctrl + Alt + M` hoặc `Ctrl + Shift + V` hoặc các biến thể).
  - [ ] Nếu phím tắt bị phần mềm khác chiếm đoạt hoặc xung đột, người dùng vẫn có thể kích hoạt Math Editor và Lịch sử Clipboard qua Menu khay hệ thống (System Tray Icon).

