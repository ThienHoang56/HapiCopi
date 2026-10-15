# 05. Lộ trình Triển khai Chi tiết (Action Roadmap)

> **NGUYÊN TẮC THỰC THI:**  
> Làm chắc chắn từng phần, đo lường được hiệu quả, kiểm thử không phát sinh lỗi hay rò rỉ RAM trước khi bước sang giai đoạn tiếp theo.

---

## 1. Giai đoạn 1: Xây dựng Bộ máy Bản quyền & Hạn mức Cục bộ (Client Engine)
*Mục tiêu: Đưa cơ chế 50 lượt/ngày, Watermark và Dòng tài trợ tinh tế vào app ngay trên máy người dùng mà chưa cần dựng server.*

* [ ] **1.1. Module `Core/LicenseService.cs`:**
  * Quản lý trạng thái: `IsProLicense` (Free vs Pro).
  * Bộ đếm hạn mức: Tối đa 50 lượt nhận diện Toán/ngày cho bản Free, tự reset lúc `00:00` hàng ngày.
  * Mã hóa file lưu trữ hạn mức bằng Windows DPAPI (`%APPDATA%\HapiCopi\license.dat`) chống chỉnh sửa gian lận.
  * Thuật toán kiểm tra License Key Offline: `HAPI-PRO-XXXXX-XXXXX-XXXXX`.
* [ ] **1.2. Module `Core/SponsorService.cs`:**
  * Tải và cache file `sponsor.json` (dòng gợi ý Shopee Affiliate bàn vẽ Wacom).
  * Fallback thông minh: Tự ẩn nếu mất mạng, tự ẩn vĩnh viễn nếu là bản Pro.
* [ ] **1.3. Nâng cấp Giao diện Bảng vẽ (`matheditor.html` & `MathEditorForm.cs`):**
  * Thêm huy hiệu hạn mức mờ ở góc canvas (`42/50 lượt`).
  * Thêm thanh Native Sponsor Tip (cao 24px) dưới thanh LaTeX.
  * Modal thông báo văn minh khi hết 50 lượt (mời nâng cấp hoặc chờ ngày mai).
  * Chèn comment `% Tạo bởi HapiCopi` khi copy LaTeX ở bản Free.
* [ ] **1.4. Nâng cấp Cửa sổ Cài đặt (`SettingsForm.cs`):**
  * Thêm mục "Bản quyền ứng dụng" cho phép người dùng xem trạng thái và dán License Key để kích hoạt.

---

## 2. Giai đoạn 2: Cổng Thanh toán VietQR Tự động (Payment Automation)
*Mục tiêu: Người dùng quét mã QR trên điện thoại là app tự động kích hoạt Pro sau 3 giây.*

* [ ] **2.1. Đăng ký và Cấu hình SePay (sepay.vn):**
  * Kết nối tài khoản ngân hàng cá nhân với SePay.
  * Lấy URL tạo mã VietQR động chuẩn Napas.
* [ ] **2.2. Xây dựng Form Thanh toán trong HapiCopi (`UpgradeForm.cs`):**
  * Hiển thị mã VietQR động chứa sẵn số tiền (49.000đ) và mã đơn hàng (ví dụ: `HAPI8821`).
  * Cơ chế Long-polling / Webhook check: Tự động kiểm tra trạng thái đơn hàng mỗi 2 giây.
  * Khi phát hiện tài khoản đã nhận tiền: Tự động điền key và nâng cấp Pro ngay lập tức.

---

## 3. Giai đoạn 3: Hệ thống Cloud AI Subscription (Shared Key Pool)
*Mục tiêu: Bán gói AI Cloud 19.000đ/tháng dùng chung API Key an toàn mà không lộ key.*

* [ ] **3.1. Dựng Cloudflare Worker Proxy (Miễn phí 100k req/ngày):**
  * Cấu hình secret `GEMINI_API_KEY` trong Cloudflare Environment.
  * Tạo bảng người dùng trên Cloudflare D1 Database.
  * Xác thực Token và Rate Limit chống spam.
* [ ] **3.2. Đấu nối Client với Cloud Proxy:**
  * Trong Bảng vẽ Toán: Khi người dùng bật chế độ "Đám mây (Cloud)", app tự động gửi qua Cloudflare Worker nếu có gói Cloud.
  * Knowledge Distillation: Tự động lưu nét vẽ đúng từ Cloud về hồ sơ cục bộ để học máy.
* [ ] **3.3. Thu thập dữ liệu ẩn danh (Data Flywheel):**
  * Cho phép người dùng Free đóng góp nét vẽ ẩn danh để nâng cấp mô hình AI toán học Việt Nam.
  * Bản Pro tự động ngắt toàn bộ kết nối gửi dữ liệu ra ngoài.
