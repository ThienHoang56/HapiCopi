# Đóng gói & trải nghiệm user

Mục tiêu: user thấy **tiện và sướng** — không phải thư mục DLL.

## Trải nghiệm mục tiêu

1. **Cài:** tải 1 file `HapiCopi-Setup.exe` → Next → xong  
2. **Chạy:** Start Menu (+ Desktop tuỳ chọn); app tray nền  
3. **Gỡ:** *Cài đặt → Ứng dụng → Uninstall* (xóa app + `%LocalAppData%\HapiCopi`)  
4. **Cài per-user** (không cần admin): `%LocalAppData%\Programs\HapiCopi\`

## Cách build

```powershell
.\installer\build.ps1
```

- Publish: `artifacts\publish\` (single-file win-x64 self-contained)  
- Setup: `artifacts\installer\HapiCopi-Setup.exe`  
- Cần [Inno Setup 6](https://jrsoftware.org/isdl.php) — script tự thử `winget install JRSoftware.InnoSetup`  
- Chỉ publish: `.\installer\build.ps1 -SkipInno`

## Code liên quan

| File | Vai trò |
|------|---------|
| `installer/HapiCopi.iss` | Inno: cài / shortcut / autostart / uninstall |
| `installer/build.ps1` | publish + ISCC |
| `Core/ResourcePaths.cs` | Đường dẫn WebViewResources + AppData |

## Không làm

- Phát hành zip cả folder `dist/` cho end-user  
- Commit `artifacts/` / `dist/` (đã có trong `.gitignore`)

## Vòng đời app (2026-09-24)

App chạy ẩn ở khay nên **không có cửa sổ để Windows gửi `WM_CLOSE`**. Mọi thao tác
cài đè / gỡ đều phải đi qua kênh lệnh của chính app:

- `Core/AppInstanceChannel.cs` — named pipe `HapiCopi.Command.<user>`.
- `HapiCopi.exe --quit` tắt êm app đang chạy rồi chờ tới khi tiến trình biến mất.
- `HapiCopi.exe --show | --settings | --update` chuyển lệnh cho instance đang chạy
  thay vì thoát im lặng (bấm shortcut lúc app đang chạy phải thấy cửa sổ hiện lên).
- `HapiCopi.iss` mục `[Code]` gọi `--quit` ở `PrepareToInstall` và `InitializeUninstall`,
  kiểm tra bằng `tasklist`, hết 10s mới `taskkill /F`.

Dữ liệu trên đĩa, để bộ gỡ dọn đúng chỗ:

| Đường dẫn | Nội dung | Khi gỡ |
|-----------|----------|--------|
| `%LocalAppData%\Programs\HapiCopi` | exe + WebViewResources | xoá cả thư mục |
| `%LocalAppData%\HapiCopi\Logs` | `hapicopi.log`, dump chẩn đoán | xoá |
| `%LocalAppData%\HapiCopi\WebViewCache` | cache Chromium | xoá |
| `%LocalAppData%\HapiCopi\Releases`, `\Updates` | nguồn + bộ cài đã tải | xoá |
| `%AppData%\HapiCopi` | lịch sử clipboard, config, loại trừ | **hỏi user**, mặc định xoá |

Không bao giờ ghi file lúc chạy vào thư mục cài đặt: bộ gỡ chỉ xoá file nó đã cài,
file lạ sẽ giữ cả thư mục lại.

## Trạng thái

Bộ khung 2026-09-21. Vòng đời + gỡ sạch 2026-09-24 (đã thử thật, xem
[handoff-ui-va-vong-doi.md](./handoff-ui-va-vong-doi.md)). Chạy `build.ps1` để ra file setup.
