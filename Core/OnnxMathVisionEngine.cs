using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HapiCopi
{
    /// <summary>
    /// Bộ máy suy luận Deep Learning ONNX siêu nhẹ cho nhận diện nét vẽ toán học.
    /// Chạy hoàn toàn offline cục bộ trên máy, tối ưu độ trễ thấp (<15ms) và ít tốn CPU/RAM.
    /// Tự động kết hợp với hồ sơ cá nhân hóa (Personalized Adapter) để phục vụ tốt nhất cho người dùng.
    /// </summary>
    public static class OnnxMathVisionEngine
    {
        private static readonly object _lock = new object();
        private static InferenceSession? _session;
        private static string? _loadedModelPath;

        public static bool IsModelLoaded => _session != null;
        public static string? LoadedModelPath => _loadedModelPath;

        public static string DefaultModelDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HapiCopi", "models");

        public static string AppBaseModelDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

        public static string ModelFileName => "math_rec.onnx";

        /// <summary>
        /// Bảng từ vựng ký hiệu toán học chuẩn hóa (Math Vocabulary) - 82 classes CROHME handwriting dataset.
        /// Bao gồm: 0-9, chữ cái Latin (thường và hoa hình học), hàm giải tích, ký tự Hy Lạp, toán tử và hệ ngoặc.
        /// </summary>
        public static readonly string[] Vocabulary = new[]
        {
            "!", "(", ")", "+", ",", "-", "0", "1", "2", "3",
            "4", "5", "6", "7", "8", "9", "=", "A", "C", "\\Delta",
            "G", "H", "M", "N", "R", "S", "T", "x", "[", "]",
            "\\alpha", "|", "b", "\\beta", "\\cos", "d", "\\div", "e", "\\exists", "f",
            "\\forall", "/", "\\gamma", "\\ge", ">", "i", "\\in", "\\infty", "\\int", "j",
            "k", "l", "\\lambda", "\\dots", "\\le", "\\lim", "\\log", "<", "\\mu", "\\neq",
            "o", "p", "\\phi", "\\pi", "\\pm", "'", "q", "\\to", "\\sigma", "\\sin",
            "\\sqrt", "\\sum", "\\tan", "\\theta", "\\times", "u", "v", "w", "y", "z",
            "\\{", "\\}"
        };

        static OnnxMathVisionEngine()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// Khởi tạo phiên suy luận ONNX (InferenceSession) với cấu hình tiết kiệm tài nguyên.
        /// </summary>
        public static bool EnsureInitialized()
        {
            lock (_lock)
            {
                if (_session != null) return true;

                try
                {
                    string[] searchPaths = new[]
                    {
                        Path.Combine(DefaultModelDir, ModelFileName),
                        Path.Combine(AppBaseModelDir, ModelFileName),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModelFileName),
                        Path.Combine(Directory.GetCurrentDirectory(), "models", ModelFileName),
                        Path.Combine(Directory.GetCurrentDirectory(), ModelFileName)
                    };

                    string? validPath = searchPaths.FirstOrDefault(File.Exists);
                    if (validPath == null)
                    {
                        Console.WriteLine($"[ONNX Engine] Search failed across: {string.Join("; ", searchPaths)}");
                        Program.Log($"[ONNX Engine] Chưa tìm thấy file weights '{ModelFileName}'. Hệ thống sẽ dùng bộ nhận diện đặc trưng cá nhân hóa.");
                        return false;
                    }

                    // Tự động đồng bộ vào thư mục AppData người dùng nếu chưa có
                    try
                    {
                        string localTarget = Path.Combine(DefaultModelDir, ModelFileName);
                        if (!File.Exists(localTarget) && !validPath.Equals(localTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.CreateDirectory(DefaultModelDir);
                            File.Copy(validPath, localTarget, overwrite: false);
                            Program.Log($"[ONNX Engine] Đã đồng bộ weights vào LocalAppData: {localTarget}");
                        }
                    }
                    catch { }

                    var sessionOptions = new SessionOptions
                    {
                        IntraOpNumThreads = 1, // Tiết kiệm CPU, chỉ dùng 1 luồng xử lý nhanh
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
                    };

                    _session = new InferenceSession(validPath, sessionOptions);
                    _loadedModelPath = validPath;
                    Program.Log($"[ONNX Engine] Nạp thành công mô hình Deep Learning ONNX từ: {validPath}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ONNX Engine Exception] {ex.GetType().Name}: {ex.Message}");
                    Program.Log($"[ONNX Engine Lỗi Khởi Tạo] {ex.Message}");
                    _session = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Chuyển đổi các nét vẽ (chuỗi điểm tọa độ) thành ảnh xám chuẩn hóa 32x32 và tạo Float Tensor [1, 1, 32, 32].
        /// Căn giữa theo trọng tâm nét vẽ với lề đệm chống méo (standard margin 4px).
        /// </summary>
        public static DenseTensor<float> RasterizeStrokesToTensor(List<List<PointF>> strokes, int targetSize = 32)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 1, targetSize, targetSize });
            if (strokes == null || strokes.Count == 0) return tensor;

            var allPts = strokes.SelectMany(s => s).ToList();
            if (allPts.Count == 0) return tensor;

            float minX = allPts.Min(p => p.X), maxX = allPts.Max(p => p.X);
            float minY = allPts.Min(p => p.Y), maxY = allPts.Max(p => p.Y);
            float w = Math.Max(1f, maxX - minX);
            float h = Math.Max(1f, maxY - minY);
            float maxDim = Math.Max(w, h);

            int drawArea = targetSize - 8; // Để lề 4px mỗi bên
            float scale = drawArea / maxDim;
            float offsetX = 4f + (drawArea - w * scale) / 2f;
            float offsetY = 4f + (drawArea - h * scale) / 2f;

            using var bmp = new Bitmap(targetSize, targetSize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Black);

                using var pen = new Pen(Color.White, Math.Max(1.8f, targetSize / 14f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };

                foreach (var stroke in strokes)
                {
                    if (stroke.Count == 1)
                    {
                        float x = offsetX + (stroke[0].X - minX) * scale;
                        float y = offsetY + (stroke[0].Y - minY) * scale;
                        g.FillEllipse(Brushes.White, x - 1f, y - 1f, 2f, 2f);
                    }
                    else if (stroke.Count > 1)
                    {
                        var scaledPts = stroke.Select(p => new PointF(
                            offsetX + (p.X - minX) * scale,
                            offsetY + (p.Y - minY) * scale
                        )).ToArray();

                        g.DrawLines(pen, scaledPts);
                    }
                }
            }

            // Đọc pixel nhị phân chuẩn hóa vào Tensor [1, 1, H, W]
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    tensor[0, 0, y, x] = c.R / 255.0f; // [0.0 - 1.0]
                }
            }

            return tensor;
        }

        /// <summary>
        /// Dự đoán nhãn ký tự qua mô hình Deep Learning ONNX kèm độ tin cậy.
        /// </summary>
        public static List<(string label, float confidence)> Predict(List<List<PointF>> strokes, int topK = 3)
        {
            var results = new List<(string label, float confidence)>();
            if (strokes == null || strokes.Count == 0) return results;

            lock (_lock)
            {
                if (_session == null) return results;

                try
                {
                    var tensor = RasterizeStrokesToTensor(strokes, targetSize: 32);
                    string inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? "input";
                    var inputVal = NamedOnnxValue.CreateFromTensor(inputName, tensor);

                    using var outputs = _session.Run(new[] { inputVal });
                    var outputTensor = outputs.FirstOrDefault()?.AsTensor<float>();

                    if (outputTensor != null)
                    {
                        int length = (int)outputTensor.Length;
                        var scores = new List<(string label, float score)>();

                        for (int i = 0; i < length; i++)
                        {
                            string label = i < Vocabulary.Length ? Vocabulary[i] : $"cls_{i}";
                            float val = outputTensor.GetValue(i);
                            scores.Add((label, val));
                        }

                        // Tính Softmax để ra xác suất chuẩn [0, 1]
                        float maxLogit = scores.Max(s => s.score);
                        var expScores = scores.Select(s => (s.label, exp: (float)Math.Exp(s.score - maxLogit))).ToList();
                        float sumExp = expScores.Sum(s => s.exp);

                        results = expScores
                            .Select(s => (s.label, confidence: s.exp / Math.Max(1e-6f, sumExp)))
                            .OrderByDescending(s => s.confidence)
                            .Take(topK)
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"[ONNX Predict Lỗi] {ex.Message}");
                }
            }

            return results;
        }
    }
}
