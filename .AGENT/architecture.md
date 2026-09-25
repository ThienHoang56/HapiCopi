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
| `Core/` | Clipboard, PII, history, hook, WebViewManager, **AppInstanceChannel**, **StartupManager**, Update* |
| `UI/` | Tray, toast, settings, update, history, Math/ABC editor, **UiTheme** |
| `Formatting/` | Auto format + render sơ đồ |
| `WebViewResources/` | HTML/JS offline (mermaid, math, abc…) |
| `Assets/` | Icon |
| `Demo/` | Tài liệu / slide pitch |

## Không phải lõi

- Math Editor / ABC Editor / sơ đồ: tiện ích phụ.
- Lõi sản phẩm (theo `Demo/HE_THONG_HAPICOPI.md`): **Clipboard DLP offline**.

Việc đang dở (vòng đời đã xong, UI chưa xong): `.AGENT/handoff-ui-va-vong-doi.md`.
