# KẾ HOẠCH PHÁT TRIỂN MODULE SOẠN NHẠC KHOA HỌC (HAPICOPI MUSIC ENGINE)

> **Mã tài liệu:** `DOC-HAPICOPI-MUSIC-01`  
> **Trạng thái:** Dự thảo kế hoạch & Thiết kế chi tiết  
> **Áp dụng cho:** HapiCopi Desktop Application (WinForms + WebView2)  
> **Ngày lập:** 25/09/2026  

---

## 1. TỔNG QUAN, TẦM NHÌN & TƯ TƯỞNG CỐT LÕI

### 1.1 Vấn đề hiện tại
- **Thô sơ & cô lập:** Bộ soạn nhạc hiện tại (`abceditor.html`) chỉ là một khung soạn thảo văn bản thô kèm vài nút chèn ký tự cơ bản, hoàn toàn phụ thuộc vào việc người dùng phải nhớ cú pháp ABC notation.
- **Độ tin cậy thấp:** Trình phát âm thanh dễ bị treo do chính sách Web Audio Context của Chromium; cơ chế xuất ảnh sang Word qua Canvas thường xuyên lỗi font tiếng Việt và méo chữ; báo lỗi cú pháp mờ nhạt không chỉ rõ nguyên nhân.
- **Đứt gãy với hệ sinh thái HapiCopi:** Không có khả năng tự động bắt mã bản nhạc từ Clipboard (khi copy từ ChatGPT/Claude) để render thẳng vào Word/Docs như các module Toán học (LaTeX) và Sơ đồ (Mermaid).

### 1.2 Tầm nhìn cốt lõi: Giải mã rào cản âm nhạc cho người dùng phổ thông
> **Câu hỏi bản chất:** Vì sao một người bình thường không có chuyên môn lại rất khó tạo ra bản nhạc như ý?  
> **Nguyên nhân:** Vì **rào cản quá cao**. Người dùng phổ thông cảm nhận âm nhạc bằng **cảm xúc** (vui tươi, buồn man mác, hùng hồn, lãng mạn, thư giãn...), nhưng âm nhạc hàn lâm lại giao tiếp bằng **cú pháp kỹ thuật** (cao độ, trường độ, cung, quãng, hợp âm trưởng/thứ, hóa biểu).  
> **Bản chất của Hợp âm:** Hợp âm chỉ là **triển khai kỹ thuật (implementation)**, mục đích của người sáng tác **không phải là bản thân hợp âm**, mà là **cảm giác/trạng thái tinh thần mà hợp âm đó đem lại**.

Từ nhận thức này, HapiCopi Music Engine đặt ra mục tiêu ở tầng cao nhất: **Biến đổi Cảm xúc & Ý niệm thành Âm nhạc (Emotion-to-Music Framework)**, với sự tham gia của AI và sự tinh chỉnh dễ dàng của người dùng:

```
┌────────────────────────────────────────────────────────────────────────┐
│ TẦNG 3: Ý NIỆM & CẢM XÚC (INTENT & EMOTION) - Người dùng & AI          │
│ Vui tươi, Buồn hoài niệm, Hùng tráng, Lofi thư giãn, Kịch tính         │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ (AI / Template Framework chuyển đổi)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ TẦNG 2: BẢN CHẤT NHẠC LÝ TRỰC QUAN (SEMANTIC MUSIC FRAMEWORK)          │
│ Điệu thức (Major/Minor), Tiến trình hợp âm cảm xúc, Nhịp điệu, Tiết tấu│
│ ──> Người dùng tinh chỉnh trực quan bằng Bàn phím Piano ảo & Palette  │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ (HapiCopi Engine sinh mã ngầm)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ TẦNG 1: ĐỘNG CƠ KỸ THUẬT & XUẤT BẢN (CORE ENGINE & PUBLISHING)         │
│ ABC Notation, Web Audio Synth chuẩn, SVG 300 DPI nhúng font cho Word   │
└────────────────────────────────────────────────────────────────────────┘
```

