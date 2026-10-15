# KẾ HOẠCH TINH GỌN & ĐÁNH BÓNG ỨNG DỤNG TRƯỚC KHI DEPLOY (POLISH BEFORE DEPLOY)

> **TƯ DUY KỸ THUẬT CỐT LÕI (NGUYÊN TẮC BẤT DI BẤT DỊCH):**  
> *"Đo lường, định lượng được, thấy được sự đánh đổi tốt nhất cho tổng thể, không phải tốt nhất ở cục bộ. Có số liệu, rõ ràng, cách làm dễ hiểu, make sense. Một chức năng dễ hiểu với người dùng thì họ nhìn vào là hiểu ngay, không cần giải thích dài dòng. Loại bỏ toàn bộ code rác, placeholder giả, đạt độ tinh khiết cao nhất để sẵn sàng deploy sản phẩm hoàn chỉnh."*

---

## I. TỔNG QUAN HIỆN TRẠNG & BÀI TOÁN ĐÁNH ĐỔI

Sau đợt refactor Math UI và kiểm thử các trụ cột lõi, ứng dụng đã vận hành ổn định về mặt logic AI và bộ nhớ. Tuy nhiên, trước khi đóng gói deploy, sản phẩm cần được polish triệt để nhằm đạt độ hoàn thiện cao nhất:

1. **Thanh Ribbon & Sidebar bị lặp nút 2 lần:**
   - Khi ở chế độ Fullscreen, các nút cơ bản (`a/b`, `√`, `∑`, `∫`, `lim`) xuất hiện đồng thời ở **Ribbon tabs trên cùng** lẫn **Panel "Mẫu công thức" ở Sidebar bên phải**, gây cảm giác thừa thãi, rối mắt và lãng phí không gian.
2. **Modal Hướng dẫn & Phím tắt rườm rà, icon lệch chuẩn:**
   - Dùng emoji màu 📖 không đồng bộ với hệ thống icon SVG nét rỗng (outline/stroke) của ứng dụng.
   - Tiêu đề phụ dài dòng vô nghĩa: *"Gõ phím chuẩn Microsoft Word Equation & MathType"*.
   - Bảng phím tắt bị lộ cú pháp LaTeX thô `(\(\times\))`, cách trình bày rối.
   - Phần giải thích dài dòng, lê thê; chân trang thừa chữ *"Offline & Riêng tư 100%"*.
3. **Các Panel phụ (Lịch sử Clipboard, Cài đặt, Cập nhật) cồng kềnh, thiếu responsive:**
   - `ClipboardHistoryForm`: Kích thước khổng lồ ($1000 \times 640\text{ px}$), chia cột nặng nề, tích hợp cả inspector format nhị phân phức tạp không cần thiết cho người dùng phổ thông.
   - `SettingsForm`: Tràn ngập các đoạn văn giải thích dài lê thê 4–5 dòng cho từng tùy chọn, form kéo dài phải cuộn mệt mỏi.
   - `UpdateForm`: Kích thước lớn ($720 \times 560\text{ px}$), phức tạp như giao diện quản trị server.
   - **Mục tiêu:** Thu nhỏ về kích thước gọn gàng tương đương Math Editor lúc thu nhỏ ($480 - 540\text{ px}$ chiều rộng, $380 - 460\text{ px}$ chiều cao), thiết kế dạng card nổi tinh tế, nhãn tự giải thích (self-explanatory), nhìn vào hiểu ngay.
4. **Thay thế Placeholder "Coming Soon" bằng Tính năng Tự động Tạo Template Mẫu riêng:**
   - Thẻ `Coming Soon: + Thêm mẫu riêng` ở Sidebar và nút `+ Thêm` ở Mini Quick Row hiện chưa có tính năng thật.
   - Cần hiện thực hóa hoàn chỉnh: Cho phép người dùng lưu công thức hiện tại thành mẫu cá nhân (Custom Equation Template), tự động tạo placeholder điền số, lưu trữ bền vững và chèn 1-click. Tuyệt đối không để lại bất kỳ tính năng "Coming Soon" dở dang nào khi deploy.

---

## II. ĐẶC TẢ CHI TIẾT & GIẢI PHÁP KỸ THUẬT

### 1. Giải quyết vấn đề lặp nút giữa Ribbon & Sidebar Mẫu công thức

