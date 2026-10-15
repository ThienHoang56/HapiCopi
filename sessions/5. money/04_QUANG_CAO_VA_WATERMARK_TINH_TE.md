# 04. Thiết kế Quảng cáo & Watermark Tinh tế (Non-Intrusive UX)

> **TRIẾT LÝ THIẾT KẾ:**  
> Quảng cáo và Watermark phải **hữu ích, thanh lịch và tôn trọng người dùng**. Tuyệt đối không làm hỏng bài tập của sinh viên, không làm xấu tài liệu của giáo viên và không làm chậm ứng dụng.

---

## 1. So sánh: App rác vs App Cao cấp

| Tiêu chí | App Rác (Adware / Spam) | HapiCopi (Cao cấp & Tinh tế) |
| :--- | :--- | :--- |
| **Hình thức** | Banner nhấp nháy, pop-up nhảy bổ vào mặt | **Dòng text 1 hàng duy nhất (Native Sponsor Tip)** |
| **Nội dung** | Game cờ bạc, quảng cáo linh tinh | **Đúng nhu cầu: Bàn vẽ cảm ứng, bút stylus, sách toán** |
| **Watermark** | Đóng dấu to tướng đè lên công thức | **Comment vô hại trong code hoặc số lượt mờ góc canvas** |
| **Ảnh hưởng Word** | Chèn logo làm xấu bài nộp của học sinh | **Văn bản Word và công thức hiển thị đẹp hoàn hảo 100%** |
| **Độ trễ & Mã độc** | Tải script quảng cáo nặng, lag máy, Defender chặn | **0% virus, chỉ là 1 dòng text thuần, tải < 0.05 giây** |

---

## 2. Chi tiết Thiết kế Dòng Tài trợ (Native Sponsor Tip)

### 2.1. Vị trí & Quy cách hiển thị
* **Vị trí:** Nằm ngay dưới khung hiển thị công thức LaTeX trong Bảng vẽ Toán (`matheditor.html`).
* **Kích thước:** Chiều cao cố định đúng **24 pixel** (chỉ 1 dòng chữ nhỏ, không chiếm diện tích vẽ).
* **Màu sắc:** 
  * Chữ xám nhạt thanh lịch (`color: #64748b`), font chữ 12px Inter / Segoe UI.
  * Khi rê chuột vào (hover): Chữ đổi sang màu xanh thương hiệu (`#0d9488`), hiện gạch chân nhẹ.
* **Cú pháp hiển thị:**
  ```html
  <div class="sponsor-bar">
      <span class="sponsor-badge">💡 Gợi ý:</span>
      <a href="https://shope.ee/..." target="_blank" class="sponsor-link">
          Bàn vẽ điện tử Wacom One chính hãng giảm 15% cho sinh viên [Xem ưu đãi ↗]
      </a>
  </div>
  ```

### 2.2. Trải nghiệm người dùng:
* Khi click vào link: Trình duyệt mặc định của Windows tự mở trang Shopee Mall của sản phẩm. Bảng vẽ HapiCopi vẫn giữ nguyên nét vẽ, không bị tắt hay gián đoạn.
* **Nếu người dùng Offline (không có mạng):** Tự động ẩn hoàn toàn dòng này, không để lại khoảng trống thừa.
* **Nếu đã kích hoạt Pro:** Tự động ẩn vĩnh viễn dòng này.

---

## 3. Chi tiết Thiết kế 3 Tầng Watermark

### 3.1. Tầng 1: "Viral Watermark" trong mã LaTeX (Lan truyền thầm lặng)
* **Cơ chế:** Khi người dùng ở bản Free bấm nút **"Sao chép LaTeX"** hoặc **"Chèn vào Office"**:
  App sẽ tự động thêm 1 dòng comment LaTeX ở đầu mã nguồn:
  ```latex
  % Được tạo bởi HapiCopi — Bộ công cụ toán học cá nhân hóa (hapicopi.com)
  \lim_{x \to 0} \frac{\sin(x)}{x} = 1
  ```
* **Tại sao cách này vừa tinh tế vừa siêu hiệu quả?**
  * Trong cú pháp LaTeX, ký tự `%` là **dòng chú thích (comment)**. 
  * Cả Microsoft Word, Overleaf, MathJax, KaTeX đều **bỏ qua dòng này khi hiển thị công thức**.
  * Công thức toán khi in ra giấy hay nộp bài vẫn đẹp 100%, không bị thầy cô trừ điểm.
  * Nhưng khi sinh viên gửi file mã nguồn cho bạn bè cùng nhóm, bạn bè mở ra sẽ thấy thương hiệu `hapicopi.com` $\rightarrow$ Tự động tải về dùng thử!
* **Bản Pro:** Xuất mã nguyên bản 100%, không có dòng comment này.

### 3.2. Tầng 2: Watermark Hạn mức trên Canvas vẽ (UI Watermark)
* **Vị trí:** Góc dưới cùng bên phải của Canvas vẽ (chỗ người dùng hầu như không bao giờ vẽ tới).
* **Quy cách:**
  * Font chữ màu xám rất mờ (`rgba(148, 163, 184, 0.5)`), kích thước 11px:
    `HapiCopi Free • 42/50 lượt hôm nay`
  * Dòng chữ này **chỉ hiển thị trên giao diện vẽ**, khi xuất ra ảnh hay chèn vào Word thì **tuyệt đối không bị dính vào**.
* **Bản Pro:** Chuyển thành huy hiệu thanh lịch màu vàng nhạt:
  `✦ HapiCopi Pro (Không giới hạn)`

### 3.3. Tầng 3: Không đóng dấu đè lên ảnh công thức (Zero Visual Pollution)
* Nhiều phần mềm đóng dấu logo chìm to tướng vào giữa ảnh công thức khiến tài liệu Word của người dùng bị xấu xí.
* **HapiCopi tuyệt đối KHÔNG làm điều này!** Ảnh công thức dán vào Word luôn sắc nét và sạch sẽ 100% kể cả ở bản Free. Ta chỉ giới hạn số lượt (50 lượt/ngày) chứ không làm hỏng tài liệu của người dùng.
