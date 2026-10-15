# Session 1 — Refactor Math Editor UI

> Ngày khởi tạo: 2026-10-15

## Nội dung session

| File | Mô tả |
|------|-------|
| [SPEC.md](SPEC.md) | Đặc tả thiết kế chi tiết (layout, màu sắc, token, hành vi) |
| [TASKS.md](TASKS.md) | Danh sách task thực thi theo phase (6 phases, ~3.5h) |
| [design_mini_popup.jpg](design_mini_popup.jpg) | Hình mong đợi — Giao diện Mini Popup tại con trỏ Word |
| [design_fullscreen.jpg](design_fullscreen.jpg) | Hình mong đợi — Giao diện Full-screen với sidebar |

## Tóm tắt thiết kế

### Mini Popup (tại con trỏ Word/Docs)
- Floating card nhỏ gọn (560×360px, resizable)
- Dark header + xanh lá accent
- Quick-Insert Row (a/b, √, Σ, ∫, xⁿ, α, π, ···) — NEW
- Nút Expand (↗) để mở full-screen
- Always On Top

### Full-screen Editor
- Layout 2 cột: Editor (65%) + Sidebar (35%)
- Sidebar tối với Template Gallery (6 mẫu + 1 Custom placeholder Coming Soon)
- Sections: Mẫu CT | Thuộc tính | Mã LaTeX | Xuất ra
- Toggle ẩn/hiện sidebar
- Responsive: < 700px sidebar tự ẩn

## Màu chủ đạo

| Token | Giá trị | Mô tả |
|-------|---------|-------|
| `--color-primary` | `#16a34a` | Xanh lá HapiCopi (thay thế xanh dương cũ) |
| `--color-primary-hover` | `#15803d` | Hover state |
| `--color-warning` | `#d97706` | Badge Coming Soon |

## File thực thi

```
WebViewResources/matheditor.html   ← sửa in-place
UI/MathEditorForm.cs               ← thêm mode injection (Phase 6)
```
