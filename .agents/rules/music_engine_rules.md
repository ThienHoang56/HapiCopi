# QUY TẮC KỸ THUẬT BẮT BUỘC: BỘ SOẠN NHẠC & ÂM THANH HAPICOPI

File này là quy chuẩn kỹ thuật bắt buộc cho mọi Agent khi làm việc với `abceditor.html`, `xml2abc.js`, hoặc hệ thống âm thanh HapiCopi Studio.

## 1. NGUYÊN TẮC CỐT LÕI: TUYỆT ĐỐI KHÔNG CODE CHẮP VÁ (NO SYMPTOM FIXING)
- Không bao giờ "thấy thiếu cái gì thì vá tạm cái đó vào".
- Mọi thay đổi logic liên quan đến Audio/Render PHẢI tuân thủ Finite State Machine (FSM) và bản đặc tả `document/BAO_CAO_LOGIC_AM_THANH.md`.

## 2. BÀI HỌC VỀ THƯ VIỆN BÊN THỨ 3 (ABCJS SYNTH GOTCHAS)
- Không bao giờ coi thư viện bên thứ 3 là hộp đen hoàn hảo; phải kiểm tra mã nguồn thực tế.
- Bug bộ nhớ đệm trong `ABCJS.synth.SynthController`:
  - `synthControl.setTune(visual, false)` của ABCJS **chỉ gán lại hình vẽ `visualObj` nhưng KHÔNG reset cờ `isLoaded = false`** và **KHÔNG xóa `midiBuffer` cũ**.
  - Khi người dùng bấm Play, hàm `runWhenReady` thấy `isLoaded === true` sẽ phát thẳng buffer cũ, dẫn đến lỗi: **Mắt thấy bài mới nhưng loa phát bài cũ**.
  - **Quy tắc bắt buộc:** Mỗi khi chuyển bài hoặc bản nhạc thay đổi, bắt buộc phải:
    1. Dập tắt âm thanh đang phát dở (`pause()`).
    2. Reset triệt để cờ cache: `synthControl.isLoaded = false` và giải phóng `midiBuffer`.
    3. Đưa đồng hồ và thanh tiến trình về `0:00`.

## 3. QUY TRÌNH VÒNG ĐỜI NẠP BÀI MỚI (NEW SCORE INGESTION INVARIANTS)
Mọi thao tác nạp bài (mở file XML/MXL/ABC, chọn bài mẫu, kéo thả file, dán mã) phải thực hiện nghiêm ngặt theo chu trình 3 giai đoạn:
1. **Giai đoạn 1 - Hủy sạch bài cũ:** Dừng âm thanh, hủy timers, xóa highlight và kim con trỏ trên SVG, reset cache âm thanh.
2. **Giai đoạn 2 - Vẽ sheet mới (View):** Gọi `ABCJS.renderAbc()` vẽ tức thì lên màn hình (60 FPS).
3. **Giai đoạn 3 - Lập lịch âm thanh (Audio Model):** Debounce 300ms rồi mới tổng hợp âm thanh mới, không làm đơ giao diện gõ nốt.

## 4. QUY TẮC PHÁT ĐA BÈ (TRUE POLYPHONY)
- **Tuyệt đối không viết parser regex thủ công để lọc nốt:** Bản nhạc chuẩn MuseScore có nhiều bè (V:1 Khóa Sol, V:2 và V:3 Khóa Fa), hợp âm chùm, arpeggio, liên ba `(3:2:2`. Bất kỳ parser regex tự chế nào cũng sẽ làm mất bè và sai phách.
- Phải để engine chuẩn `ABCJS.synth` đảm nhiệm việc biên dịch toàn bộ các bè đồng thời.

## 5. QUY CHUẨN KIỂM THỬ BẮT BUỘC (MANDATORY TEST CRITERIA)
Trước khi kết luận một tính năng hoàn tất, CẤM CHỈ KIỂM THỬ KỊCH BẢN MÀU HỒNG. Phải chạy kiểm thử tự động trên Headless Browser tối thiểu 4 kịch bản chuyển đổi trạng thái:
1. **Kịch bản Chuyển bài (Score Transition Test):** Nạp bài A $\rightarrow$ Phát bài A $\rightarrow$ Nạp tiếp bài B $\rightarrow$ Bấm Play $\rightarrow$ Khẳng định 100% âm thanh phát ra là của bài B, không dính bài A.
2. **Kịch bản Cắt ngang (Interruption Test):** Đang bấm Play bài A $\rightarrow$ Nạp ngay bài B $\rightarrow$ Âm thanh bài A phải tắt ngấm ngay lập tức, không phát chồng lấn.
3. **Kịch bản Gõ nhanh (Rapid Typing / 60 FPS Test):** Gõ 10 nốt liên tiếp tốc độ cao $\rightarrow$ Giao diện nốt cập nhật tức thì, không bị nghẽn hay đơ chuột do biên dịch audio.
4. **Kịch bản Hết bài (Completion Rewind Test):** Bài phát đến nốt cuối $\rightarrow$ Tự động chuyển nút về icon Play, đồng hồ quay về `0:00`, xóa sạch highlight.

## 6. ĐẶT CÂU HỎI THỰC TẾ SỬ DỤNG (REAL-WORLD USER EXPERIENCE FIRST)
- **Đây không phải món đồ chơi để code tùy tiện theo suy diễn của Agent.**
- Đây là công cụ làm việc thực tế cho người làm nhạc, giáo viên, học sinh và người ký âm chuyên nghiệp.
- **Quy tắc bắt buộc:** Trước khi triển khai hoặc thay đổi bất kỳ tính năng tương tác nào, Agent **bắt buộc phải đặt câu hỏi làm rõ các kịch bản sử dụng thực tế (User Scenarios & Workflows)**:
  - Khi đang nghe nhạc mà sửa nốt thì nhạc dừng hay chạy tiếp?
  - Cơ chế nghe thử nốt (Auditioning) khi gõ phím hoặc cắm đàn MIDI?
  - Xử lý lặp đoạn (Loop) khi luyện tập ô nhịp khó?
  - Thao tác bàn phím (Shortcuts) có xung đột với thói quen gõ văn bản thông thường không?
