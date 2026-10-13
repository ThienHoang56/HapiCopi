# Bàn giao: vòng đời app + dọn UI

Cập nhật lúc token hết: **2026-09-24 ~23:45 UTC+7**.
Agent tiếp theo: đọc file này **trước** khi đụng `UI/`, `Program.cs`, `installer/`, `Core/AppInstanceChannel.cs`.

Cuộc chat gốc: transcript
`C:\Users\ThienNLH3\.cursor\projects\c-Users-ThienNLH3-Desktop-i-h-c-FPT-K-8-EXE-HapiCopi\agent-transcripts\cc406d53-5b58-4aaf-89e2-b117fb2cbed7\cc406d53-5b58-4aaf-89e2-b117fb2cbed7.jsonl`

---

## 0. Tinh thần của user (đừng làm ẩu)

User nói rõ: **không muốn app bị nhìn như món đồ chơi rẻ tiền**. Giao diện chưa cần xuất sắc, nhưng phải **rõ ràng và sạch**. Phần vòng đời (cài / chạy / gỡ) và UI là **core**, phải làm nghiêm túc.

Hệ quả cho agent:

- Không vá từng toạ độ pixel rồi tuyên bố xong.
- Không chụp toàn màn hình để “verify” (đã lọt nội dung riêng tư của user một lần — xin lỗi rồi, **cấm lặp**).
- Không commit giữa chừng UI trừ khi user bảo. User đã chọn: **xong UI rồi mới commit một lần** (gộp với vòng đời).
- Không tự push trừ khi user bảo.

---

## 1. Quyết định user đã chốt

| Câu hỏi | Quyết định |
|---------|------------|
| Thứ tự làm | Vòng đời / gỡ app trước, UI sau |
| Khi gỡ: dữ liệu cá nhân (`%AppData%\HapiCopi`) | **Hỏi**, mặc định xoá. Gỡ `/SILENT` thì **giữ** |
| UI audit | Agent audit trước, user bổ sung sau |
| Mức sửa UI | **Sửa tận gốc** — layout tự giãn, không toạ độ cứng |
| Theme | **Tất cả tông sáng** (lịch sử đang tối → đổi sáng) |
| Commit lúc này | **Đợi xong UI** |

Các quyết định cũ hơn (đã ship, đã commit):

- SVG copy từ mọi nơi → PNG offline, nền trong suốt mặc định.
- Cập nhật: nguồn local trước (folder / LAN / https), kênh **positional** (bản mới nhất = Thử nghiệm, bản liền trước = Khuyến nghị).
- `idea/` **gitignore**, không commit.

---

## 2. Git / máy lúc dừng

- Remote: `https://github.com/ThienHoang56/HapiCopi.git` (private). Branch `main`.
- HEAD đã push (phiên trước): `097dbfd` docs git + ignore `idea/`.
  - `fd77e17` SVG → PNG
  - `3789405` in-app update/rollback
- **Toàn bộ vòng đời + UI theme + SettingsForm rewrite CHƯA commit.**
- `HapiCopi.csproj` đã bump `1.2.0` (chưa commit).
- Máy user lúc dừng: thường có bản cài `%LocalAppData%\Programs\HapiCopi` (1.2.0) và feed `%LocalAppData%\HapiCopi\Releases` (1.1.0 + 1.2.0). App tray có thể đang chạy — tắt bằng `HapiCopi.exe --quit` hoặc `Get-Process HapiCopi | Stop-Process` trước khi build/cài.

File mới / sửa chưa commit (ước lượng, `git status` để xác nhận):

```
Core/AppInstanceChannel.cs          MỚI
Core/StartupManager.cs              MỚI
Core/ResourcePaths.cs               LogDirectory, RoamingDataRoot
Core/WebViewManager.cs              Shutdown()
Program.cs                          Main(int), --quit/--settings/--update, log path
Formatting/MarkdownToWordConverter.cs  diagnostic ra Logs
installer/HapiCopi.iss              StopRunningApp + hỏi xoá AppData
UI/HapiCopiApplicationContext.cs    kênh lệnh, shutdown chung, ctor(string)
UI/UiTheme.cs                       MỚI
UI/SettingsForm.cs                  viết lại, CHƯA verify ảnh
HapiCopi.csproj                     Version 1.2.0
tools/capture-window.ps1            MỚI
.AGENT/handoff-ui-va-vong-doi.md
.AGENT/packaging.md                 đã cập nhật vòng đời
.AGENT/README.md                    đã trỏ handoff
```

Chưa đụng (vẫn toạ độ cứng / theme cũ):

```
UI/UpdateForm.cs
UI/ClipboardHistoryForm.cs          (giữ EnableSvgDrop)
UI/PiiToastForm.cs
UI/MathEditorForm.cs
UI/AbcEditorForm.cs
```

