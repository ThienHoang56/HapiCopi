# 01. Mô hình Kinh doanh & Định giá (Business Model & Unit Economics)

> **TƯ DUY CỐT LÕI:** Đo lường định lượng chi phí thực tế từng token, tính toán biên lợi nhuận, thấy rõ sự đánh đổi để đưa ra mức giá "không thể cưỡng lại" cho học sinh, sinh viên và giáo viên Việt Nam.

---

## 1. Định lượng Chi phí Biên Thực tế (Unit Economics of Cloud AI)

Nhiều người nghĩ gọi AI đám mây (Google Gemini, OpenAI) là rất tốn kém. Hãy tính toán chính xác bằng số liệu thực tế đo đạc từ API của Google:

### 1.1. Chi phí token của 1 lượt nhận diện nét vẽ Toán học
* **Mô hình sử dụng:** `gemini-2.5-flash` (Google Generative AI).
* **Định mức dữ liệu cho 1 request:**
  * Ảnh nét vẽ PNG trắng đen (kích thước tối ưu $\approx 360 \times 200$ px): Gemini mã hóa cố định $\approx \mathbf{258 \text{ tokens}}$.
  * System Prompt định dạng LaTeX: $\approx \mathbf{80 \text{ tokens}}$.
  * Output LaTeX trả về (ví dụ: `\int_{0}^{1} x^2 dx = \frac{1}{3}`): $\approx \mathbf{20 - 40 \text{ tokens}}$.
  * **Tổng cộng 1 lượt nhận diện:** $\approx \mathbf{360 \text{ tokens}}$ (trong đó ~338 tokens Input, ~25 tokens Output).

### 1.2. Giá cước của Google Gemini 2.5 Flash (Bản có phí Pay-as-you-go)
* Giá Input: **0.075 USD / 1,000,000 tokens** ($\approx 1,900 \text{ VNĐ} / 1 \text{ triệu tokens}$).
* Giá Output: **0.300 USD / 1,000,000 tokens** ($\approx 7,600 \text{ VNĐ} / 1 \text{ triệu tokens}$).

$$\text{Chi phí 1 lượt nhận diện} = \left(\frac{338}{1,000,000} \times 1,900\right) + \left(\frac{25}{1,000,000} \times 7,600\right) \approx 0.64 + 0.19 \approx \mathbf{0.83 \text{ VNĐ / lượt!}}$$

> 💡 **KẾT LUẬN ĐỊNH LƯỢNG:**  
> **1.000 lượt nhận diện công thức qua Google Gemini 2.5 Flash chỉ tốn của bạn chưa tới 900 ĐỒNG!**  
> Một người dùng học tập/giảng dạy bình thường dùng nhiều lắm cũng chỉ **300 – 500 lượt/tháng**, chi phí API bạn phải trả cho Google trên đầu người dùng đó chỉ vỏn vẹn **~400 VNĐ/tháng**.

---

## 2. Chiến lược 2 Tầng Doanh thu (Two-Tier Revenue Model)

Dựa trên con số chi phí siêu rẻ ở trên, ta xây dựng mô hình định giá "mềm như bún" nhưng biên lợi nhuận (Gross Margin) **> 90%**:

