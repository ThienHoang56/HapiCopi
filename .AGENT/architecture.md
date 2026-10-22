# Kiến trúc tối giản

HapiCopi là **app desktop Windows** (WinForms tray), **không** theo mô hình server–client.

```
Người dùng (Ctrl+C / Ctrl+V / Bấm Ctrl+Alt mở Math Editor)
        │
        ▼
 HapiCopiApplicationContext (tray)
        │
        ├─────────────────────────────┐
        ▼                             ▼
AutoFormatter / Diagram         MathEditorForm
(Word OMML, LaTeX, Mermaid)     (Offline ONNX & Tự học nét)
        │
        ▼
History (Local DPAPI, Async Debounced)
```

## Thư mục source chính

| Thư mục | Vai trò |
|---------|---------|
| `Core/` | Lõi xử lý chia theo module: `Clipboard/`, `Formatters/`, `Math/`, `Storage/`, `System/` |
| `UI/` | Tray, settings, history, MathEditorForm, **UiTheme** |
| `Formatting/` | Auto format + render sơ đồ (Mermaid, TikZ, Kroki) |
| `WebViewResources/` | HTML/JS offline (mermaid, math, history…) |
| `Assets/` | Icon & hình ảnh |
| `Demo/` | Tài liệu / slide pitch |

## Hai trụ cột cốt lõi

1. **Local Clipboard Auto-Formatting**: Tự động nhận diện và chuyển đổi công thức Markdown/LaTeX sang Word OMML và hình ảnh đồ thị tức thì (< 5 ms).
2. **AI Math Handwriting Engine**: Bảng vẽ nhận diện công thức toán offline dựa trên mô hình ONNX siêu nhẹ (25 MB E2E) và tự học nét cá nhân.