---

## 3. ĐÃ XONG — vòng đời (đã thử thật, đừng làm lại)

Chi tiết kỹ thuật nằm ở các file; dưới đây là **vì sao** và **bẫy**.

### 3.1 Vì sao không tắt / không gỡ sạch được

App tray **không có cửa sổ** → Restart Manager / `CloseApplications=yes` vô dụng.
Instance 2 trước đây thấy mutex rồi **thoát im lặng** → shortcut lúc app đang chạy “không làm gì”; uninstaller không bảo app tự thoát.

### 3.2 Kênh lệnh — `Core/AppInstanceChannel.cs`

Named pipe `HapiCopi.Command.<UserName>`. Mutex: `Global\HapiCopi_SingleInstance_Mutex_9912`.

Lệnh: `show` | `settings` | `update` | `quit` | `ping`.

`Program.ParseCommand`: `--quit`/`--exit`, `--update`, `--settings`, mặc định `show`.

**Bẫy mutex:** instance thứ hai phải `_mutex.Dispose()` **ngay** sau khi biết mình không phải instance mới. Nếu giữ handle, `WaitForExit` soi thấy chính mình → `--quit` chờ 20s rồi exit code 1 dù app đã tắt sau 0,1s. Đã sửa, đã đo: `--quit` ~1,1s, mã 0.

`IsRunning()` dùng `Mutex.TryOpenExisting` rồi **Dispose** handle kiểm tra.

Inno **không tin mã thoát** của `--quit`: bản cũ không hiểu `--quit` cũng return 0. Phải `tasklist` / `AppIsRunning()`.

### 3.3 Shutdown chung

`Application.ApplicationExit` → `OnApplicationExit`:
kênh lệnh → ẩn+dispose tray → listener → hook → `WebViewManager.Shutdown()`.

`UpdateForm` vẫn gọi `Application.Exit()` — **đừng** thêm dispose rời ở đó; đường chung đã lo icon ma.

WebView2 **phải** Shutdown: `msedgewebview2.exe` khoá `%LocalAppData%\HapiCopi\WebViewCache`.

### 3.4 Log không được nằm cạnh exe

`Program.Log` → `%LocalAppData%\HapiCopi\Logs\hapicopi.log` (cắt nửa khi > 2MB).
Diagnostic HTML cùng thư mục Logs.

`ResourcePaths.LogDirectory`, `RoamingDataRoot` (`%AppData%\HapiCopi`).

### 3.5 Inno `installer/HapiCopi.iss`

- `CloseApplications=no`
- `[Code]`: `StopRunningApp` ở `PrepareToInstall` + `InitializeUninstall`
- `--quit` → chờ tối đa ~10s bằng `tasklist` → `taskkill /F`
- `[UninstallDelete]` `{localappdata}\HapiCopi` + `{app}`
- `usPostUninstall`: hỏi xoá `{userappdata}\HapiCopi`; silent thì giữ

**Đừng đổi `AppId={{A8F3C2E1-...}}`.** Inno escape ngoặc nhọn; khóa uninstall thật có thể kết thúc `}}_is1`. `UpdateManager.InstalledLocation` dò GUID, không hardcode tên khóa.

### 3.6 `Core/StartupManager.cs`

HKCU `Run` value `HapiCopi`. Settings đã có checkbox “Chạy HapiCopi cùng Windows”.

### 3.7 Ca đã verify

| Ca | Kết quả |
|----|---------|
| Cài 1.2.0 đè 1.1.0 đang chạy | Tự tắt, cài xong, không Task Manager |
| `--quit` (sau fix mutex) | 0 sau ~1,1s |
| Shortcut lúc đang chạy | Gửi `show`, mở lịch sử |
| Gỡ silent lúc đang chạy | Process 0; `Programs\HapiCopi` và `%LocalAppData%\HapiCopi` **không còn** |
| `%AppData%\HapiCopi` sau gỡ silent | **Còn** (đúng vì silent không hỏi) |

`testing/Bug.txt` mô tả hai lỗi cũ — coi như đã xử lý ở vòng đời; agent có thể đánh dấu đã sửa khi commit.

---

## 4. UI — đã viết lại 3 cửa sổ chính (2026-09-24 đêm)

Chụp ở 125% bằng `tools/capture-window.ps1` (không fullscreen):

- Settings ~718px rộng: chữ mô tả xuống dòng đủ; có “Chạy HapiCopi cùng Windows”.
- Update: chọn bản đang dùng; nút “Đang dùng bản này”. Sửa so sánh version 1.2.0 vs 1.2.0.0 trong `UpdateManager.Normalize`.
- History: tông sáng, empty state, hai nút copy đủ chữ, resize được, giữ SVG drop.

