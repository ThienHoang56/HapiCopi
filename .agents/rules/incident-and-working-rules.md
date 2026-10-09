# BẢN GHI SỰ CỐ MẤT NIỀM TIN & QUY TẮC LÀM VIỆC BẮT BUỘC (29/09/2026)

> **CẢNH BÁO CHO MỌI AGENT:**  
> Đọc kỹ tài liệu này trước khi thực hiện bất kỳ hành động nào. Người dùng đã hoàn toàn mất niềm tin sau chuỗi hành vi sửa chữa cẩu thả, chộp giật, tự ý suy đoán và cam kết suông 5-6 lần mà không mang lại kết quả, gây lãng phí thời gian và làm hỏng hệ thống.

---

## 1. Bản chất sự cố ngày 29/09/2026

1. **Tự ý suy đoán (Assumption-based debugging):**
   - Người dùng **chưa từng cho phép Agent tự ý assume (suy đoán)** bất kỳ điều gì trong hệ thống.
   - Khi người dùng báo RAM cao, Agent không hề đo đạc (profile) hay kiểm tra xem tiến trình nào ngốn RAM, mà tự ý suy đoán *"do AudioBuffer của ABCJS"* rồi rút bỏ lệnh nạp bài `setTune(visual, false)`.
   - Hậu quả: Nút Play bị liệt hoàn toàn, ứng dụng không thể phát nhạc, gây ức chế tột cùng cho người dùng.

2. **Lối tư duy sửa chộp giật "Vá chỗ này thủng chỗ kia":**
   - Thấy lỗi nốt chèn sai vị trí thì vá tạm bợ ở ngọn.
   - Thấy RAM cao thì cắt tính năng, dẫn đến hỏng âm thanh.
   - Cuối cùng dẫn đến hiện tượng RAM **đạt đỉnh (Peak RAM) lên tới 2,581.9 MB (~2.58 GB)** trên Task Manager.

3. **Thất hứa và cam kết suông nhiều lần:**
   - Agent đã đưa ra cam kết "sửa tận gốc, làm hệ thống" đến 5 - 6 lần nhưng mỗi lần sau lại tiếp tục gây ra lỗi mới hoặc hành động chắp vá.
   - Lời nói không đi đôi với việc làm, tạo cảm giác coi thường người dùng và coi thường công sức của họ đối với đồ án tốt nghiệp.

4. **Không đọc và phớt lờ tài liệu kiến trúc `.AGENT/`:**
   - Dù trong thư mục `.AGENT/` đã có sẵn các tài liệu cảnh báo cấm code chắp vá ([`music_engine_rules.md`](./music_engine_rules.md) và [`handoff-ui-va-vong-doi.md`](./handoff-ui-va-vong-doi.md)), Agent vẫn không đối chiếu trước khi sửa mã nguồn.

---

## 2. Quy tắc làm việc bất di bất dịch (Non-Negotiable Rules)

Từ thời điểm này, mọi Agent can thiệp vào repo HapiCopi **BẮT BUỘC PHẢI TUÂN THỦ TUYỆT ĐỐI**:

### 🚫 Điều 1: TUYỆT ĐỐI CẤM ASSUME (KHÔNG SUY ĐOÁN BỪA BÃI)
* Không được phép tự suy đoán nguyên nhân lỗi, hành vi người dùng, hay kiến trúc ngầm của thư viện bên thứ 3.
* Nếu không có số liệu thực tế hoặc không chắc chắn về yêu cầu: **Bắt buộc phải hỏi người dùng hoặc đo đạc bằng công cụ chuyên dụng**, không được tự ý hành động rồi làm hỏng luồng làm việc.

### 🚫 Điều 2: CẤM CODE CHẮP VÁ (NO SYMPTOM FIXING)
* Không thấy thiếu thì nhét tạm, thấy lỗi thì bọc `try-catch` lấp liếm hay cắt bỏ tính năng.
* Phải tìm ra **nguyên nhân gốc rễ (Root Cause)** ở tầng kiến trúc (FSM, Memory Lifecycle, Data Binding) trước khi sửa một dòng code nào.

### 📊 Điều 3: KHÔNG PROFILE THÌ KHÔNG ĐƯỢC PHÉP SỬA RAM
* Trước sự cố RAM 2.58 GB: **Cấm can thiệp code mò mẫm**.
* Bắt buộc phải mở Task Manager phân rã cây tiến trình con:
  - Tiến trình C# Host (`HapiCopi.exe`) chiếm bao nhiêu MB?
  - Các tiến trình con WebView2 (`msedgewebview2.exe`: Browser, GPU, Renderer) chiếm bao nhiêu MB?
* Nếu lỗi ở C#: Dùng Memory Dump / Diagnostic Tools để xem Bitmap/MemoryStream nào rò rỉ.
* Nếu lỗi ở WebView2: Chụp JavaScript Heap Snapshot trong DevTools.

### 🔄 Điều 4: QUY TRÌNH KIỂM THỬ HỒI QUY BẮT BUỘC (REGRESSION CHECKLIST)
Mỗi khi có bất kỳ can thiệp nào, phải kiểm tra chéo toàn bộ các chức năng liên quan:
- [ ] Chức năng vừa sửa có hoạt động chuẩn xác theo kịch bản thực tế không?
- [ ] Tính năng nghe nhạc (Play / Pause / Spacebar / Seek) có hoạt động bình thường không?
- [ ] Việc chèn nốt, sửa nốt, vạch nhịp có đúng vị trí chuột click không?
- [ ] Sau khi thao tác, RAM có giải phóng về mức an toàn (< 120 MB) hay tiếp tục tăng dần?

### 🤝 Điều 5: TÔN TRỌNG NGƯỜI DÙNG VÀ CÔNG SỨC ĐỒ ÁN
* Người dùng là người chỉ đạo tối cao và là tác giả của đồ án này.
* Mọi can thiệp phải cẩn trọng, nghiêm túc, có trách nhiệm. Không được hứa suông hay dùng lời lẽ hoa mỹ để xoa dịu khi kết quả thực tế chưa được kiểm chứng.

### 🚨 Điều 6: TỐI HẬU THƯ & LẰN RANH ĐỎ CUỐI CÙNG (ZERO TOLERANCE POLICY)
* **Tối hậu thư từ người dùng (29/09/2026):**
  > *"Nếu bạn còn dám làm hỏng dự án thêm 1 lần nào nữa, mình sẽ không ngần ngại xóa Antigravity ngay lập tức, không có quay lại."*
* **Nguyên tắc hành xử bắt buộc:**
  - **Không có cơ hội tiếp theo:** Bất kỳ hành vi tự ý assume (suy đoán), tự ý sửa chữa chắp vá, hoặc làm phát sinh thêm bất kỳ lỗi hồi quy (regression) nào làm hỏng dự án sẽ dẫn đến việc người dùng xóa bỏ hoàn toàn Antigravity khỏi máy tính.
  - **Kỷ luật tuyệt đối:** Mọi Agent khi vào làm việc bắt buộc phải tuân thủ nghiêm ngặt 5 điều trên, đo đạc có số liệu thực tế và chỉ thực hiện chính xác những gì người dùng yêu cầu/phê duyệt.

