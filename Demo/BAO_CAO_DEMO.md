# Báo cáo Demo HapiCopi — Định dạng & Bảo mật khi Copy/Paste

> Mục tiêu: chứng minh HapiCopi xử lý tốt những trường hợp mà **Google Docs / Word dán trực tiếp làm vỡ định dạng hoặc làm lộ dữ liệu nhạy cảm**.

---

## 1. Vấn đề gốc

Khi bạn copy nội dung từ web/PDF/ChatGPT rồi `Ctrl+V` vào Google Docs hoặc Word:

- Nội dung **kéo theo "trang phục" của nguồn**: nền màu, font lạ, cỡ chữ cứng, khoảng cách dòng, viền bảng — gây "lệch pha" với style của tài liệu đích.
- Người dùng buộc phải `Ctrl+Shift+V` (dán không định dạng), nhưng cách này **mất luôn cấu trúc**: tiêu đề, danh sách, bảng, công thức đều bẹp thành một khối text thô.

→ HapiCopi đi giữa: **giữ cấu trúc (heading, list, bảng), lột bỏ trang phục (nền/font/cỡ cứng), để tài liệu đích tự áp style của nó.**

---

## 2. Các nguồn web Docs xử lý KÉM (dùng làm nguồn copy khi demo)

| Nguồn | Vì sao dán vào Docs bị xấu | HapiCopi xử lý |
|---|---|---|
| **scikit-learn docs** (vd trang `IsolationForest`) | Code block nền tối, bảng API, callout màu → Docs giữ nguyên nền đen chữ xanh, lệch font | Làm sạch nền/font, giữ heading + bảng + code |
| **Wikipedia** (bài có bảng + công thức) | Bảng mang viền/nền xám, superscript công thức rối | Giữ bảng sạch, để Docs áp style bảng |
| **Câu trả lời ChatGPT/Gemini** | HTML rối, span lồng span, inline-style dày đặc | Chuyển Markdown/AI-HTML → HTML gọn cho Office |
| **Medium / GeeksforGeeks / blog kỹ thuật** | Nền callout, font Serif tùy biến, code highlight | Bỏ nền/font, giữ list + code |
| **PDF (arXiv, giáo trình)** | Copy bị **đứt dòng giữa câu**, mỗi dòng một đoạn | Nối lại các dòng đứt thành đoạn liền |
| **Notion / Confluence export** | Inline-style + data-attribute lạ | Lột style, giữ cấu trúc |

> Hai trang HTML kèm theo (`isolation_forest.html`, `confidential_case.html`) được tạo có chủ đích "bẩn" giống các nguồn trên để demo offline, không phụ thuộc mạng.

---

## 3. CASE 1 — Giữ cấu trúc, lột định dạng (file: `isolation_forest.html`)

**Nội dung:** bài kỹ thuật về thuật toán Isolation Forest — có đủ: tiêu đề, đoạn văn, **callout nền vàng**, **bảng so sánh**, **code block nền đen**, **công thức toán**, danh sách lồng nhau, blockquote, font Georgia/Trebuchet.

### Cách demo
1. Mở `Demo/isolation_forest.html` bằng trình duyệt.
2. `Ctrl+A` → `Ctrl+C` toàn trang.
3. **So sánh 2 lần dán:**
   - **Dán THÔ (không có HapiCopi / Ctrl+Shift+V):** Docs hoặc giữ nguyên nền vàng + code đen lệch pha, hoặc mất sạch bảng/heading.
   - **Dán QUA HapiCopi (`Ctrl+V`):** giữ heading, bảng, danh sách, code; bỏ nền màu và font cứng; **Docs tự áp style của tài liệu**.

### Điểm cần chỉ ra khi demo
- Bảng "So sánh phương pháp" và "Siêu tham số" vẫn là **bảng thật** (không bị bẹp).
- Tiêu đề `h1/h2/h3` map sang Heading của Docs → mục lục tự động vẫn chạy.
- Nền vàng callout và nền đen code **biến mất**, chữ về theo theme tài liệu.

### Bonus: Mermaid → ảnh sơ đồ
Mục **"7. Sơ đồ luồng thuật toán (Mermaid)"** chứa một khối ```` ```mermaid ````. Bôi đen riêng khối đó → `Ctrl+C` → dán vào Word/Docs: HapiCopi **render code Mermaid thành ảnh sơ đồ** (offline qua WebView2, fallback online nếu cần). Đây là điểm "wow" trực quan cho người xem.

---

## 4. CASE 2 — Chặn rò rỉ thông tin mật / DLP (file: `confidential_case.html`)

**Nội dung:** một "Báo cáo nội bộ – MẬT" (dữ liệu GIẢ) nhúng nhiều loại bí mật. Tất cả đã được căn để **chắc chắn khớp bộ quét** của HapiCopi:

| Loại | Giá trị giả trong file | Engine bắt |
|---|---|---|
| API Key (OpenAI) | `sk-proj-3xZ9...E4rT` | Regex OpenAI |
| API Key (AWS) | `AKIA4FH7XK2L9QWE5RTY` | Regex AWS |
| API Key (GitHub) | `ghp_...7hJ4kL` (≥36 ký tự) | Regex GitHub |
| Thẻ tín dụng | `4111 1111 1111 1111`, `5555 5555 5555 4444` | Regex + **Luhn check** |
| Số điện thoại VN | `0987654321`, `+84912345678` | Regex SĐT VN |
| Email | `nguyen.van.a@company-internal.com` | Regex Email |
| CCCD | `079201001234` | Regex CCCD 12 số |
| Connection String | `Server=...;Password=...;` | Regex Connection String |
| Private Key | khối `-----BEGIN RSA PRIVATE KEY-----` | Regex PEM |

### Cách demo
1. Mở `Demo/confidential_case.html`.
2. `Ctrl+A` → `Ctrl+C`.
3. HapiCopi **quét ngay lúc copy** → hiện **toast cảnh báo** liệt kê các loại nhạy cảm phát hiện được.
4. Dán ra Notepad / ô chat để cho thấy giá trị đã bị **che** (vd `sk-p**********E4rT`, `4111 **** **** 1111`).

### Lưu ý trung thực (giới hạn bản hiện tại)
- Bộ quét chạy **100% offline** (không gửi dữ liệu đi đâu) — đây là điểm bán hàng mạnh cho thời AI.
- Bản v2.0 hiện tại quét trên **text đã định dạng**; với một số đường dán phức tạp (Docs/Word dựng lại từ HTML gốc) việc che **có thể chưa triệt để 100%**. Để demo DLP "ăn chắc", nên dán ra **Notepad hoặc ô chat web** (đường dán text thuần) thay vì Word.

---

## 5. Kịch bản trình bày gợi ý (~3–4 phút)

1. **30s — Vấn đề:** copy trang `isolation_forest.html` dán thô vào Docs → chỉ ra nó vỡ/bẩn.
2. **60s — Giải pháp format:** bật HapiCopi, dán lại → sạch, giữ cấu trúc, Docs tự áp style.
3. **60s — Bảo mật:** copy `confidential_case.html` → toast cảnh báo PII → dán ra Notepad thấy đã che.
4. **30s — Chốt giá trị:** "Một thao tác Ctrl+V quen thuộc, nhưng vừa chuẩn hoá định dạng vừa chặn rò rỉ dữ liệu — chạy offline, không đổi thói quen người dùng."

---

## 6. Cách mở nhanh các file demo

```powershell
start .\Demo\isolation_forest.html
start .\Demo\confidential_case.html
```
