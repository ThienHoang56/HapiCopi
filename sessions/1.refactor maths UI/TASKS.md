# TASKS — Refactor Math Editor UI
> Session: `1.refactor maths UI` · Ngày: 2026-10-15
> File thực thi: `WebViewResources/matheditor.html`, `UI/MathEditorForm.cs`, `UI/HapiCopiApplicationContext.cs`

---

## Quy ước trạng thái
- `[ ]` Chưa làm  
- `[~]` Đang làm  
- `[x]` Hoàn thành  
- `[!]` Bị chặn / cần quyết định

---

## Phase 0 — Chuẩn bị (≈ 15 phút)

- [x] **T0-1** Đọc lại `SPEC.md`, `A_spec` và 2 hình design để nắm layout
- [x] **T0-2** Backup `matheditor.html` → `matheditor.html.bak` trước khi sửa
- [x] **T0-3** Xác nhận trạng thái nền tảng và kiến trúc trước khi can thiệp

---

## Phase 1 — Design Tokens & Base Styles (≈ 30 phút)

- [x] **T1-1** Thay toàn bộ `#2563eb` / `#1d4ed8` / `#1e40af` (xanh dương) bằng token Teal HapiCopi (#0E7C86) theo `A_spec` & `SPEC.md`
  - Primary: `#0E7C86`
  - Hover: `#0a6470`
  - Light: `#CFF1F3`
  - Ring: `rgba(14,124,134,0.20)`
- [x] **T1-2** Khai báo CSS custom properties (`--color-primary`, `--font-ui`, `--font-math`, `--font-mono`, v.v.) ở đầu `<style>` để tập trung quản lý
- [x] **T1-3** Cập nhật `MathLive` CSS variables:
  ```css
  --hue: 186;               /* teal */
  --primary-color: #0E7C86;
  --selection-background-color: #CFF1F3;
  --caret-color: #0E7C86;
  ```
- [x] **T1-4** Cập nhật màu `.ribbon-tab.active`, `.mode-tab.active`, `.btn-primary`, `.btn-sm-primary`
- [x] **T1-5** Cập nhật màu `.ribbon-content button:hover`, `.ink-tool-btn.active`
- [x] **T1-6** Cập nhật focus ring trên `.editor-card:focus-within`, `.ink-canvas-card:focus-within`
- [x] **T1-7** Kiểm tra: xác nhận không còn màu xanh dương nào sót trong HTML/CSS

---

## Phase 2 — Mini Popup Layout (≈ 45 phút)

- [x] **T2-1** Thêm CSS class `.mode-mini` và `.mode-fullscreen` vào `<body>` tag
  - Hỗ trợ cả C# bridge inject qua `setDisplayMode('mini')` và JavaScript toggle
- [x] **T2-2** Ẩn sidebar bằng CSS khi `.mode-mini`
- [x] **T2-3** Thêm **Expand button** (↗ icon) và **Collapse button** (↙ icon) vào header toolbar:
  - Hiển thị theo mode tương ứng
  - Gửi message `{type:'expand'}` / `{type:'collapse'}` lên C# để resize form mượt mà
- [x] **T2-4** Thêm **Quick-Insert Row** vào HTML giữa workspace và footer:
  - 7 nút công thức (`a/b`, `√`, `Σ`, `∫`, `xⁿ`, `α`, `π`), nút `···` (mở ribbon), và nút `+ Thêm ⏳`
  - Chỉ hiển thị khi `.mode-mini` và mode tab `wysiwyg`/`latex` đang active
- [x] **T2-5** CSS cho `.quick-insert-row`: height 36px, flex row, gap 5px, border-top, bg white
- [x] **T2-6** Nút `[+ Thêm ⏳]` — disabled, cursor not-allowed, amber border, tooltip "Coming Soon"
- [x] **T2-7** JS: hỗ trợ cả `mousedown` và `click` an toàn (debounce chống double-insert)

---

## Phase 3 — Full-screen Sidebar (≈ 60 phút)

- [x] **T3-1** Thêm `<aside id="sidebar" class="sidebar">` vào layout bên cạnh cột chính
- [x] **T3-2** CSS: sidebar width 310px, nền sáng `#F6F7F9` (Chuẩn A_spec/SPEC.md — Toàn bộ Light), viền trái `#E3E6EC`, overflow-y auto
- [x] **T3-3** Ẩn sidebar khi `.mode-mini`, hiển thị khi `.mode-fullscreen`
- [x] **T3-4** Section **MẪU CÔNG THỨC**: 5 template cards chức năng (Phân số, Căn thức, Sigma, Tích phân, Giới hạn)
  - Mỗi card: click/mousedown → inject LaTeX tương ứng vào `mathField`
- [x] **T3-5** Thẻ **Custom Template Placeholder**:
  - Viền: `1.5px dashed #d97706`
  - Badge: `⏳ Coming Soon`
  - Label: `+ Thêm mẫu riêng`
  - Trạng thái: disabled, cursor not-allowed, opacity 0.85
- [x] **T3-6** Section **THUỘC TÍNH**: Kiểu select (Toàn bộ / Nội dòng), Kích thước select (đồng bộ 2 chiều với Header), Màu chữ toggle (Đen / Đỏ)
- [x] **T3-7** Section **MÃ LATEX**: readonly monospace `<textarea>` sync 2 chiều tức thời với `mathField`
- [x] **T3-8** Section **XUẤT RA**: 3 nút [Word] [PNG] [SVG]
- [x] **T3-9** Toggle button `Mẫu & Xuất` trong header: ẩn/hiện sidebar linh hoạt
- [x] **T3-10** Responsive: khi `window.innerWidth < 700px` → sidebar tự động ẩn

---

## Phase 4 — Responsive & Polish (≈ 30 phút)

- [x] **T4-1** Kiểm tra Mini Popup ở các kích thước và responsive breakpoint
- [x] **T4-2** Kiểm tra Full-screen ở 900×600, 1280×720
- [x] **T4-3** Ribbon symbols: auto-wrap mượt mà, cuộn mỏng (thin scrollbar)
- [x] **T4-4** CSS transition mượt mà cho hover, active, dialog popups
- [x] **T4-5** Focus ring xanh teal `#0E7C86` xuất hiện đúng chuẩn trên mọi interactive element

---

## Phase 5 — Kiểm thử & Chụp ảnh (≈ 20 phút)

- [x] **T5-1** Kiểm tra luồng Mini Popup: Quick-insert, expand to fullscreen
- [x] **T5-2** Kiểm tra luồng Full-screen: Mẫu công thức, chỉnh thuộc tính, xuất SVG/Word
- [x] **T5-3** Kiểm tra AI Math & Distillation & Topology (100% test pass, zero regression)
- [x] **T5-4** Chụp ảnh kết quả thực tế qua browser automation → lưu tại `testing/shots/after_refactor_mini.png` và `testing/shots/after_refactor_fullscreen.png`
- [x] **T5-5** So sánh thực tế với bản thiết kế: màu Teal `#0E7C86`, bố cục 2 cột, font typography chuẩn

---

## Phase 6 — C# Integration (≈ 20 phút)

- [x] **T6-1** Trong `MathEditorForm.cs`: thêm hàm `SetDisplayMode(bool isMini)`
- [x] **T6-2** Trong `HapiCopiApplicationContext.cs`: tự động nhận diện nếu bấm `Ctrl+Alt+M` từ Word hoặc Docs thì mở Mini Popup (560×360); nếu mở độc lập/Tray thì mở Full-screen (900×600)
- [x] **T6-3** Xử lý message `{type:'expand'}` và `{type:'collapse'}` trong C# `OnWebMessage` để resize cửa sổ đồng bộ
- [x] **T6-4** Cập nhật `MinimumSize` theo mode (Mini: 420×280, Full: 700×450)

---

## Checklist cuối

```
[x] Không còn màu xanh dương (#2563eb) trong HTML/CSS
[x] Mini popup hoạt động đúng tại con trỏ Word (560×360, resizable, min 420×280)
[x] Full-screen có sidebar với template gallery (900×600, resizable, min 700×450)
[x] Custom template placeholder hiển thị đúng (disabled, Coming Soon)
[x] Quick-insert row hoạt động đúng
[x] Responsive tại các breakpoint đã kiểm tra (< 700px tự thu gọn sidebar)
[x] Không có regression về logic AI / MathLive / ink canvas (18/18 topo pass, 6/6 distill pass)
[x] Ảnh before/after đã chụp và lưu vào testing/shots/
```