Chưa: chụp lại Settings sau khi thu ô từ khóa; thử DPI 100%/150%; build bộ cài Inno lần cuối; **chưa commit** (user muốn một commit khi xong).

### 4.1 Nguyên nhân gốc (một nguyên nhân, mọi cửa sổ)

Toạ độ `Location`/`Width`/`Height` cứng + `PerMonitorV2` + **không** `AutoScaleMode`.
Máy user: 1920×1200, phóng **125%**. Chữ +25%, khung giữ nguyên → cắt chữ.
Ở 100% nhìn “ổn” nên lỗi sống sót.

**Cách làm đúng:** `UiTheme.ApplyDialog` (`AutoScaleMode.Font`, `AutoScaleDimensions(7,17)`), `TableLayoutPanel`/`FlowLayoutPanel`, nút `AutoSize`, nhãn wrap có `MaximumSize`.

**Cách làm sai:** nới `Width` từng nút. Hỏng lại ở 150%.

### 4.2 `UI/UiTheme.cs` — đã viết, dùng cho form mới

- Tông sáng: Background trắng, Accent xanh `#0067C0`, Success/Warning/Danger.
- `PrimaryButton` / `SecondaryButton`: AutoSize, `UseMnemonic = false`.
- `Description`/`Hint`: `Tag = UiTheme.WrapTag`.
- **Bẫy wrap:** Label AutoSize đo **một dòng**. Không `ApplyWrapWidth` thì form phình (đã gặp **1282×1029**). Gọi lúc dựng **và** `ClientSizeChanged`.
- `FitToContent`: không vượt WorkingArea.
- `AppIcon`: icon từ exe, không vẽ 16×16.

### 4.3 `UI/SettingsForm.cs` — viết lại, **CHƯA CHỤP LẠI**

Đã: layout cột, cuộn, mục SVG, lịch sử, **Khởi động cùng Windows**, footer Lưu/Đóng.

Lần chụp **trước** `ApplyWrapWidth`: 1282px — quá rộng.
Đã thêm wrap + `DesignWidthDip = 560`. Lệnh build+chụp **bị abort** vì hết token.

**Việc đầu tiên:** publish (`build.ps1 -SkipInno`), `--settings`, chụp bằng `tools/capture-window.ps1`. Kỳ vọng ~700px ở 125%, không cắt câu mô tả DLP/SVG, checkbox “Chạy HapiCopi cùng Windows” hiện đủ.

Nếu vẫn rộng: `ApplyWrapWidth` chưa chạy trước `FitToContent`, hoặc `content` không nhận width (Dock/AutoSize). Sửa theme, đừng hardcode Width form.

Footer: `Controls.Add` Fill trước, Bottom sau — nếu nút bị che, đảo thứ tự Add (WinForms dock theo z-order ngược).

### 4.4 `UI/UpdateForm.cs` — CHƯA VIẾT LẠI

Lỗi đã thấy (ảnh cũ `testing/shots/update.png` hoặc agent-store shots):

1. Label mặc định mnemonic → tiêu đề mất `&` (“Phiên bản  Cập nhật”).
2. TextBox đè nút “Lưu nguồn”.
3. Cảnh báo đỏ cụt (“…Cài đặt sẽ tạo bản”).
4. Dòng mô tả nguồn bị xén đáy (`Height=16`).
5. Nút “Quay về bản khuyến nghị” mất chữ (`Width=180`).
6. Khoảng trống chết ~100px giữa list và status.
7. **Hành vi:** `SelectSensibleRow()` khi không có bản mới hơn chọn *Khuyến nghị* (= bản cũ) → nút chính “Quay về vX” tô xanh. **Sai.** Không có bản mới thì chọn **bản đang dùng**. Chỉ auto-select bản mới hơn khi `ParsedVersion > CurrentVersion`.

Giữ: confirm mặc định **Hủy** (`MessageBoxDefaultButton.Button2`); `Shown` focus `_releaseList` không phải nút cài.

Cảnh báo “chạy từ thư mục build” đúng khi mở `artifacts\publish\HapiCopi.exe`.

### 4.5 `UI/ClipboardHistoryForm.cs` — CHƯA VIẾT LẠI (nặng nhất)

Ảnh cũ: hai nút chính cùng chữ **“Sao chép bản”** (gốc vs mới). Đây là màn hình đầu sau cài khi bấm shortcut.

Còn: “Xem trước”→“Xem”, “Thanh tra”→“Thanh”; empty state đen trống; tông tối; `FormBorderStyle.None` 1000×640 không resize; panel phải không tiêu đề.

**Giữ:** `EnableSvgDrop` (kéo `.svg`); preview gốc/đã xử lý; inspector formats; mask PII + checkbox hiện gốc; copy gốc / copy đã xử lý. Đổi theme + layout, đừng phá hành vi.

