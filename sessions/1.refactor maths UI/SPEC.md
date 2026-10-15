# SPEC — Thiết kế lại Math Editor UI
> Session: `1.refactor maths UI` · Ngày: 2026-10-15

---

## 1. Bối cảnh & Mục tiêu

HapiCopi hiện có cửa sổ soạn công thức toán (`matheditor.html`) với màu chủ đạo **xanh dương** (`#2563eb`). Bản refactor này chuyển sang **teal** (`#0E7C86`) — đồng nhất màu brand mới của HapiCopi, trưởng thành và dễ nhận diện hơn — và tái cấu trúc layout thành 2 chế độ hiển thị rõ ràng:

| Chế độ | Kích thước khởi đầu | Khi nào xuất hiện |
|--------|--------------------|--------------------|
| **Mini Popup** | 560 × 360 px (resizable) | Người dùng nhấn Ctrl+Alt+M khi con trỏ đang trong Word / Google Docs |
| **Full-screen** | 900 × 600 px (resizable, có sidebar) | Người dùng bấm nút phóng to hoặc mở độc lập từ System Tray |

---

## 2. Design Token (Màu & Typography)

```css
/* Màu chủ đạo — Teal HapiCopi (đồng nhất toàn app) */
--color-primary:        #0E7C86;   /* teal — màu nhấn DUY NHẤT */
--color-primary-hover:  #0a6470;   /* teal đậm khi hover */
--color-primary-light:  #CFF1F3;   /* teal nhạt — tô vùng đang chọn */
--color-primary-ring:   rgba(14,124,134,0.20);

/* Nền & bề mặt — TOÀN BỘ LIGHT */
--color-bg:             #F6F7F9;   /* nền cửa sổ, vùng soạn */
--color-surface:        #ffffff;   /* thanh header, panel, thẻ */
--color-sidebar-bg:     #F6F7F9;   /* sidebar xám rất nhạt */

/* Chữ */
--color-text-primary:   #1B2230;   /* xanh đen — không dùng #000 */
--color-text-muted:     #5B6578;   /* chữ phụ, nhãn section */
--color-text-disabled:  #9AA3B2;

/* Viền */
--color-border:         #E3E6EC;   /* rất nhạt — không viền đậm */
--color-border-focus:   #0E7C86;

/* Trạng thái */
--color-warning:        #d97706;   /* amber — badge "Coming Soon" */
--color-danger:         #dc2626;

/* Typography */
--font-ui:     'IBM Plex Sans', 'Segoe UI', system-ui, sans-serif;
--font-math:   'STIX Two Text', 'Cambria Math', serif;
--font-mono:   'IBM Plex Mono', 'Consolas', monospace;
```

> **Nguyên tắc màu sắc**: Teal `#0E7C86` CHỈ xuất hiện tại nút chính, tab active (nền nhạt + chữ đậm), viền focus, và vùng đang chọn trong công thức. Còn lại là trắng `#fff` và xám rất nhạt `#F6F7F9`.

---

## 2b. Typography System — Hệ thống kiểu chữ

| Role | Font | Size | Weight | Color | Dùng cho |
|------|------|------|--------|-------|----------|
| **section-label** | IBM Plex Sans | 12px | 600 | `#5B6578` | MẪU CÔNG THỨC, THUỘC TÍNH… — UPPERCASE + letter-spacing 0.06em |
| **body-sm** | IBM Plex Sans | 13px | 500 | `#1B2230` | Button text, tab text, nhãn input, card label |
| **title** | IBM Plex Sans | 13px | 600 | `#1B2230` | Tên công thức trong header |
| **muted** | IBM Plex Sans | 12px | 400 | `#5B6578` | Keyboard hints, placeholder, "Đã lưu", status bar |
| **primary-btn** | IBM Plex Sans | 13px | 600 | `#ffffff` | Text trong nút teal chính |
| **math-canvas** | STIX Two Text | 40px | — | `#1B2230` | Công thức trên canvas — biến in nghiêng chuẩn sách toán |
| **math-preview** | STIX Two Text | 26px | — | `#1B2230` | Preview trong mini popup |
| **code** | IBM Plex Mono | 12px | 400 | `#1B2230` | LaTeX code box, nền `#F6F7F9` |

