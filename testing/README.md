# HapiCopi — Cấu trúc Thư mục Kiểm thử (Testing)

Thư mục này chứa toàn bộ các tài nguyên kiểm thử, dữ liệu mẫu (fixtures), ảnh chụp màn hình kiểm chứng (screenshots), và các báo cáo định lượng (benchmark reports) của dự án HapiCopi.

---

## 1. Cấu trúc thư mục

| Thư mục / File | Mô tả nội dung |
|----------------|----------------|
| [`reports/`](./reports/) | Chứa các bản ghi kết quả đo đạc định lượng thực tế (Benchmark Reports: ONNX Math, Topology, Distillation). Các file này được sinh tự động khi chạy cờ CLI. |
| [`diagrams/`](./diagrams/) | Bộ dữ liệu mẫu đầu vào (Fixtures) cho các sơ đồ kỹ thuật: PlantUML, Graphviz, D2, DBML, Nomnoml, Blockdiag, Seqdiag, Actdiag, Wavedrom, Vega-Lite, LaTeX Math, TikZ. |
| [`shots/`](./shots/) | Ảnh chụp màn hình giao diện (Screenshots) dùng để đối chiếu xác minh UI, tránh hồi quy giao diện. |
| [`Bug.txt`](./Bug.txt) | Ghi chú các lỗi thực tế được phát hiện trong quá trình kiểm thử cần theo dõi. |

---

## 2. Các lệnh chạy Kiểm thử Định lượng (CLI Benchmarks)

Theo nguyên tắc bất di bất dịch của HapiCopi (đo lường bằng số liệu thực tế, cấm võ đoán), mọi bài test đều có thể kích hoạt trực tiếp từ terminal:

### 2.1. Kiểm thử Toàn diện AI Math Engine (3 Tầng + ONNX + Resource Profiling)
```powershell
dotnet run -- --test-ai-math
```
- **Mục tiêu:** Đo lường độ trễ CPU / ký tự (< 15 ms), RAM working set (< 120 MB), kích thước weights ONNX (< 1 MB), kiểm tra nhận diện Số (Tầng 1), Toán tử (Tầng 2), Cấu trúc 2D (Tầng 3: Căn thức, Số mũ, Baseline tracking, Cân bằng ngoặc), và cơ chế Contrastive Repulsion.
- **Báo cáo xuất ra:** [`reports/ai_math_benchmark_report.txt`](./reports/ai_math_benchmark_report.txt).

### 2.2. Kiểm thử Tự học Thích ứng & Phạt Nhãn Sai (Distillation & Personal Calibration)
```powershell
dotnet run -- --test-distill
```
- **Mục tiêu:** Kiểm tra 7 biến thể hình học khi học nét mới, nhận diện chuỗi ký tự nguyên tử, căn chỉnh thông số không gian vector/số mũ, gợi ý ứng viên đa tầng (Candidate Ranking), và phạt đẩy lùi (Feature Repulsion) khi nhận diện sai.
- **Báo cáo xuất ra:** [`reports/distill_test_report.txt`](./reports/distill_test_report.txt).

### 2.3. Kiểm thử Bất biến Topo & Hình thái học (Topological Invariants)
```powershell
dotnet run -- --test-topo
```
- **Mục tiêu:** Kiểm tra các bất biến hình thái (vòng kín Euler, tự giao cắt, điểm gập Cusp) để phân biệt các cặp ký tự dễ nhầm (8 vs 3, 0 vs C, + vs -, v vs u).
- **Báo cáo xuất ra:** [`reports/topo_test_report.txt`](./reports/topo_test_report.txt).

### 2.4. Kiểm thử Bộ máy Nhận diện Chuỗi Toàn diện End-to-End (Vision Transformer / BTTR)
```powershell
dotnet run -- --test-e2e-math
```
- **Mục tiêu:** Kiểm tra mô hình Sequence Transformer giải mã trực tiếp ảnh công thức 2D sang LaTeX (không cắt dán hộp bao, không heuristic phân tách nét). Đo lường kích thước ONNX (< 30 MB), độ trễ CPU (< 300 ms / toàn bộ công thức), RAM Working Set (< 150 MB), nhận diện ma trận 2D & vector cột thực tế từ tập MathWriting, và kiểm thử trên ảnh công thức thực tế của người dùng ($\int 2x dx$).
- **Báo cáo xuất ra:** [`reports/e2e_math_benchmark_report.txt`](./reports/e2e_math_benchmark_report.txt).
