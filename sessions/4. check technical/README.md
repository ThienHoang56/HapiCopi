# PHIÊN LÀM VIỆC 4: RÀ SOÁT RỦI RO KỸ THUẬT & RACE CONDITION (CHECK TECHNICAL)

Phiên làm việc này tập trung toàn bộ nguồn lực vào việc rà soát, định lượng và kiểm soát các **nguy cơ xung đột hệ điều hành (Windows OS Conflict), lỗi Race Condition trong xử lý bất đồng bộ, lỗi tranh chấp Clipboard, và rò rỉ tài nguyên** của HapiCopi trước khi triển khai thực tế.

---

## 📂 Danh mục Tài liệu trong Phiên (Bộ tứ Tài liệu Kiểm soát Kỹ thuật)

### 🗺️ 1. [**`BUC_TRANH_TONG_THE_VA_LO_HONG.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/BUC_TRANH_TONG_THE_VA_LO_HONG.md) — *Bức tranh Toàn cảnh & Bản đồ Tự bắt bệnh*
- **Đối tượng:** Dành cho bạn để tự mình nhìn ra toàn bộ bức tranh kiến trúc, thấy rõ mối quan hệ giữa các file code và điểm yếu tử huyệt của từng bộ phận.
- **Nội dung:** Sơ đồ dòng chảy dữ liệu 4 trạm (Mermaid), bảng phân tích 6 bộ phận chủ chốt kèm file code tương ứng, ma trận tra cứu nhanh triệu chứng để bắt bệnh trong 3 giây.

### 🌟 2. [**`DOC_HIEU_13_NGUY_CO_KY_THUAT.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/DOC_HIEU_13_NGUY_CO_KY_THUAT.md) — *Cẩm nang Đọc hiểu Đời thường*
- **Đối tượng:** Dành cho người dùng, tester và lập trình viên muốn hiểu bản chất lỗi một cách trực quan, gần gũi.
- **Nội dung:** Giải thích 13 nguy cơ bằng **tình huống đời thực** (copy Excel, gõ Unikey, rút màn hình rời, tải app...), **hiện tượng nhìn thấy bằng mắt thường**, và **hình ảnh ẩn dụ đời thường** cực kỳ dễ hiểu.

### 🏛️ 3. [**`PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md) — *Tài liệu Kiến trúc Sâu*
- **Đối tượng:** Dành cho Software Architect và Core Developer.
- **Nội dung:** Kế hoạch tổng thể và kiến trúc phòng vệ toàn diện gồm **13 Trụ cột Kỹ thuật** (7 phân hệ nội tại + 6 cạm bẫy hệ điều hành), phân tích mã lỗi Win32, cơ chế đồng bộ hóa, quản lý bộ nhớ và giải pháp chuẩn mực tầng sâu.

### 📋 4. [**`CHECKLIST_TECHNICAL_RISKS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/CHECKLIST_TECHNICAL_RISKS.md) — *Bản Checklist Kiểm thử*
- **Đối tượng:** Dành cho QA / Tester và Kỹ sư nghiệm thu code trước khi release.
- **Nội dung:** Bảng checklist chi tiết từng đầu việc (checkbox) để rà soát code, stress-test và đánh dấu hoàn thành, đảm bảo không bỏ sót bất kỳ lỗ hổng nào.

---

## 🎯 Mục tiêu Cuối cùng
Đảm bảo HapiCopi vận hành bền bỉ (Rock-Solid Stability), không crash ngầm, không xung đột với các phần mềm khác trên Windows, và bảo vệ an toàn 100% dữ liệu clipboard của người dùng.