### 1.3 Triết lý thiết kế: "Khoa học từ bản chất – Dễ dàng trong thao tác"
Âm nhạc được mô hình hóa từ **4 thuộc tính vật lý của âm thanh** sang các thao tác trực quan:
```
[BẢN CHẤT VẬT LÝ]          [NGỮ PHÁP NHẠC LÝ]             [TRẢI NGHIỆM TRÊN HAPICOPI]
1. Tần số (Frequency)   ──> Cao độ & Điệu tính       ──> Bàn phím Piano ảo + Selector Điệu tính (Key)
2. Thời gian (Duration) ──> Trường độ & Số chỉ nhịp ──> Bộ chọn hình nốt (𝅝, 𝅗𝅥, 𝅘𝅥, 𝅘𝅥𝅮) + Phân số nhịp
3. Biên độ (Amplitude)  ──> Cường độ & Sắc thái      ──> Ký hiệu sắc thái (p, mf, f) + Vạch luyến/ngắt
4. Dạng sóng (Waveform) ──> Âm sắc & Hòa âm          ──> Tiếng Piano đa âm + Trợ lý hợp âm cảm xúc
```

---

## 2. HỆ THỐNG MỤC TIÊU & CHỈ TIÊU SỐ RÕ RÀNG (KPIs)

Tất cả các tính năng được phát triển phải đáp ứng hệ thống chỉ tiêu số định lượng sau:

### 2.1 Chỉ tiêu về Hiệu năng (Performance KPIs)
| STT | Chỉ số đo lường | Định nghĩa / Cách đo | Mục tiêu cam kết |
|:---:|:---|:---|:---:|
| P1 | **Độ trễ phản hồi gõ nốt** | Thời gian từ lúc nhấn phím đàn piano/gõ phím đến khi nốt hiện trên khuông nhạc | **≤ 40 ms** (60 FPS) |
| P2 | **Độ trễ phát âm thanh (Audio Latency)** | Thời gian từ lúc click phím đàn đến khi loa phát tiếng nốt nhạc | **≤ 25 ms** |
| P3 | **Thời gian render lại bản nhạc** | Render lại toàn bộ khuông nhạc khi sửa văn bản ABC (bản nhạc 32 ô nhịp) | **≤ 80 ms** |
| P4 | **Thời gian khởi tạo cửa sổ** | Mở cửa sổ soạn nhạc từ Menu Tray | **≤ 350 ms** |
| P5 | **Thời gian xuất ảnh 300 DPI** | Xử lý SVG -> Canvas -> PNG đưa vào Clipboard | **≤ 150 ms** |

### 2.2 Chỉ tiêu về Độ tin cậy & Chất lượng (Reliability KPIs)
| STT | Chỉ số đo lường | Định nghĩa / Cách đo | Mục tiêu cam kết |
|:---:|:---|:---|:---:|
| R1 | **Tỷ lệ treo/mất tiếng Audio** | Tần suất Web Audio Context bị khóa hoặc câm tiếng khi tương tác | **0%** (Tự động kích hoạt resume) |
| R2 | **Tỷ lệ bảo toàn Font chữ tiếng Việt** | Tiêu đề, tên tác giả, lời bài hát tiếng Việt dán vào Word không bị lỗi font | **100%** |
| R3 | **Độ sắc nét khi in ấn/phóng to** | Độ phân giải xuất ảnh mặc định vào Clipboard | **≥ 300 DPI** (Retina/Print ready) |
| R4 | **Độ chính xác nhận diện mã ABC** | Tỷ lệ nhận diện đúng đoạn nhạc ABC trong Clipboard từ ChatGPT/Claude/Web | **≥ 99%** |
| R5 | **Tỷ lệ báo lỗi cú pháp chính xác** | Phát hiện sai nhịp, thiếu vạch nhịp, sai ký âm và chỉ rõ vị trí dòng/cột | **100%** các lỗi cơ bản |