### Quy tắc

1. **Section header** → UPPERCASE + letter-spacing + màu `#5B6578`. Không bold đen.
2. **Viền rất nhạt** `#E3E6EC` — không dùng viền đậm, không đổ bóng nặng.
3. **Spacing**: khoảng cách giữa các phần 14–16px · giữa nút trong lưới 6–8px.
4. **Bo góc**: nút & ô nhập `8px` · thẻ mẫu `10px` · khung công thức `14px` · cửa sổ ngoài `12px`.
5. **Button height**: 36px (header) · 32px (sidebar) · 28px (quick-insert).
6. **Icon SVG**: stroke-width 1.5px, size 14–16px.

---

## 3. Chế độ A — Mini Popup (tại con trỏ Word / Docs)

### 3.1 Layout (560 × 360 px, resizable)

```
┌─────────────────────────────────────────────────────────┐
│ HEADER (44px)                                           │
│  [🌿 HapiCopi] [Gõ ▪ Viết tay ▪ LaTeX]   [Cỡ] [Chèn→]│
├─────────────────────────────────────────────────────────┤
│ RIBBON TABS (28px)                                      │
│  Cấu trúc | Toán tử & Ma trận | Quan hệ | Tập hợp | Hy Lạp│
├─────────────────────────────────────────────────────────┤
│ RIBBON SYMBOLS (44px)                                   │
│  [√x] [a/b] [Σ] [∫] [x²] [lim] [α] [β] [π] [∇] …     │
├─────────────────────────────────────────────────────────┤
│ EDITOR CARD (flex-grow)                                 │
│  ┌─────────────────────────────────────────────────┐   │
│  │  MathLive field (xanh lá khi focus)              │   │
│  │  H = 12/n(n+1) × [(ΣR_i)²/n₁]  |cursor|        │   │
│  └─────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│ QUICK-INSERT ROW (34px)                                 │
│  [a/b] [√] [Σ] [∫] [xⁿ] [α] [π] [···]  [+ Thêm]      │
├─────────────────────────────────────────────────────────┤
│ FOOTER (32px)                                           │
│  [Ctrl+Enter] Chèn ·  [Esc] Đóng          [Chèn vào Word]│
└─────────────────────────────────────────────────────────┘
```

### 3.2 Hành vi đặc biệt
- **Smart positioning**: Popup xuất hiện sát vị trí con trỏ Word, không che khuất văn bản đang soạn. Nếu sát đáy màn hình thì mở lên trên.
- **Drag handle**: Kéo từ thanh Header để di chuyển (không dùng title bar Windows).
- **Resize**: Cho phép kéo rộng/thu hẹp; min-width = 420px, min-height = 280px.
- **Always On Top**: Luôn nổi trên Word/Docs cho đến khi Esc hoặc Chèn xong.
- **Expand button** (↗): Góc phải header — mở chuyển sang Full-screen mode.

---

## 4. Chế độ B — Full-screen Editor (2 cột)

### 4.1 Layout (900 × 600 px, resizable)

```
┌──────────────────────────────────────┬─────────────────────┐
│ LEFT PANEL (flex, min 55%)           │ RIGHT SIDEBAR (320px)│
│                                      │                      │
│  HEADER (44px) — full controls       │  ─ MẪU CÔNG THỨC ─  │
│  RIBBON TABS + SYMBOLS               │  [Phân số] [Căn thức]│
│  ─────────────────────────────────   │  [Sigma]   [Tích phân]│
│  EDITOR / INK / LATEX workspace      │  [Số mũ]   [Giới hạn]│
│  (flex-grow)                         │  [+ Thêm mẫu riêng] │  ← placeholder
│                                      │  ─ THUỘC TÍNH ─      │
│  FOOTER (32px)                       │  Kiểu:    [▼ Toàn bộ]│
│                                      │  Kích thước: [▼ 11pt]│
│                                      │  Màu chữ: [●Đen][○Đỏ]│
│                                      │  ─ MÃ LATEX ─        │
│                                      │  [monospace readonly] │
│                                      │  ─ XUẤT RA ─         │
│                                      │  [W Word][PNG][SVG]  │
└──────────────────────────────────────┴─────────────────────┘
```

