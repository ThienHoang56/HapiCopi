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
