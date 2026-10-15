# 03. Quy trình Nhận tiền Tự động 100% (Payment Automation)

> **MỤC TIÊU:**  
> Người dùng quét mã VietQR ngân hàng bất kỳ $\rightarrow$ **3 đến 5 giây sau tự động kích hoạt Bản quyền ngay trên app**, không cần duyệt tay, không cần gửi ảnh bill Zalo, bạn đang ngủ tiền vẫn tự chảy vào tài khoản ngân hàng cá nhân.

---

## 1. Sơ đồ Luồng Thanh toán Tự động (VietQR + SePay)

```
 [ Người dùng bấm "Nâng cấp" trên HapiCopi ]
                       │
                       ▼
 ┌───────────────────────────────────────────────────────────────┐
 │   HỘP THOẠI THANH TOÁN (Pop-up trong App)                    │
 │   • Hiển thị mã VietQR động chuẩn Napas                       │
 │   • Số tiền: 19.000đ (App trọn đời) hoặc 9.000đ (Cloud/tháng) │
 │   • Nội dung chuyển khoản tự động: HAPI9281                   │
 └─────────────────────┬─────────────────────────────────────────┘
                       │
                       │ Người dùng quét mã trên App Ngân hàng
                       ▼
 [ Ngân hàng chuyển tiền thành công ] (Vietcombank, MB, Tech, Momo...)
                       │
                       │ 1. Báo biến động số dư trong 1-2s
                       ▼
 [ Cổng SePay.vn / Casso.vn ] ──(2. Bắn Webhook HTTP POST)──┐
                                                            ▼
                                        ┌───────────────────────────────────────┐
                                        │  CLOUDFLARE WORKER                    │
                                        │  • Nhận webhook: Đơn HAPI9281         │
                                        │  • Sinh License Key cho máy đó        │
                                        │  • Lưu vào Cloudflare Database D1     │
                                        └───────────────────┬───────────────────┘
                                                            │
                                                            │ 3. Trả kết quả về App
                                                            ▼
 ┌───────────────────────────────────────────────────────────────┐
 │   HAPICOPI DESKTOP APP (Màn hình người dùng)                 │
 │   • Pop-up đổi sang màu xanh: "Thanh toán thành công! 🎉"      │
 │   • Tự động điền License Key và mở khóa Pro vĩnh viễn         │
 │   • Không cần gõ lại mã, có hiệu lực tức thì!                 │
 └───────────────────────────────────────────────────────────────┘
```

---

## 2. Các công cụ cần chuẩn bị (Cực kỳ đơn giản)

1. **Một tài khoản Ngân hàng cá nhân bất kỳ:**
   * Vietcombank, MBBank, Techcombank, TPBank, ACB... (đã có sẵn Internet Banking).
2. **Đăng ký tài khoản tại SePay (sepay.vn):**
   * SePay là nền tảng kết nối tài khoản ngân hàng với phần mềm phổ biến nhất Việt Nam.
   * Gói miễn phí hoặc gói cơ bản chỉ vài chục nghìn/tháng.
   * Khi tài khoản của bạn nhận được tiền có nội dung `HAPIxxxx`, SePay sẽ gửi 1 thông báo (Webhook) đến URL Cloudflare Worker của bạn.
3. **Chuẩn tạo mã VietQR động:**
   * Không cần cài đặt gì phức tạp, chỉ là một đường link ảnh sinh tự động theo chuẩn Napas:
     ```
     https://qr.sepay.vn/img?acc=SO_TAI_KHOAN&bank=NGAN_HANG&amount=19000&des=HAPI9281
     ```
   * App HapiCopi chỉ cần hiển thị ảnh từ đường link này là người dùng quét được ngay trên mọi ứng dụng ngân hàng và MoMo.

---

## 3. Quy trình Trải nghiệm của Người dùng (User Flow)

* **Bước 1 (Bấm Nâng cấp):**  
  Người dùng chạm trần 50 lượt/ngày hoặc bấm vào huy hiệu `[✦ Nâng cấp Pro]` trong Bảng vẽ hoặc Cài đặt.
* **Bước 2 (Chọn gói):**
  * [x] **Mở khóa App Offline Vĩnh viễn:** 19.000 VNĐ (Thanh toán 1 lần duy nhất).
  * [ ] **Gói Cloud AI Tốc độ cao:** 9.000 VNĐ / tháng.
* **Bước 3 (Quét QR):**  
  App hiện mã QR kèm dòng chữ: *"Mở app ngân hàng bất kỳ để quét mã (Nội dung và số tiền đã được điền sẵn tự động)"*.
* **Bước 4 (Kích hoạt tức thì):**  
  Sau khi chuyển tiền xong, app đang mở sẽ tự động nhận diện và hiện thông báo:
  > *"Chúc mừng! Bạn đã nâng cấp thành công HapiCopi Pro. Toàn bộ giới hạn đã được dỡ bỏ."*

---

## 4. Phương án cho Thị trường Quốc tế (Foreign Users)

Nếu sau này bạn chia sẻ app lên Reddit, Twitter/X, ProductHunt cho người dùng nước ngoài:
* Sử dụng **LemonSqueezy** hoặc **Gumroad**:
  * Người dùng quốc tế bấm "Buy License" $\rightarrow$ Mở trang web LemonSqueezy $\rightarrow$ Thanh toán qua Thẻ tín dụng (Visa, MasterCard), Apple Pay hoặc PayPal.
  * LemonSqueezy tự động sinh License Key và gửi thẳng vào Email của khách.
  * Tiền USD sẽ được LemonSqueezy gom lại và chuyển về tài khoản ngân hàng Việt Nam của bạn hàng tháng.