### 2.3 Chỉ tiêu về Độ bao phủ Nhạc lý (Music Theory Coverage)
| STT | Khía cạnh nhạc lý | Phạm vi hỗ trợ |
|:---:|:---|:---|
| M1 | **Điệu tính (Key Signatures)** | Đủ 15 điệu tính chuẩn (từ 7 dấu giáng $\flat$ đến 7 dấu thăng $\sharp$, kèm Giọng Trưởng/Thứ tương ứng). |
| M2 | **Khóa nhạc (Clefs)** | Khóa Sol (Treble), Khóa Fa (Bass), Khóa Đô (Alto/Tenor), Khóa gõ (Percussion). |
| M3 | **Trường độ & Nốt lặng** | 7 cấp trường độ: Tròn, Trắng, Đen, Móc đơn, Móc kép, Móc ba, Móc tư; nốt chấm dôi, liên ba (triplets). |
| M4 | **Bàn phím Piano ảo** | Tối thiểu 31 phím (từ $F_3$ đến $C_6$) kèm hiển thị tên nốt chuẩn quốc tế và cao độ. |
| M5 | **Hợp âm cơ bản** | Đầy đủ các họ hợp âm 3 (Major, Minor, Diminished, Augmented) và hợp âm 7 ($7$, $maj7$, $m7$, $m7b5$). |

---

## 3. KẾ HOẠCH TRIỂN KHAI THEO 3 GIAI ĐOẠN

```
┌────────────────────────────────────────────────────────────────────────┐
│ GIAI ĐOẠN 1: NỀN TẢNG KỸ THUẬT & ĐỘ TIN CẬY LÕI (CORE RELIABILITY)     │
│ Sửa triệt để Audio, Playhead Cursor, Xuất ảnh 300 DPI nhúng font UTF-8 │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ GIAI ĐOẠN 2: TRẢI NGHIỆM SOẠN NHẠC KHOA HỌC (VISUAL MUSIC COMPOSER)   │
│ Đàn Piano ảo, Thanh công cụ nhạc lý, Trợ lý hợp âm & Gióng lời hát     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ GIAI ĐOẠN 3: TÍCH HỢP CLIPBOARD THÔNG MINH & HỆ SINH THÁI XUẤT FILE    │
│ Tự động nhận diện ABC từ AI, Dán trực tiếp vào Word, Xuất MIDI/MusicXML │
└────────────────────────────────────────────────────────────────────────┘
```

---

### GIAI ĐOẠN 1: NỀN TẢNG KỸ THUẬT & ĐỘ TIN CẬY LÕI (CORE RELIABILITY)

**Mục tiêu:** Xóa bỏ hoàn toàn tình trạng âm thanh chập chờn, hình ảnh dán sang Word mờ hoặc lỗi font, và lỗi cú pháp mập mờ. Đưa độ tin cậy của phần render & phát nhạc lên 100%.

#### Các hạng mục công việc chi tiết:
1. **Tái cấu trúc Bộ phát âm thanh (Web Audio Engine):**
   - Cơ chế tự động mở khóa `AudioContext` khi người dùng bấm bất kỳ đâu trong cửa sổ để không bao giờ bị chính sách Autoplay của Chromium chặn tiếng.
   - Quản lý vòng đời phát nhạc chặt chẽ: Dừng luồng phát cũ an toàn khi người dùng chỉnh sửa văn bản, tránh tình trạng phát chồng âm hoặc méo tiếng.
   - Thêm tính năng **Playhead Cursor (Con trỏ bám nhịp thời gian thực)**: Tận dụng `TimingCallbacks` của `abcjs` để vẽ một vạch sáng mượt mà chạy qua từng nốt nhạc trên khuông khi âm thanh vang lên.
   - Đồng bộ 2 chiều (Click-to-Highlight): Bấm vào bất kỳ nốt nào trên khuông nhạc -> con trỏ văn bản tự nhảy đến đúng ký tự nốt đó trong ô soạn thảo.

2. **Cơ chế Xuất ảnh Vector / Bitmap Siêu nét cho Word & Docs:**
   - Khắc phục lỗi font tiếng Việt: Nhúng bảng font chuẩn (Open Sans / Segoe UI) dưới dạng Base64 trực tiếp vào thẻ `<defs><style>` của SVG trước khi đưa vào Canvas rasterize.
   - Render ở mật độ điểm ảnh cao **300 DPI** (hệ số scale $3\times$ hoặc $4\times$ thay vì $2\times$ thô sơ hiện tại).
   - Đảm bảo lề và khoảng cách không bị cắt xén khi bản nhạc có nhiều khuông hoặc nhiều trang.