### 4.2 Sidebar — Màu sắc & Mẫu Công thức (Template Gallery)
- **Sidebar background**: `#f1f5f9` (xám nhạt), border-left `#e2e8f0`. **Không tối**.
- **6 thẻ template**: Phân số, Căn thức, Sigma, Tích phân, Số mũ, Giới hạn.
- Mỗi thẻ: bg `#ffffff`, border `#e2e8f0`, hover border `#16a34a`. Hiển thị công thức render nhỏ + tên phía dưới.
- **Thẻ thứ 7** — placeholder "Custom Template":
  - Viền đứt nét `border: 2px dashed #d97706`
  - Label: `+ Thêm mẫu riêng`
  - Badge amber: `⏳ Coming Soon`
  - Trạng thái: disabled (cursor: not-allowed), opacity 0.6
  - *Ghi chú kỹ thuật*: Khi tính năng sẵn sàng, thẻ này sẽ mở modal lưu công thức hiện tại thành template.

### 4.3 Resize & Responsive
- Sidebar có thể ẩn/hiện qua toggle button (☰ Sidebar).
- Khi cửa sổ < 700px width: sidebar ẩn, left panel chiếm 100%.
- Ribbon symbols wrap tự động khi width thu hẹp.

---

## 5. Ba Mode nhập liệu (giữ nguyên logic, đổi màu)

| Tab | ID hiện tại | Màu active cũ | Màu active mới |
|-----|-------------|---------------|----------------|
| Gõ công thức | `wysiwyg` | `#2563eb` blue | `#16a34a` green |
| Viết tay AI | `ink` | `#2563eb` blue | `#16a34a` green |
| Mã LaTeX | `latex` | `#2563eb` blue | `#16a34a` green |

---

## 6. Quick-Insert Row (Mới — Mini Popup)

Hàng nằm giữa Editor Card và Footer, chỉ xuất hiện ở Mini Popup mode:

| Nút | LaTeX inject | Tooltip |
|-----|-------------|---------|
| a/b | `\frac{}{}` | Phân số |
| √ | `\sqrt{}` | Căn bậc 2 |
| Σ | `\sum_{}^{}` | Sigma |
| ∫ | `\int_{}^{}` | Tích phân |
| xⁿ | `^{}` | Số mũ |
| α | `\alpha` | Alpha |
| π | `\pi` | Pi |
| ··· | (mở ribbon) | Xem thêm |
| **+ Thêm** | — | Placeholder (disabled, Coming Soon) |

---

## 7. Accessibility & Polish

- **Focus ring**: xanh lá (`box-shadow: 0 0 0 3px var(--color-primary-ring)`) trên mọi element interactive.
- **Keyboard nav**: Tab qua toàn bộ ribbon → editor → quick-insert → footer.
- **High-DPI**: Các SVG icon scale theo `viewBox`, không dùng pixel cứng.
- **Transition**: `all 0.15s ease` cho hover, `0.2s cubic-bezier(0.16,1,0.3,1)` cho modal/popup appear.
- **Scrollbar styling**: Ribbon symbols dùng thin scrollbar (`4px`, màu xanh lá).

---

## 8. Không thay đổi

- Toàn bộ **logic JS** nhận diện AI (ONNX), MathLive API calls, canvas ink.
- Toàn bộ **C# bridge** (`MathEditorForm.cs`) — chỉ HTML/CSS thay đổi.
- File `matheditor.html` — refactor in-place, **không tạo file mới**.
- Phím tắt: `Ctrl+Enter`, `Ctrl+M`, `Esc` giữ nguyên.
