# PHIÊN LÀM VIỆC 4: RÀ SOÁT RỦI RO KỸ THUẬT & RACE CONDITION (CHECK TECHNICAL)

Phiên làm việc này tập trung toàn bộ nguồn lực vào việc rà soát, định lượng và kiểm soát các **nguy cơ xung đột hệ điều hành (Windows OS Conflict), lỗi Race Condition trong xử lý bất đồng bộ, lỗi tranh chấp Clipboard, và rò rỉ tài nguyên** của HapiCopi trước khi triển khai thực tế.

---

## 📂 Danh mục Tài liệu trong Phiên

1. [**`PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/PLAN_TECHNICAL_AUDIT_AND_RACE_CONDITIONS.md):
   - Kế hoạch tổng thể và kiến trúc phòng vệ cho 7 phân hệ cốt lõi:
     - Phân hệ Clipboard & Windows Message Loop (Retry Backoff, Re-entrancy Guard, STA Enforcement).
     - Phân hệ Xử lý Bất đồng bộ (Generation Counter chống Out-of-Order, Atomic File I/O).
     - Phân hệ Màn hình & Đồ họa (High-DPI PerMonitorV2, cấm `WS_EX_COMPOSITED`, chống Off-screen).
     - Phân hệ Giao tiếp WebView2 & DOM (Zero Duplicate DOM Events, Ready Queue).
     - Quản lý Vòng đời & Bộ nhớ (Single-Instance Mutex, Zero Ghost Process, Zero-Alloc Paint).
     - Phân hệ Cập nhật Tự động (SHA-256 Hash Guard).
     - Xử lý Ngoại lệ Toàn cục (Global Unhandled Exception Traps).

2. [**`CHECKLIST_TECHNICAL_RISKS.md`**](file:///c:/Users/ThienNLH3/Desktop/Đại%20học%20FPT/Kỳ%208/EXE/HapiCopi/sessions/4.%20check%20technical/CHECKLIST_TECHNICAL_RISKS.md):
   - Bản checklist chi tiết từng đầu việc để kiểm tra chéo (stress-test và code audit) nhằm đảm bảo ứng dụng không mắc phải bất kỳ lỗi cơ bản nào trên máy người dùng.

---

## 🎯 Mục tiêu Cuối cùng
Đảm bảo HapiCopi vận hành bền bỉ (Rock-Solid Stability), không crash ngầm, không xung đột với các phần mềm khác trên Windows, và bảo vệ an toàn 100% dữ liệu clipboard của người dùng.