3. **Hệ thống Linter & Báo lỗi cú pháp thông minh:**
   - Thay thế dòng thông báo lỗi đỏ chung chung bằng bảng thông báo chi tiết:
     + Dòng số mấy? Vị trí ký tự nào?
     + Lỗi gì? (Ví dụ: *"Ô nhịp 3 đang có 5 phách đen, vượt quá số chỉ nhịp 4/4"* hoặc *"Dấu đóng ngoặc hợp âm `]` bị thiếu"*).
     + Gợi ý sửa nhanh (1-click fix).

**Tiêu chí nghiệm thu (Acceptance Criteria):**
- [x] Phát nhạc ổn định, tự động unlock Web Audio, không bị câm tiếng hay treo synth.
- [x] Vạch Playhead chạy mượt đúng tốc độ BPM bám theo nốt nhạc thời gian thực.
- [x] Click trên nốt nhạc trên khuông tự nhảy con trỏ mã nguồn (Bi-directional sync).
- [x] Linter báo lỗi cú pháp nhạc lý trực quan với 1-click jump đến dòng/cột bị sai.
- [x] Copy ảnh bản nhạc 300 DPI nhúng font UTF-8 tiếng Việt sắc nét, chuẩn in ấn và chuẩn Microsoft Word.

---

### GIAI ĐOẠN 2: TRẢI NGHIỆM SOẠN NHẠC KHOA HỌC (VISUAL MUSIC COMPOSER)

**Mục tiêu:** Người dùng không cần là lập trình viên hay chuyên gia ABC notation vẫn soạn được bản nhạc hoàn chỉnh một cách trực quan, khoa học, đi từ bản chất nhạc lý.

#### Các hạng mục công việc chi tiết:
1. **Bàn phím Piano ảo (Interactive Virtual Piano):**
   - Thiết kế bàn phím đàn từ 25-31 phím (quãng $C_4$ đến $C_6$, có thể chuyển quãng $8\text{va}\uparrow / 8\text{va}\downarrow$), bao gồm phím trắng và phím đen.
   - Hiển thị rõ tên nốt quốc tế (C, D, E, F, G, A, B) và hệ Solfège (Đô, Rê, Mi, Fa, Sol, La, Si).
   - Khi bấm chuột hoặc gõ phím máy tính:
     + Phát ra âm thanh nốt tức thì ($< 25\text{ ms}$) bằng bộ tổng hợp đa âm piano tự nhiên.
     + Phím đàn lún xuống với hiệu ứng thị giác sắc nét.
     + Tự động chèn ký tự nốt tương ứng vào ô soạn thảo theo đúng trường độ đang chọn.

2. **Thanh thiết lập Nhạc lý Đầu khuông (Score Setup Header):**
   - **Khóa nhạc (Clef):** Nút chọn trực quan giữa Khóa Sol ($\oint$ Treble) và Khóa Fa ($\mathcal{O}$ Bass).
   - **Số chỉ nhịp (Time Signature):** Bộ chọn nhịp phân số phổ biến: $2/4, 3/4, 4/4, 6/8, 12/8$ và nhịp tự do.
   - **Điệu tính (Key Signature):** Bộ chọn điệu tính theo Vòng tròn bậc 5 (Circle of Fifths) từ $0$ dấu hóa (Đô trưởng / La thứ) đến các dấu thăng/giáng phức tạp.
   - **Tốc độ (Tempo/BPM):** Thanh trượt điều chỉnh từ $50$ đến $180\text{ BPM}$ kèm nhãn tốc độ và cảm xúc (Chậm buồn, Thong thả, Vừa phải, Rộn rã, Nhanh).

