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

## Trạng thái

Đã implement bộ khung (2026-09-21). Chạy `build.ps1` để ra file setup.
