# PHIÊN LÀM VIỆC 4: RÀ SOÁT RỦI RO KỸ THUẬT & RACE CONDITION (CHECK TECHNICAL)

Phiên làm việc này tập trung toàn bộ nguồn lực vào việc rà soát, định lượng và kiểm soát các **nguy cơ xung đột hệ điều hành (Windows OS Conflict), lỗi Race Condition trong xử lý bất đồng bộ, lỗi tranh chấp Clipboard, và rò rỉ tài nguyên** của HapiCopi trước khi triển khai thực tế.

---

## 📂 Danh mục Tài liệu trong Phiên

1. [**`PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md):
   - Kế hoạch tổng thể và kiến trúc phòng vệ toàn diện gồm **13 Trụ cột Kỹ thuật**:
     - **7 Phân hệ Phòng vệ Nội tại (Internal Architecture):**
       - Phân hệ Clipboard & Windows Message Loop (Retry Backoff, Re-entrancy Guard, STA Enforcement).
       - Phân hệ Xử lý Bất đồng bộ (Generation Counter chống Out-of-Order, Atomic File I/O).
       - Phân hệ Màn hình & Đồ họa (High-DPI PerMonitorV2, cấm `WS_EX_COMPOSITED`, chống Off-screen).
       - Phân hệ Giao tiếp WebView2 & DOM (Zero Duplicate DOM Events, Ready Queue).
       - Quản lý Vòng đời & Bộ nhớ (Single-Instance Mutex, Zero Ghost Process, Zero-Alloc Paint).
       - Phân hệ Cập nhật Tự động (SHA-256 Hash Guard).
       - Xử lý Ngoại lệ Toàn cục (Global Unhandled Exception Traps).
     - **6 Cạm bẫy Môi trường Thực chiến Windows (OS Ecosystem Pitfalls):**
       - Phòng thủ Antivirus / Windows Defender (False Positive, UAC Manifest `asInvoker`).
       - Tương thích Bộ gõ Tiếng Việt (Unikey/EVKey clipboard macro filter).
       - Xử lý Đường dẫn Unicode Tiếng Việt & Khoảng trắng (`C:\Users\Nguyễn Văn A\...`).
       - Kiểm soát Môi trường Thiếu WebView2 Runtime (Kiểm tra Registry, Fallback Bootstrapper).
       - Phân quyền Windows & Cơ chế Cách ly UIPI (User Interface Privilege Isolation).
       - Sức khỏe Keyboard Hook & Xử lý Timeout Hệ thống (`LowLevelHooksTimeout`).

2. [**`CHECKLIST_TECHNICAL_RISKS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/CHECKLIST_TECHNICAL_RISKS.md):
   - Bản checklist chi tiết từng đầu việc tương ứng với 13 trụ cột kỹ thuật để kiểm tra chéo (stress-test và code audit) nhằm đảm bảo ứng dụng không mắc phải bất kỳ lỗi cơ bản nào trên máy người dùng.

---

## 🎯 Mục tiêu Cuối cùng
Đảm bảo HapiCopi vận hành bền bỉ (Rock-Solid Stability), không crash ngầm, không xung đột với các phần mềm khác trên Windows, và bảo vệ an toàn 100% dữ liệu clipboard của người dùng.