3. **Bảng chọn Giá trị Nốt & Tiết tấu (Duration & Articulation Palette):**
   - Nút chọn hình nốt chuẩn đồ họa:
     + 𝅝 Nốt Tròn (Whole - 4 phách)
     + 𝅗𝅥 Nốt Trắng (Half - 2 phách)
     + 𝅘𝅥 Nốt Đen (Quarter - 1 phách)
     + 𝅘𝅥𝅮 Nốt Móc đơn (Eighth - 1/2 phách)
     + 𝅘𝅥𝅯 Nốt Móc kép (Sixteenth - 1/4 phách)
   - Công cụ gia tăng trường độ: Dấu chấm dôi ($\cdot$), Dấu thăng ($\sharp$), Dấu giáng ($\flat$), Nốt lặng ($\mathcal{Z}$), Vạch nhịp ($|$), Đoạn lặp ($|: :|$).

4. **Trợ lý Hợp âm & Dòng Lời bài hát (Harmony & Lyrics Assistant):**
   - Bộ phím bấm chèn nhanh hợp âm theo giọng: Khi chọn giọng Đô trưởng, tự động gợi ý các hợp âm trong giọng ($C$, $Dm$, $Em$, $F$, $G$, $Am$, $G^7$) kèm chú thích cảm xúc (Tươi sáng, Trầm buồn, Bay bổng, Hùng tráng...).
   - Bấm vào hợp âm sẽ phát ra âm thanh hòa âm (chord tones) ngay lập tức giúp người dùng cảm nhận được cảm xúc trước khi chèn vào bản nhạc.

5. **Bộ điều khiển Thu phóng & Giao diện hiện đại (Modern High-DPI UI):**
   - Thiết kế theo chuẩn giao diện hiện đại của HapiCopi: Tông sáng, phẳng, đường nét thanh lịch, không dùng góc bo thô kệch.
   - Hỗ trợ phóng to / thu nhỏ khuông nhạc (Zoom $65\% - 180\%$).
   - Tương thích $100\%$ với các mức phóng màn hình Windows ($100\%, 125\%, 150\%$).

**Tiêu chí nghiệm thu (Acceptance Criteria):**
- [x] Một người dùng chưa biết cú pháp ABC có thể soạn xong giai điệu bài hát thiếu nhi chỉ bằng chuột bấm phím Piano ảo và bảng trường độ trong vòng chưa đầy 2 phút.
- [x] Đổi giọng hoặc nhịp chỉ bằng 1 cú click trên thanh Setup Header và tự động đồng bộ hai chiều với văn bản ABC.
- [x] Bấm nút hợp âm vừa nghe thấy âm thanh hợp âm vang lên vừa chèn hợp âm vào khuông nhạc.
- [x] Giao diện hiển thị sắc nét trên cả màn hình 1080p 100% và laptop 125%/150% DPI, hỗ trợ Zoom mượt mà.

---

### GIAI ĐOẠN 3: TÍCH HỢP CLIPBOARD THÔNG MINH & HỆ SINH THÁI XUẤT FILE

**Mục tiêu:** Đưa tính năng soạn nhạc trở thành một vũ khí đắc lực trong quy trình làm việc hàng ngày của HapiCopi, liên kết mật thiết với Clipboard và các phần mềm soạn thảo văn bản.

#### Các hạng mục công việc chi tiết:
1. **Bộ nhận diện Clipboard Bản nhạc thông minh (Music Clipboard Interceptor):**
   - Bổ sung bộ lọc trong `Core/ClipboardSource.cs` và `UI/HapiCopiApplicationContext.cs`:
     + Nhận diện các khối code ````abc ... ```` từ ChatGPT/Claude/DeepSeek.
     + Nhận diện các đoạn văn bản thô có cấu trúc ABC chuẩn (`X:\n...`, `T:\n...`, `K:\n...`).
   - Tự động gọi nền WebView2 render thành ảnh khuông nhạc 300 DPI, đóng gói vào Clipboard HTML (`CF_HTML`) để khi người dùng nhấn `Ctrl+V` vào Word/Docs là ra ngay bản nhạc trình bày đẹp mắt.
   - Hiện Toast thông báo: *"Đã nhận diện bản nhạc ABC — [Mở chỉnh sửa]"*.