#### 1.1. Bản chất sự lặp lại
- **Vị trí 1 (Ribbon Header):** Tab *"Cấu trúc"* chứa `Phân số`, `Căn thức`, `Số mũ`. Tab *"Toán tử & Ma trận"* chứa `∑`, `∫`, `lim`.
- **Vị trí 2 (Sidebar bên phải):** Khung *"MẪU CÔNG THỨC"* lại hiển thị 5 thẻ lớn: `a/b (Phân số)`, `√a+b (Căn thức)`, `∑ (Sigma)`, `∫ (Tích phân)`, `lim (Giới hạn)`.
- **Vị trí 3 (Quick Bar dưới đáy ở chế độ Mini):** Đã có hàng quick-insert `a/b`, `√`, `Σ`, `∫`, `xⁿ`.

#### 1.2. Giải pháp tái cấu trúc (Make Sense & Rõ ràng vai trò)
- **Ribbon tabs (trên cùng):** Giữ vai trò **"Bàn phím ký hiệu nguyên tử" (Atomic Symbol Palette)** — phục vụ chèn từng toán tử, ký tự Hy Lạp, ngoặc, ma trận đơn lẻ.
- **Sidebar bên phải (Fullscreen):** Chuyển đổi nhóm *"Mẫu công thức"* thành **"Công thức mẫu hoàn chỉnh" (Preset Equations)**:
  - Thay vì lặp lại các ký hiệu đơn lẻ $a/b, \sqrt{x}$, sidebar sẽ cung cấp các công thức toán thực tế mà người dùng hay cần chèn nhanh:
    1. **Phương trình bậc 2:** $x = \frac{-b \pm \sqrt{b^2-4ac}}{2a}$
    2. **Định lý Pythagoras:** $a^2 + b^2 = c^2$
    3. **Tích phân từng phần:** $\int u \, dv = uv - \int v \, du$
    4. **Chuỗi Taylor:** $\sum_{n=0}^{\infty} \frac{f^{(n)}(a)}{n!}(x-a)^n$
    5. **Hệ 2 phương trình:** $\begin{cases} a_1 x + b_1 y = c_1 \\ a_2 x + b_2 y = c_2 \end{cases}$
    6. **Phân phối Chuẩn (Gauss):** $f(x) = \frac{1}{\sigma \sqrt{2\pi}} e^{-\frac{(x-\mu)^2}{2\sigma^2}}$
  - **Lợi ích:** Người dùng bấm 1 phát là có khung hoàn chỉnh để điền số, đúng bản chất "MẪU CÔNG THỨC", triệt tiêu hoàn toàn sự trùng lặp với Ribbon.

---

### 2. Hiện thực hóa Tính năng Tạo Template Tự động (Auto Custom Template Generator)

#### 2.1. Mục tiêu
Xóa bỏ hoàn toàn badge `⏳ Coming Soon`. Biến nút `+ Thêm` (ở Mini) và `+ Tạo mẫu riêng` (ở Sidebar) thành tính năng thực tế 100%, cho phép người dùng biến bất kỳ phương trình đang soạn thành mẫu riêng tái sử dụng.

#### 2.2. Luồng hoạt động tự động & thông minh (Workflow)
1. **Kích hoạt:** Người dùng đang gõ một công thức bất kỳ trên canvas/editor $\rightarrow$ bấm nút `+ Thêm` (trên Quick Row) hoặc `+ Tạo mẫu riêng` (trên Sidebar).
2. **Kiểm tra trạng thái:**
   - Nếu khung soạn thảo trống: Hiển thị toast nhẹ: *"Hãy nhập công thức trước khi lưu làm mẫu."*
   - Nếu có công thức: Mở modal popup nhỏ tinh gọn **"Lưu công thức mẫu riêng"**.
3. **Modal Tạo Mẫu Gọn gàng (Micro Dialog):**
   - **Xem trước:** Render trực tiếp công thức LaTeX xem trước sắc nét qua MathJax/MathField.
   - **Tên mẫu:** Ô nhập ngắn gọn (tự động gợi ý tên mặc định như `Mẫu 1`, `Mẫu 2` hoặc lấy từ token đầu).
   - **Tự động tạo placeholder:** Một nút bấm thông minh `Tự động thay số thành ô trống (#?)` — tự động quét các hằng số hoặc biến trong công thức thành `#?` để người dùng sau này chỉ việc Tab điền số.
   - **Nút bấm:** `Hủy` và `Lưu mẫu`.
