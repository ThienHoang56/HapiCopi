# Kiến trúc tối giản

HapiCopi là **app desktop Windows** (WinForms tray), **không** theo mô hình server–client.

```
Người dùng (Ctrl+C / Ctrl+V)
        │
        ▼
 HapiCopiApplicationContext (tray)
        │
 ClipboardListener
        │
   ┌────┴────┐
   ▼         ▼
PiiScanner  AutoFormatter / Diagram
(offline)   (Mermaid offline, TikZ local LaTeX, Kroki optional)
   │
   ▼
PiiToast + History (local, có thể mã hóa)
```

## Thư mục source chính

| Thư mục | Vai trò |
|---------|---------|
| `Core/` | Lõi xử lý chia theo module: `Clipboard/`, `Dlp/`, `Formatters/`, `Math/`, `Storage/`, `System/` |
| `UI/` | Tray, toast, settings, history, MathEditorForm, **UiTheme** |
| `Formatting/` | Auto format + render sơ đồ (Mermaid, TikZ, Kroki) |
| `WebViewResources/` | HTML/JS offline (mermaid, math, history…) |
| `Assets/` | Icon & hình ảnh |
| `Demo/` | Tài liệu / slide pitch |

## Hai trụ cột cốt lõi

1. **Local Clipboard DLP & Formatting**: Quét thông tin nhạy cảm offline, tự động định dạng Markdown/LaTeX sang Word OMML.
2. **AI Math Handwriting Engine**: Bảng vẽ nhận diện công thức toán offline dựa trên mô hình ONNX siêu nhẹ và tự học nét cá nhân.
