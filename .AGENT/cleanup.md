# Cleanup notes

## Đã xóa (2026-09-21)

| Mục | Lý do |
|-----|--------|
| `Backend/` | Chỉ còn `.env` thừa; không code nào đọc `Backend/` |
| `Clients/AiClient.cs` | Dead code — gọi Gemini/OpenRouter, không chỗ nào gọi; app không có server |

`AppConfig` đọc `.env` từ `BaseDirectory` (thư mục chạy app), không từ `Backend/`.

## Đã dọn dẹp & sắp xếp lại (2026-10)

| Mục | Trạng thái mới | Lý do |
|-----|----------------|-------|
| `test-diagrams/` | Chuyển vào [`testing/diagrams/`](../testing/diagrams/) | Gom toàn bộ dữ liệu kiểm thử về thư mục `testing/` |
| `*.txt` báo cáo test ở root | Chuyển vào [`testing/reports/`](../testing/reports/) | Tránh làm bừa bộn thư mục gốc khi chạy benchmark |
| Music Engine (`AbcEditorForm`, Web assets, docs) | Đã xóa hoàn toàn | Loại bỏ tính năng không cốt lõi để tập trung 100% vào DLP và AI Math |
| License, Sponsor, Quota, Telemetry | Đã xóa hoàn toàn | Ứng dụng hoạt động 100% offline, không phiền phức, không gửi dữ liệu ra mạng |
| `Core/` | Tổ chức thành 6 module: `Clipboard/`, `Dlp/`, `Formatters/`, `Math/`, `Storage/`, `System/` | Codebase gọn gàng, rõ trách nhiệm, dễ bảo trì |

## Chưa xóa (cố ý)

| Mục | Ghi chú |
|-----|---------|
| `dist/` | ~163MB publish output — **local only**, chưa gitignore; **không commit** |

## Nên làm khi dọn tiếp

1. Thêm `dist/` vào `.gitignore` rồi xóa local (khi user cho phép).
2. Không commit `.env` / `.env.bak`.