4. **Lưu trữ bền vững (Persistence):**
   - Lưu trữ đồng thời vào `localStorage` của WebView và gửi message `saveCustomTemplate` về C# backend để ghi vào `%APPDATA%\HapiCopi\custom_templates.json`.
   - Đảm bảo an toàn 100%, không mất dữ liệu khi tắt máy hay cập nhật phần mềm.
5. **Hiển thị & Sử dụng:**
   - Mẫu mới lập tức xuất hiện trong danh sách mẫu ở Sidebar và thanh Quick Bar.
   - Thẻ mẫu hiển thị tên + công thức thu nhỏ + nút xóa (icon thùng rác nhỏ) khi rê chuột.
   - Nhấp vào thẻ mẫu: Chèn ngay lập tức vào vị trí con trỏ của MathField!

---

### 3. Tinh gọn & Chuẩn hóa Modal "Hướng dẫn & Phím tắt"

#### 3.1. Chuẩn hóa Icon & Header
- **Icon tiêu đề:** Bỏ emoji `📖`, thay bằng SVG nét rỗng (outline/stroke $2\text{px}$), đồng bộ $100\%$ với các nút trên thanh toolbar:
  ```html
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/>
      <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/>
  </svg>
  ```
- **Tiêu đề:** `Hướng dẫn gõ phím & Phím tắt`
- **Phụ đề:** Bỏ hoàn toàn câu *"Gõ phím chuẩn Microsoft Word Equation & MathType"*, thay bằng phụ đề súc tích: `Mẹo soạn thảo nhanh với bàn phím và bảng vẽ` (hoặc bỏ hẳn để modal thoáng đãng).

#### 3.2. Tinh chỉnh Bảng Phím tắt Gõ nhanh
- Sửa lỗi cú pháp render: Loại bỏ các ký tự raw `(\(\times\))` bị lọt ra ngoài text.
- Bảng hiển thị 2 cột trực quan, ngắn gọn, súc tích:
  | Ký hiệu | Cách gõ (bấm phím Space sau khi gõ) | Ví dụ kết quả |
  |---|---|---|
  | Phân số | `/` hoặc `(a+b)/(c+d)` | $\frac{a+b}{c+d}$ |
  | Số mũ / Chỉ số | `^` (mũ) / `_` (chỉ số dưới) | $x^2$, $x_i$ |
  | Căn thức | `sqrt` (căn bậc 2), `root` (căn bậc n) | $\sqrt{x}$, $\sqrt[n]{x}$ |
  | Tích phân / Tổng | `int` (tích phân), `sum` (tổng $\Sigma$) | $\int$, $\sum$ |
  | Vector | `vec` + Space hoặc `Alt + V` | $\vec{v}$ |
  | Toán tử | `*` ($\cdot$), `times` ($\times$), `+-` ($\pm$) | $\cdot$, $\times$, $\pm$ |
  | Hy Lạp | `alpha`, `beta`, `pi`, `theta`, `oo` ($\infty$) | $\alpha$, $\beta$, $\pi$, $\theta$, $\infty$ |

#### 3.3. Rút gọn Phím điều khiển & Mẹo AI
- **Phím tắt điều khiển (3 dòng quan trọng nhất):**
  - `Ctrl + Alt` (hoặc `Ctrl + Alt + M`): Mở/ẩn nhanh cửa sổ soạn toán từ mọi nơi.
  - `Ctrl + Enter`: Chèn ngay công thức sắc nét vào Word / Docs.
  - `Ctrl + M`: Chuyển nhanh giữa chế độ Gõ trực quan và Gõ mã LaTeX.
- **Viết tay AI:**
  - Viết công thức tự nhiên bằng chuột/bút; máy tự nhận diện thời gian thực.
  - Nhấp vào gợi ý phía trên nếu muốn chọn ký tự thay thế.
- **Footer Modal:**
  - Đổi từ `HapiCopi v1.2.5 — Offline & Riêng tư 100%` $\rightarrow$ `HapiCopi v1.2.5`.

---

### 4. Tái thiết kế Tinh gọn & Responsive cho các Panel phụ

#### 4.1. Nguyên tắc thiết kế đồng bộ (Mini Popup Standard)
- **Kích thước:** Chiều rộng chuẩn $480 - 520\text{ px}$, chiều cao $400 - 460\text{ px}$ (tương đồng với kích thước Math Mini Popup).
- **Trải nghiệm:** Tự giải thích (self-explanatory), bỏ hết văn bản mô tả thừa. Người dùng nhìn nút bấm/toggle là hiểu ngay chức năng.

