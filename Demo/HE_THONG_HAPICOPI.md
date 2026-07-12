# HapiCopi — Mô tả hệ thống (cho AI đọc & trình bày)

> Tài liệu này mô tả đầy đủ sản phẩm HapiCopi để một AI có thể hiểu, tóm tắt và trình bày lại trong buổi pitch khởi nghiệp. Hãy bám sát phần "Khung thông điệp" khi nói: **bảo mật là lõi, định dạng là tiện ích phụ.**

## 1. Một câu định nghĩa
HapiCopi là công cụ chạy nền trên Windows, **chặn rò rỉ dữ liệu nhạy cảm ngay tại thao tác copy/paste**, hoạt động **100% offline** (không gửi dữ liệu lên cloud), không thay đổi thói quen Ctrl+C / Ctrl+V của người dùng.

## 2. Vấn đề
- Thời đại AI: người dùng liên tục dán source code, thông tin khách hàng, API key, hợp đồng vào ChatGPT/Gemini.
- **Một khi đã dán/gửi là không thể thu hồi.** Không có thao tác "select all" nào lấy lại được bí mật đã lộ.
- Tiền lệ thật: Samsung cấm ChatGPT 2023 sau khi kỹ sư làm rò mã nguồn; nhiều ngân hàng (JPMorgan), Apple cấm theo.
- → Phải can thiệp **trước khi dữ liệu rời máy**, đúng tại điểm rò phổ biến nhất là clipboard.

## 3. Cách hoạt động (luồng)
1. Người dùng nhấn **Ctrl+C**.
2. HapiCopi bắt sự kiện clipboard, lấy nội dung text/HTML.
3. **Bộ quét PII chạy cục bộ** (dưới 1ms) tìm thông tin nhạy cảm.
4. Nếu phát hiện: hiện **toast cảnh báo** + **ghi đè clipboard bằng bản đã che** (masked).
5. Khi người dùng **Ctrl+V**, mọi đích đến (Notepad/Word/Google Docs/LaTeX) đều nhận **bản đã che**, không phải bản gốc.

## 4. Công nghệ phát hiện (3 engine, đều offline)
- **Engine 1 — Regex mẫu cố định:** API key (OpenAI `sk-`, AWS `AKIA…`, GitHub `ghp_…`, Google, Stripe, Slack, Twilio…), số điện thoại VN, email, CCCD 12 số, connection string, private key (PEM block), Bearer token.
- **Engine 2 — Shannon Entropy:** bắt các chuỗi ngẫu nhiên (secret lạ) không khớp mẫu nào, có kiểm tra ngữ cảnh (gần từ khóa như "key", "password", "token") để tránh báo động giả trên mã nguồn.
- **Engine 3 — Thuật toán Luhn:** xác thực số thẻ tín dụng 16 số trước khi báo, loại số ngẫu nhiên không hợp lệ.
- Có **danh sách ngoại lệ** (exclusion) và **từ khóa tùy chỉnh** do người dùng/doanh nghiệp tự cấu hình.

## 5. Tính năng phụ (tiện ích "and also", KHÔNG phải lõi)
- Dán vào Word/Google Docs: làm sạch nền & font từ web, giữ cấu trúc (heading/bảng/list), để tài liệu đích tự áp style.
- Code **Mermaid** → render thành ảnh sơ đồ.
- Bảng → mã **LaTeX** cho Overleaf.
- Text copy từ PDF bị đứt dòng → nối liền câu.
- Lịch sử clipboard, mã hóa cục bộ.

## 6. Khác biệt cạnh tranh
| Tiêu chí | DLP doanh nghiệp (Purview, Zscaler…) | HapiCopi |
|---|---|---|
| Triển khai | Nặng, cần đội IT, đắt | Cài 1 phút, chạy nền |
| Dữ liệu khi quét | Thường qua cloud | 100% trên máy |
| Thói quen người dùng | Phải học quy trình | Không đổi gì |
| Đối tượng | Tập đoàn lớn | Dev team, SMB, cá nhân |

## 7. Thị trường & điểm vào (beachhead)
- **Đội dev/kỹ thuật**: copy code & key vào AI mỗi ngày, nhạy cảm với rò secret.
- **SMB ngành quy định** (kế toán, phòng khám, luật): cần tuân thủ nhưng không kham nổi DLP doanh nghiệp.
- **Tổ chức privacy-first**: không chấp nhận công cụ bảo mật lại gửi data lên cloud.

## 8. Lộ trình
1. **Nay:** bảo vệ clipboard, offline.
2. **Kế:** browser extension — quét nội dung submit thẳng lên trang AI (phủ cả gõ tay & upload file).
3. **Sau:** console quản lý tập trung + nhật ký audit cho team/doanh nghiệp.

## 9. Ranh giới thành thật (để trả lời phản biện)
- HapiCopi **không** thay thế DLP doanh nghiệp, **không** phủ mọi vector (gõ tay, upload, screenshot — đó là lộ trình).
- Tối ưu cho **các mẫu bí mật phổ biến**, không nhằm quét sạch một file xuất dữ liệu hàng loạt.
- Giá trị: chặn **vector phổ biến và bất cẩn nhất** (copy-paste vào AI) một cách nhẹ, offline, 0 thao tác.

## 10. Khung thông điệp khi trình bày
- Câu mở: "Thời AI ai cũng copy-paste vào chatbot. Đã dán là không lấy lại được."
- Câu lõi: "Bạn không thể ‘select all’ để thu hồi một bí mật đã lộ. HapiCopi chặn ngay tại Ctrl+C, offline."
- Câu chốt: "Chúng tôi không thay DLP doanh nghiệp — chúng tôi chặn vector phổ biến nhất của thời đại AI."
- Lưu ý: **không** quảng cáo tính năng dọn định dạng như giá trị chính (chỉnh tay làm tốt hơn); chỉ nhắc như tiện ích giữ chân người dùng.
