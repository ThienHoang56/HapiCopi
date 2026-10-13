# Cleanup notes

## Đã xóa (2026-09-21)

| Mục | Lý do |
|-----|--------|
| `Backend/` | Chỉ còn `.env` thừa; không code nào đọc `Backend/` |
| `Clients/AiClient.cs` | Dead code — gọi Gemini/OpenRouter, không chỗ nào gọi; app không có server |

`AppConfig` đọc `.env` từ `BaseDirectory` (thư mục chạy app), không từ `Backend/`.

## Đã dọn dẹp & sắp xếp lại (2026-10-09)

| Mục | Trạng thái mới | Lý do |
|-----|----------------|-------|
| `test-diagrams/` | Chuyển vào [`testing/diagrams/`](../testing/diagrams/) | Gom toàn bộ dữ liệu kiểm thử về thư mục `testing/` |
| `*.txt` báo cáo test ở root (`ai_math_benchmark_report.txt`, `distill_test_report.txt`, `topo_test_report.txt`) | Chuyển vào [`testing/reports/`](../testing/reports/) | Tránh làm bừa bộn thư mục gốc khi chạy benchmark |
| `musescore_grade_music_engine_plan.md` | Chuyển vào [`document/`](../document/musescore_grade_music_engine_plan.md) | Thống nhất toàn bộ tài liệu đặc tả kỹ thuật vào `document/` |

## Chưa xóa (cố ý)

| Mục | Ghi chú |
|-----|---------|
| `dist/` | ~163MB publish output — **local only**, chưa gitignore; **không commit** |

## Nên làm khi dọn tiếp

1. Thêm `dist/` vào `.gitignore` rồi xóa local (khi user cho phép).
2. Không commit `.env` / `.env.bak`.