- **Tuyệt đối không tự suy đoán hành vi của người dùng rồi code bừa.**

## 7. NGUYÊN TẮC NHẤT QUÁN TUYỆT ĐỐI (CONSISTENCY IS THE FOUNDATION)
> **"Nhất quán là nền tảng sống còn của sản phẩm. Bất kỳ sự không nhất quán nào giữa Chuột - Nốt - Âm thanh đều phá hỏng trải nghiệm người dùng."**

1. **TỐI KỴ: "Chuột một đằng, nốt một nẻo":**
   - Tuyệt đối **CẤM** hành vi: Chuột click vào ô nhịp đầu bài (ví dụ ô nhịp 2) nhưng nốt lại âm thầm bị nhét xuống tận ô nhịp cuối bài (`|]`).
   - Chuột click vào nốt nào/ô nhịp nào $\rightarrow$ Vạch kim phát nhạc (`abcjs-cursor`) và con trỏ nhập nốt (Caret) **BẮT BUỘC PHẢI nhảy đến chính xác vị trí đó**.
2. **Tách bạch giữa Điều hướng (Navigation) và Nhập nốt (Note Entry):**
   - Click chuột vào Sheet nhạc là để **Chọn nốt / Đặt kim phát nhạc** tại vị trí đó để nghe thử hoặc sửa.
   - Không được bắt sự kiện click toàn trang tự do để tự tiện thả thêm nốt lạ làm rách nhịp bản nhạc đang soạn.
3. **Đồng bộ 4 chiều (4-Way Alignment) - Không được lệch 1 ly:**
   - **Mắt thấy:** Vị trí nốt nhạc trên khuông nhạc SVG.
   - **Chuột trỏ / Phím gõ:** Vị trí con trỏ Caret và vạch kim phát nhạc.
   - **Mã nguồn:** Ký tự nốt tương ứng trong ô mã ABC.
   - **Tai nghe:** Âm thanh phát ra đúng nốt đó, đúng bè đó, đúng phách đó.
   - *Bốn yếu tố trên luôn luôn phải đồng nhất trên cùng một thực thể âm nhạc.*

## 8. CHUẨN MỰC THIẾT KẾ ĐỒNG BỘ ĐA PHÂN HỆ (CROSS-SESSION CONSISTENCY)
> **"HapiCopi là một hệ sinh thái đa phân hệ (Toán học, Bản nhạc, Sơ đồ Markdown, Khay hệ thống). Bất kỳ sự lệch chuẩn nào giữa các cửa sổ đều khiến toàn bộ sản phẩm trở nên lôm côm, chắp vá."**

1. **Bảng màu chủ đạo Hợp nhất (Unified Emerald Green Brand Tokens):**
   - Đồng bộ 100% với Logo "HC" xanh lá trên taskbar và khay hệ thống.
   - Brand Primary: `#059669` (Emerald 600) | Hover: `#047857` (Emerald 700)
   - Active Accent: `#ecfdf5` (Emerald 50) | Border Accent: `#a7f3d0` (Emerald 200)
   - Nền Header/Canvas: `#ffffff` / `#f8fafc` | Viền Header: `1px solid #e2e8f0`
   - Văn bản: Tiêu đề `#0f172a` (Slate 900) | Nhãn & Chú thích `#64748b` (Slate 500)
   - Tuyệt đối không tự ý dùng màu xanh dương `#2563eb` lệch tông với nhận diện thương hiệu.
2. **Quy chuẩn Iconography duy nhất (Single Icon System across All Sessions):**
   - **CẤM** dùng emoji màu mè và text symbols tự gõ (`+`, `-`, `◀`, `▶`, `?`, `↶`).
   - 100% dùng chung bộ Vector SVG Line-Art chuẩn: `viewBox="0 0 24 24"`, `stroke="currentColor"`, `stroke-width="1.75"`, `fill="none"`.
3. **Kích thước & Kiểu dáng Linh kiện chuẩn (Component Geometry Standard):**
   - Thanh Header: Chiều cao `44px` trên tất cả các cửa sổ.
   - Nút công cụ / Ribbon Button: Chiều cao `28px`, border-radius `5px`, viền `1px solid #cbd5e1`, font-size `12px`, font-weight `600`.
   - Nút Icon-Only: Kích thước hình vuông `28px x 28px`.
   - Segmented Switcher (`.mode-switch`): Chiều cao `30px`, padding `2px`, radius `6px`, nền `#f1f5f9`.
   - Toast thông báo: Bo tròn `8px`, nền tối mờ `rgba(15, 23, 42, 0.9)`, chữ trắng, đổ bóng `0 8px 24px rgba(0,0,0,0.15)`.
4. **Hệ thống Phím tắt Xuyên suốt (Universal Shortcuts):**
   - `Ctrl+Enter`: Luôn là "Sao chép ảnh chất lượng cao 300 DPI vào Clipboard" ở cả 3 trình soạn thảo (Toán học, Bản nhạc, Sơ đồ).
   - `Ctrl+B`: Luôn là "Bật/Tắt chế độ toàn màn hình Zen mode / Xem trước".
   - `Escape`: Luôn là "Thoát focus, đóng modal / đóng drawer".
   - `Spacebar`: Luôn là "Phát / Tạm dừng" trong trình phát âm thanh.