#### 4.2. Panel Lịch sử Clipboard (`ClipboardHistoryForm`)
- **Vấn đề cũ:** Kích thước $1000 \times 640\text{ px}$, có panel Inspector format phức tạp, 2 RichTextBox chiếm trọn màn hình.
- **Thiết kế mới tinh gọn:**
  - Kích thước: $480 \times 440\text{ px}$.
  - Giao diện 1 cột dạng danh sách thẻ (Card List) tương tự Windows Clipboard (`Win + V`) hiện đại:
    - **Header:** Tiêu đề `Lịch sử Clipboard`, ô tìm kiếm nhanh (Search Box), nút `Xóa sạch`.
    - **Body:** Danh sách các mục đã copy. Mỗi thẻ hiển thị:
      - Icon ứng dụng nguồn + Thời gian copy (ví dụ: *Chrome • 2 phút trước*).
      - Nội dung trích đoạn (2 dòng text rõ nét).
      - Badge bảo mật nhỏ (nếu đã che PII).
      - Nút hành động nhanh trên thẻ: `Dán lại` (hoặc `Copy`), `Xóa mục`.
    - **Thao tác:** Bấm 1 click hoặc Enter để sao chép lại ngay vào Clipboard và tự động ẩn form.
    - **Đánh đổi:** Ẩn hoặc loại bỏ tab xem Inspector nhị phân (chỉ giữ cho developer khi chạy debug flag `--inspector`), giải phóng hoàn toàn giao diện người dùng.

#### 4.3. Panel Cài đặt (`SettingsForm`)
- **Vấn đề cũ:** Chiều rộng 560px + scroll dài dằng dặc với các đoạn văn mô tả lê thê.
- **Thiết kế mới tinh gọn:**
  - Kích thước: $460 \times 460\text{ px}$.
  - Bỏ toàn bộ các đoạn văn giải thích 3–4 dòng. Thay bằng danh sách các mục toggle/checkbox ngắn gọn, tự giải thích:
    ```
    [✓] Quét bảo vệ dữ liệu nhạy cảm (PII)
        [✓] Cảnh báo Toast khi phát hiện PII
        [✓] Tự động che PII trong lịch sử
    ────────────────────────────────────────
    [✓] Tự động chuyển SVG sang PNG khi copy
        [✓] Giữ nền trong suốt
    ────────────────────────────────────────
    [✓] Khởi động cùng Windows
    [✓] Ghi nhớ lịch sử clipboard (tối đa 30 mục)
        [✓] Lưu mã hóa an toàn trên ổ đĩa
    ```
  - Khung "Từ khóa nhạy cảm tùy chỉnh": Thu gọn thành ô nhập nhỏ gọn có placeholder rõ ràng.
  - Footer: Nút `Đóng` và `Lưu cấu hình`.

#### 4.4. Panel Phiên bản & Cập nhật (`UpdateForm`)
- **Vấn đề cũ:** Kích thước $720 \times 560\text{ px}$, chứa hàng loạt nút `Rollback`, `Change Feed`, `Release List` nhiều cột rườm rà.
- **Thiết kế mới tinh gọn:**
  - Kích thước: $420 \times 320\text{ px}$ (dạng Card thông tin phiên bản tiêu chuẩn).
  - Bố cục trung tâm (Centered Card):
    - Icon ứng dụng HapiCopi lớn sắc nét với vòng trạng thái.
    - Tiêu đề: `HapiCopi v1.2.5`
    - Dòng trạng thái ngắn:
      - Nếu mới nhất: `✓ Ứng dụng đang ở phiên bản mới nhất.` (Màu xanh Teal dịu).
      - Nếu có bản mới: `★ Đã có phiên bản v1.2.6 (Khuyên dùng).` (Màu cam nổi bật kèm nút Cập nhật ngay).
    - Nút bấm chính: `Kiểm tra cập nhật` (Primary Button) và `Đóng`.
    - Tùy chọn nâng cao (Rollback/Đổi nguồn): Thu gọn vào 1 link nhỏ `Tùy chọn nâng cao...` chỉ hiện khi người dùng thực sự cần.

---

## III. BẢNG SO SÁNH TRƯỚC VÀ SAU KHI TINH GỌN (BENCHMARK UI/UX)