```
                                  HAPICOPI PRICING LADDER
                                  
  ┌──────────────────────────────────────────────────────────────────────────────────┐
  │ 1. BẢN MIỄN PHÍ (FREE FOREVER)                                                  │
  │ • Giá: 0 VNĐ                                                                     │
  │ • Clipboard, DLP, Word Office Formatter: Vô hạn                                  │
  │ • AI Math OCR Cục bộ (Offline): Hào phóng 50 lượt / ngày                         │
  │ • Watermark LaTeX: Có comment "% Tạo bởi HapiCopi"                               │
  │ • Quảng cáo: 1 dòng Sponsor Tip tinh tế dưới bảng vẽ                             │
  └────────────────────────────────────────┬─────────────────────────────────────────┘
                                           │
                                           ▼ (Khách cần dùng nhiều & sạch sẽ)
  ┌──────────────────────────────────────────────────────────────────────────────────┐
  │ 2. PHÍ MỞ KHÓA APP CỤC BỘ (BASE APP LICENSE - ONE-TIME)                         │
  │ • Giá: 49.000 VNĐ – 79.000 VNĐ (Mua 1 lần dùng vĩnh viễn)                       │
  │ • Mở khóa: Nhận diện Toán Cục bộ Offline VÔ HẠN (không lo hết 50 lượt)           │
  │ • Tắt sạch 100% dòng Sponsor Tip, xóa bỏ Watermark LaTeX                         │
  │ • 100% Offline, cam kết bảo mật riêng tư tuyệt đối (Zero data leave machine)     │
  └────────────────────────────────────────┬─────────────────────────────────────────┘
                                           │
                                           ▼ (Khách lười kiếm API key, muốn AI Cloud xịn)
  ┌──────────────────────────────────────────────────────────────────────────────────┐
  │ 3. GÓI DỊCH VỤ CLOUD AI (CLOUD PRO SUBSCRIPTION / POOL KEY)                      │
  │ • Giá: 19.000 VNĐ / tháng  (hoặc 99.000 VNĐ / 6 tháng, 169.000 VNĐ / năm)        │
  │ • Sử dụng AI Cloud trung tâm của HapiCopi (Gemini 2.5 Flash siêu tốc)            │
  │ • Nhận diện được cả các phương trình ma trận, tích phân nhiều tầng cực phức tạp   │
  │ • Tự động chắt lọc tri thức (Knowledge Distillation) dạy lại cho máy cá nhân    │
  │ • Biên lợi nhuận: Khách trả 19.000đ/tháng, chi phí API thực tế ~500đ -> LÃI 97%! │
  └──────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. So sánh với các đối thủ trên thị trường

| Tiêu chí | **Mathpix** | **MyScript Web** | **HapiCopi** |
| :--- | :--- | :--- | :--- |
| **Giá thuê bao** | $4.99 / tháng (~130.000đ) | $0.002 / stroke call | **19.000đ / tháng** (hoặc 49k trọn đời offline) |
| **Hoạt động Offline** | Không (100% cần mạng) | Không | **Có (ONNX Offline < 0.5ms)** |
| **Bảo mật Clipboard / DLP** | Không có | Không có | **Có (Quét API key, CCCD, Thẻ ngân hàng)** |
| **Format sang Word Office** | Cần xuất file qua lại | Không hỗ trợ Word gốc | **Tự động Ctrl+V ăn ngay vào Word** |
| **Độ thân thiện túi tiền VN** | Rất đắt với học sinh/SV | Chỉ bán cho Developer B2B | **Rẻ hơn 6 lần so với Mathpix** |

---

## 4. Mục tiêu Doanh thu Dự kiến (Unit Projection)

Giả sử sau 6 tháng tiếp cận các trường Đại học (FPT, Bách Khoa, KHTN...) và cộng đồng giáo viên:
* **Tổng tệp người dùng Free:** 5,000 người dùng thường xuyên.
* **Tỷ lệ chuyển đổi mua Base App (49.000đ):** 4% $\approx$ 200 người $\rightarrow$ **9,800,000 VNĐ** (doanh thu mua đứt ban đầu).
* **Tỷ lệ đăng ký Cloud AI (19.000đ/tháng):** 3% $\approx$ 150 người $\rightarrow$ **2,850,000 VNĐ / tháng** (Dòng tiền định kỳ thụ động - Recurring Revenue).
* **Chi phí API Google phải trả cho 150 người đó:** $\approx 150 \times 500\text{đ} = \mathbf{75,000 \text{ VNĐ / tháng}}$.
* **Tiền hoa hồng Affiliate từ dòng Sponsor Tip:** $\approx 1,000,000 \text{ VNĐ / tháng}$.
* 👉 **Lợi nhuận ròng (Net Profit):** $\approx \mathbf{3,700,000 \text{ VNĐ / tháng}}$ đều đặn mà chi phí máy chủ server chỉ tốn ~0đ (dùng hạ tầng Cloudflare miễn phí).