Title bar tự vẽ + `ReleaseCapture`/`HT_CAPTION`: có thể bỏ nếu chuyển `Sizable` + caption Windows (sạch hơn, khớp Settings/Update). User muốn rõ ràng, không fancy.

### 4.6 Chưa audit

`PiiToastForm`, `MathEditorForm`, `AbcEditorForm`. Toast có thể giữ tối (thông báo nổi). Editor WebView2: ít nhất `AutoScaleMode` + không cắt chrome.

`CreateAppIcon()` trong `HapiCopiApplicationContext` vẫn vẽ 16×16 — thay `UiTheme.AppIcon`.

Menu tray title vẫn `"HapiCopi v2.0 - ..."` — lệch version 1.2.0. Nên `v{UpdateManager.CurrentVersionText}`.

---

## 5. Việc còn lại — thứ tự bắt buộc

1. **Verify SettingsForm** bằng `tools/capture-window.ps1` (không fullscreen). Sửa wrap nếu còn phình/cắt.
2. Viết lại **UpdateForm** theo `UiTheme` + sửa `SelectSensibleRow`.
3. Viết lại **ClipboardHistoryForm** tông sáng, nút đủ chữ, empty state, resize được, giữ SVG drop.
4. `CreateAppIcon` → `UiTheme.AppIcon`; sửa title tray cho khớp version.
5. Lướt toast + 2 editor: cắt chữ / DPI.
6. Chụp 3 cửa sổ chính ở **100% / 125% / 150%** nếu đổi được DPI; không thì ít nhất 125% (máy user) + ghi rõ chưa thử mức khác.
7. `build.ps1 -Version 1.2.0`, cài, `--quit`, gỡ silent (không còn `Programs\HapiCopi`), mở UI trên bản cài.
8. Cập nhật `testing/Bug.txt` (đã sửa vòng đời). `architecture.md` nếu thiếu file mới.
9. **Commit một lần** (vòng đời + UI) theo `.AGENT/git.md` — **không** `git commit` thường (bị trailer Cursor). User chưa bảo push thì đừng push.

---

## 6. Công cụ & lệnh

### Chụp cửa sổ — `tools/capture-window.ps1`

`PrintWindow` từng HWND process `HapiCopi`. Có `SetProcessDPIAware()`.

```powershell
# Tắt bản cũ, publish, mở settings
Get-Process HapiCopi -ErrorAction SilentlyContinue | Stop-Process -Force
.\installer\build.ps1 -Version 1.2.0 -SkipInno
Start-Process ".\artifacts\publish\HapiCopi.exe"
Start-Sleep 12
Start-Process ".\artifacts\publish\HapiCopi.exe" -ArgumentList "--settings"
Start-Sleep 4
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\capture-window.ps1" -TitleLike "*" -OutDir ".\testing\shots"
```

Ảnh cũ tham chiếu (có thể đã đổi tên / mất):
`%LOCALAPPDATA%\Cursor\AgentStores\cursor_agent_stores\cc406d53-5b58-4aaf-89e2-b117fb2cbed7\files\shots\`
(`settings.png`, `update.png`, `history.png` — trạng thái **trước** rewrite Settings).

**Cấm** `CopyFromScreen` toàn màn hình.

### Build

```powershell
.\installer\build.ps1 -Version 1.2.0 -SkipInno
.\installer\build.ps1 -Version 1.2.0
```

`--quit` trên bản **cài** (`%LocalAppData%\Programs\HapiCopi\HapiCopi.exe`), không chỉ bản publish.

---

## 7. Commit (khi user bảo)

Đọc `.AGENT/git.md`. `git commit` **và** `commit-tree -F` đều bị chèn `Co-authored-by: Cursor`.
Cách còn dùng: tự dựng object commit → `hash-object -t commit -w` → `update-ref -m "..." HEAD <sha>`.
Verify `git cat-file -p HEAD` không có trailer.

Gợi ý message (gộp, vì user muốn một commit):

```
fix: stop and uninstall cleanly, and stop clipping UI text at 125% DPI

App is a tray process, so the installer must ask it to quit over the
named pipe instead of hoping Restart Manager finds a window. Logs and
diagnostics leave the install folder so uninstall can delete it. Forms
scale with font size instead of hard pixel boxes so labels stay readable
on a 125% display.
```

Không commit `artifacts/`, `dist/`, `idea/`, `.env`.

---

## 8. Việc không nằm trong scope này (đã xong phiên trước)

- SVG → PNG offline từ mọi clipboard (trừ copy từ IDE).
- Update/rollback positional + feed local.
- Những thứ đó **đã trên `main`**. Đừng revert.

Repo private → GitHub Releases chưa phải nguồn update công khai. Feed local vẫn đúng.