2. **Hệ sinh thái Xuất & Chuyển đổi định dạng đa dạng:**
   - **Xuất ảnh Bitmap:** Copy PNG 300 DPI (nền trắng hoặc nền trong suốt).
   - **Xuất đồ họa Vector:** Copy mã SVG hoặc lưu file `.svg` cho dân thiết kế đồ họa.
   - **Xuất file âm thanh/MIDI:** Tải file `.mid` (Standard MIDI File) để mở trong MuseScore, FL Studio, Logic Pro, GarageBand.
   - **Xuất MusicXML:** Chuyển đổi ngược từ ABC sang định dạng MusicXML chuẩn để nhập vào các phần mềm ký âm chuyên nghiệp (Finale, Sibelius, MuseScore).

**Tiêu chí nghiệm thu (Acceptance Criteria):**
- [ ] Copy đoạn code ABC bất kỳ từ chat ChatGPT -> chuyển sang Microsoft Word bấm `Ctrl+V` -> hiển thị ngay lập tức hình ảnh bản nhạc rõ nét mà không cần mở app lên thao tác thủ công.
- [ ] Bấm nút "Tải file MIDI" -> file `.mid` phát chuẩn xác trên Windows Media Player hoặc phần mềm làm nhạc.

---

## 4. MA TRẬN PHÂN CHIA TRÁCH NHIỆM & CÔNG NGHỆ

| Thành phần | File liên quan | Công nghệ / Thư viện sử dụng |
|:---|:---|:---|
| **Giao diện Editor & Đàn Piano** | `WebViewResources/abceditor.html` | HTML5 Canvas, Web Audio API, SVG, CSS Grid/Flexbox |
| **Thư viện Ký âm & Âm thanh** | `WebViewResources/abcjs-basic-min.js` | ABCJS v6.4.4 (SynthController, TimingCallbacks, VisualSheet) |
| **Window Host Form** | `UI/AbcEditorForm.cs` | WinForms, Microsoft.Web.WebView2, UiTheme |
| **Tích hợp Clipboard Lõi** | `Core/ClipboardSource.cs`, `UI/HapiCopiApplicationContext.cs` | Win32 Clipboard API, CF_HTML Packager |
| **Xử lý Chuyển đổi File** | `WebViewResources/xml2abc.js`, `jszip.min.js` | XML DOMParser, JSZip, MIDI Builder |

---

## 5. KẾ HOẠCH BẢO ĐẢM CHẤT LƯỢNG & KIỂM THỬ (QA & TESTING)

1. **Bộ Test bản nhạc chuẩn nhạc lý:**
   - Test 1: Bản nhạc đơn âm, âm vực hẹp (Giai điệu dân ca / đồng dao).
   - Test 2: Bản nhạc nhiều bè, có nốt kép/hợp âm phức tạp (Piano sonatina).
   - Test 3: Bản nhạc có lời bài hát tiếng Việt có dấu đầy đủ và dấu luyến phức tạp.
   - Test 4: Bản nhạc biến đổi nhịp điệu (đảo phách, liên ba, chấm dôi kép).
2. **Kiểm tra môi trường hiển thị:**
   - Màn hình Full HD (1920x1080) tỉ lệ 100%.
   - Màn hình laptop độ phân giải cao (1920x1200 / 2560x1440) tỉ lệ phóng 125% và 150%.
3. **Kiểm tra tương thích đích dán (Paste Targets):**
   - Microsoft Word 2016 / 2019 / 2021 / Microsoft 365.
   - Google Docs (trên Chrome và Edge).
   - LibreOffice Writer.

---

## 6. KẾT LUẬN

Kế hoạch này giải quyết dứt điểm sự "thô sơ" và "kém tin cậy" của module nhạc hiện tại, biến HapiCopi thành công cụ hỗ trợ ký âm và soạn nhạc thông minh, khoa học và tiện dụng hàng đầu trên Windows, bổ trợ hoàn hảo cho thế mạnh Clipboard DLP và AutoFormat vốn có của phần mềm.
