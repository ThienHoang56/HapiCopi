# PHIÊN LÀM VIỆC 4: RÀ SOÁT RỦI RO KỸ THUẬT & RACE CONDITION (CHECK TECHNICAL)

Phiên làm việc này tập trung toàn bộ nguồn lực vào việc rà soát, định lượng và kiểm soát các **nguy cơ xung đột hệ điều hành (Windows OS Conflict), lỗi Race Condition trong xử lý bất đồng bộ, lỗi tranh chấp Clipboard, và rò rỉ tài nguyên** của HapiCopi trước khi triển khai thực tế.

---

## 📂 Danh mục Tài liệu trong Phiên (Được thiết kế theo 3 cấp độ)

### 🌟 1. [**`DOC_HIEU_13_NGUY_CO_KY_THUAT.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/DOC_HIEU_13_NGUY_CO_KY_THUAT.md) — *Nên đọc đầu tiên*
- **Đối tượng:** Dành cho người dùng, tester và lập trình viên muốn hiểu bản chất lỗi một cách trực quan, gần gũi.
- **Nội dung:** Giải thích 13 nguy cơ bằng **tình huống đời thực** (copy Excel, gõ Unikey, rút màn hình rời, tải app...), **hiện tượng nhìn thấy bằng mắt thường** (đơ máy, văng app, chớp nháy, rác lịch sử...), và **hình ảnh ẩn dụ đời thường** cực kỳ dễ hiểu.

### 🏛️ 2. [**`PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md) — *Tài liệu Kiến trúc*
- **Đối tượng:** Dành cho Software Architect và Core Developer.
- **Nội dung:** Kế hoạch tổng thể và kiến trúc phòng vệ toàn diện gồm **13 Trụ cột Kỹ thuật** (7 phân hệ nội tại + 6 cạm bẫy hệ điều hành), phân tích mã lỗi Win32, cơ chế đồng bộ hóa, quản lý bộ nhớ và giải pháp chuẩn mực tầng sâu.

### 📋 3. [**`CHECKLIST_TECHNICAL_RISKS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/CHECKLIST_TECHNICAL_RISKS.md) — *Bản Checklist Kiểm thử*
- **Đối tượng:** Dành cho QA / Tester và Kỹ sư nghiệm thu code trước khi release.
- **Nội dung:** Bảng checklist chi tiết từng đầu việc (checkbox) để rà soát code, stress-test và đánh dấu hoàn thành, đảm bảo không bỏ sót bất kỳ lỗ hổng nào.

---

## 🎯 Mục tiêu Cuối cùng
Đảm bảo HapiCopi vận hành bền bỉ (Rock-Solid Stability), không crash ngầm, không xung đột với các phần mềm khác trên Windows, và bảo vệ an toàn 100% dữ liệu clipboard của người dùng.


