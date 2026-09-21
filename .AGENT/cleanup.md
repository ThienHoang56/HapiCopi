# Cleanup notes

## Đã xóa (2026-09-21)

| Mục | Lý do |
|-----|--------|
| `Backend/` | Chỉ còn `.env` thừa; không code nào đọc `Backend/` |
| `Clients/AiClient.cs` | Dead code — gọi Gemini/OpenRouter, không chỗ nào gọi; app không có server |

`AppConfig` đọc `.env` từ `BaseDirectory` (thư mục chạy app), không từ `Backend/`.

## Chưa xóa (cố ý)

| Mục | Ghi chú |
|-----|---------|
| `dist/` | ~163MB publish output — **local only**, chưa gitignore; **không commit** |
| `test-diagrams/` | Fixture test render sơ đồ — hữu ích, không phải rác bắt buộc |

## Nên làm khi dọn tiếp

1. Thêm `dist/` vào `.gitignore` rồi xóa local (khi user cho phép).
2. Không commit `.env` / `.env.bak`.
