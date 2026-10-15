# Session 3: Tinh gọn & Đánh bóng ứng dụng trước khi Deploy (Polish Before Deploy)

## Mục tiêu trọng tâm (Bản hoàn chỉnh tinh khiết sẵn sàng Deploy)
1. **Khắc phục triệt để lặp nút Ribbon & Sidebar:**
   - Ribbon: Chứa toàn bộ ký hiệu nguyên tử (toán tử, ma trận, quan hệ, ký tự Hy Lạp).
   - Sidebar Mẫu công thức: Chuyển thành các phương trình mẫu hoàn chỉnh (Equation Presets như Bậc 2, Tích phân từng phần, Hệ phương trình, Taylor...).
2. **Xây dựng Chức năng Tạo Template Tự động (Auto Template Generator):**
   - Xóa bỏ hoàn toàn badge placeholder `Coming Soon`.
   - Cho phép người dùng lưu công thức hiện tại thành template mẫu riêng với cơ chế tự động tạo placeholder `#?` để điền số.
   - Lưu trữ bền vững hai lớp (`localStorage` + `custom_templates.json` qua C# backend).
   - Chèn 1-click và hỗ trợ xóa mẫu cá nhân.
3. **Chuẩn hóa Modal Hướng dẫn & Phím tắt:**
   - Icon SVG nét rỗng (outline/stroke) đồng nhất app.
   - Xóa phụ đề dài dòng thừa thãi.
   - Bảng phím tắt gõ nhanh sạch sẽ, không bị lộ raw LaTeX syntax.
   - Rút gọn phím tắt điều khiển & mẹo AI thành bullet points ngắn gọn.
   - Footer chuẩn: `HapiCopi v1.2.5`.
4. **Thu gọn & Hiện đại hóa các Panel phụ (Clipboard History, Settings, Update):**
   - Thu nhỏ kích thước về dạng Mini Popup ($420 - 480\text{ px}$), không còn to bự chảng.
   - Bỏ toàn bộ chữ mô tả rườm rà; thiết kế theo nguyên tắc "nhìn vào là hiểu ngay, tự giải thích (self-explanatory)".

## Tài liệu chi tiết
- Kế hoạch kỹ thuật: [PLAN_POLISH_BEFORE_DEPLOY.md](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/3.%20polish%20before%20deploy/PLAN_POLISH_BEFORE_DEPLOY.md)
- Checklist thực hiện: [TASKS.md](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/3.%20polish%20before%20deploy/TASKS.md)