| Tiêu chí | Trước khi tinh gọn | Sau khi tinh gọn | Đánh giá cải thiện |
|---|---|---|---|
| **Lặp nút Ribbon & Sidebar** | Trùng lặp 5 nút cơ bản ở cả 2 nơi | Ribbon là nút đơn lẻ, Sidebar là công thức hoàn chỉnh | **Triệt tiêu 100% sự trùng lặp**, tăng giá trị sử dụng |
| **Tính năng Tạo Mẫu riêng** | Placeholder `⏳ Coming Soon` | Tự động tạo template từ công thức hiện tại | **100% chức năng thật**, không có tính năng dở dang |
| **Kích thước Clipboard Form** | $1000 \times 640\text{ px}$ (Quá khổ) | $480 \times 440\text{ px}$ (Dạng popup card) | **Giảm 67% diện tích chiếm dụng**, thao tác nhanh gấp 2 lần |
| **Kích thước Settings Form** | Dài cuộn trang, chữ mô tả dày đặc | $460 \times 460\text{ px}$, toggle tự giải thích | **Không cần cuộn**, nhìn 2 giây là nắm trọn cấu hình |
| **Kích thước Update Form** | $720 \times 560\text{ px}$ (Cồng kềnh) | $420 \times 320\text{ px}$ (Card hiện đại) | **Gọn nhẹ**, đúng chuẩn ứng dụng desktop chuyên nghiệp |
| **Tính đồng nhất Icon** | Lẫn lộn Emoji màu và SVG | 100% SVG nét rỗng (outline/stroke $2\text{px}$) | **Đạt thẩm mỹ cao cấp (Rich Aesthetics)** |
| **Độ dài văn bản hướng dẫn** | Dài dòng, lộ code LaTeX raw | Súc tích, bảng 2 cột sạch đẹp, footer gọn | **Dễ đọc, dễ hiểu ngay lập tức** |

---

## IV. LỘ TRÌNH TRIỂN KHAI (IMPLEMENTATION PHASES)

- [ ] **Phase 1: Tinh chỉnh Math Editor UI & Xóa lặp nút (`matheditor.html`)**
  - Đổi các nút lặp trong Sidebar thành 6 công thức mẫu hoàn chỉnh (Quadratic, Taylor, Integral, System...).
  - Thay icon modal hướng dẫn bằng SVG outline rỗng.
  - Bỏ phụ đề thừa, làm sạch bảng phím tắt (xóa raw `\times`, căn chỉnh 2 cột).
  - Tinh gọn các mẹo viết tay AI thành bullet point ngắn, đổi footer thành `HapiCopi v1.2.5`.
- [ ] **Phase 2: Xây dựng Chức năng Tạo Template Tự động (Auto Template Generator)**
  - Thay thế badge `Coming Soon` bằng logic tạo template trực tiếp.
  - Modal micro nhập tên mẫu & tự động sinh placeholder `#?`.
  - Lưu trữ bền vững (`localStorage` + `custom_templates.json` qua C# bridge).
  - Hỗ trợ chèn 1-click và xóa template cá nhân.
- [ ] **Phase 3: Thu gọn & Hiện đại hóa Clipboard History (`ClipboardHistoryForm.cs`)**
  - Thu nhỏ form về $480 \times 440\text{ px}$.
  - Chuyển sang layout card list 1 cột, tập trung vào trích đoạn text, thời gian, copy 1-click.
  - Ẩn phần Inspector nhị phân cồng kềnh.
- [ ] **Phase 4: Tinh giản Settings Form (`SettingsForm.cs`)**
  - Thu nhỏ kích thước về $460 \times 460\text{ px}$.
  - Loại bỏ các đoạn văn giải thích dài dòng, giữ lại các checkbox súc tích, tự giải thích.
- [ ] **Phase 5: Thu nhỏ Update Form (`UpdateForm.cs`)**
  - Thu nhỏ kích thước về $420 \times 320\text{ px}$.
  - Thiết kế card trạng thái phiên bản tinh gọn, tập trung nút Kiểm tra / Cập nhật.
- [ ] **Phase 6: Kiểm thử định lượng & Đóng gói deploy**
  - Chạy toàn bộ test suites (`--test-e2e-math`, `--test-ai-math`, `--test-distill`, `--test-topo`).
  - Chụp ảnh màn hình kiểm thử lưu vào `testing/shots/`.
  - Báo cáo kết quả và sẵn sàng release.
