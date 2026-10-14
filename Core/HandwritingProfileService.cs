using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HapiCopi
{
    /// <summary>
    /// Đặc trưng cấu trúc topo hình thái học bất biến (Euler loops, crossings, cusps, endpoints).
    /// </summary>
    public class TopologicalProfile
    {
        public int Loops { get; set; } = 0;       // Số vòng kín Euler (0, 1, 2)
        public int Crossings { get; set; } = 0;   // Số điểm giao cắt giữa các nét (X/T junction)
        public int Cusps { get; set; } = 0;       // Số góc gập nhọn đột ngột (>110 độ)
        public int Endpoints { get; set; } = 0;   // Số đầu mút tự do
    }

    /// <summary>
    /// Bản ghi một mẫu nét chữ viết tay cá nhân của người dùng.
    /// </summary>
    public class HandwritingPrototype
    {
        public string Label { get; set; } = "";
        public float[] Features { get; set; } = Array.Empty<float>();
        public int StrokeCount { get; set; } = 1;
        public float AspectRatio { get; set; } = 1f;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int HitCount { get; set; } = 1;
        public int MissCount { get; set; } = 0;
        public bool IsUserTrained { get; set; } = false;
        public bool IsAugmented { get; set; } = false;
        public string Source { get; set; } = "base"; // "base", "user", "distilled", "user_aug", "distilled_aug"

        // Thuộc tính Topo bất biến
        public int LoopCount { get; set; } = 0;
        public int CrossingCount { get; set; } = 0;
        public int CuspCount { get; set; } = 0;
    }

    /// <summary>
    /// Các tham số cấu trúc không gian 2D được hiệu chuẩn (calibrated) tự động theo phong cách người dùng.
    /// </summary>
    public class SpatialCalibration
    {
        public float ExponentMaxHeightRatio { get; set; } = 0.85f;
        public float ExponentMinVerticalShift { get; set; } = 0.18f;
        public float SubscriptMaxHeightRatio { get; set; } = 0.85f;
        public float SubscriptMinVerticalShift { get; set; } = 0.22f;
        public float VectorArrowMaxHeightRatio { get; set; } = 0.42f;
        public float VectorArrowMinAspect { get; set; } = 1.6f;
        public float MatrixBracketMinHeightRatio { get; set; } = 1.4f;
    }

    /// <summary>
    /// Dịch vụ lưu trữ, chắt lọc tri thức (Distillation) và học nét chữ viết tay cá nhân hóa.
    /// Tự động sinh biến thể hình học (Augmentation) và học cấu trúc quan hệ 2D (Spatial Grammar).
    /// </summary>
    public static class HandwritingProfileService
    {
        private static readonly object _lock = new object();
        private static List<HandwritingPrototype> _prototypes = new List<HandwritingPrototype>();
        private static bool _isLoaded = false;

        public static SpatialCalibration Spatial { get; private set; } = new SpatialCalibration();
        public static event Action<int, string>? OnProfileUpdated;

        public static string ProfileDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HapiCopi", "profiles");

        public static string ProfileFilePath =>
            Path.Combine(ProfileDirectory, "handwriting_profile.json");

        public static string SpatialCalibrationFilePath =>
            Path.Combine(ProfileDirectory, "spatial_calibration.json");

        static HandwritingProfileService()
        {
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_isLoaded) return;
                _isLoaded = true;

                try
                {
                    if (File.Exists(ProfileFilePath))
                    {
                        string json = File.ReadAllText(ProfileFilePath);
                        var loaded = JsonSerializer.Deserialize<List<HandwritingPrototype>>(json);
                        if (loaded != null && loaded.Count > 0)
                        {
                            _prototypes = loaded;
                            Program.Log($"[HandwritingProfile] Đã nạp {_prototypes.Count} mẫu nét chữ cá nhân từ {ProfileFilePath}.");
                        }
                    }

                    if (File.Exists(SpatialCalibrationFilePath))
                    {
                        string calibJson = File.ReadAllText(SpatialCalibrationFilePath);
                        var calibLoaded = JsonSerializer.Deserialize<SpatialCalibration>(calibJson);
                        if (calibLoaded != null)
                        {
                            Spatial = calibLoaded;
                            Program.Log($"[HandwritingProfile] Đã nạp cấu hình không gian 2D từ {SpatialCalibrationFilePath}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"[HandwritingProfile] Lỗi nạp profile: {ex.Message}");
                }

                if (_prototypes.Count == 0)
                {
                    LoadDefaultBasePrototypes();
                }
                else
                {
                    // Tự động dọn sạch các mẫu học vẹt nguyên khối cũ (như "14 + 32", "18", "213+", "35"...)
                    int removedComposites = _prototypes.RemoveAll(p => !IsValidAtomicLabel(p.Label));
                    if (removedComposites > 0)
                    {
                        Program.Log($"[HandwritingProfile] Đã loại bỏ {removedComposites} mẫu học vẹt nguyên khối không hợp lệ khỏi profile.");
                        SaveProfile();
                    }

                    // Đảm bảo các mẫu cơ sở hoặc mẫu đã nạp đều có Topo hợp lệ nếu chưa được gán
                    foreach (var p in _prototypes)
                    {
                        if (p.LoopCount == 0 && p.CrossingCount == 0 && p.CuspCount == 0)
                        {
                            if (p.Label == "8") p.LoopCount = 2;
                            else if (p.Label is "0" or "6" or "9" or "b" or "d" or "p" or "q" or "o") p.LoopCount = 1;
                            else if (p.Label is "+" or "\\times") p.CrossingCount = 1;
                            else if (p.Label is "v" or "V" or "7") p.CuspCount = 1;
                        }
                    }
                }
            }
        }

        public static void SaveProfile()
        {
            lock (_lock)
            {
                try
                {
                    if (_isTestingMode)
                    {
                        Program.Log("[HandwritingProfile] Đang ở chế độ Test Benchmark in-memory - Bỏ qua ghi đĩa để bảo vệ dữ liệu người dùng.");
                        return;
                    }

                    if (!Directory.Exists(ProfileDirectory))
                    {
                        Directory.CreateDirectory(ProfileDirectory);
                    }

                    string json = JsonSerializer.Serialize(_prototypes, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ProfileFilePath, json);

                    string calibJson = JsonSerializer.Serialize(Spatial, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SpatialCalibrationFilePath, calibJson);

                    Program.Log($"[HandwritingProfile] Đã lưu {_prototypes.Count} mẫu nét chữ và cấu hình không gian vào {ProfileDirectory}");
                }
                catch (Exception ex)
                {
                    Program.Log($"[HandwritingProfile] Lỗi lưu profile: {ex.Message}");
                }
            }
        }

        private static bool _isTestingMode = false;

        /// <summary>
        /// Khởi tạo phiên test in-memory cho benchmark: nạp bộ mẫu chuẩn và ngăn không cho ghi đè lên file đĩa người dùng.
        /// </summary>
        public static void ResetInMemoryForTesting()
        {
            lock (_lock)
            {
                _isTestingMode = true;
                _prototypes.Clear();
                Spatial = new SpatialCalibration();
                LoadDefaultBasePrototypes();
                Program.Log("[HandwritingProfile] Đã kích hoạt môi trường Test Benchmark cách ly trong RAM (bảo vệ file người dùng).");
            }
        }

        /// <summary>
        /// Kết thúc test benchmark và nạp lại nguyên vẹn hồ sơ thực tế của người dùng từ ổ đĩa.
        /// </summary>
        public static void ReloadFromDisk()
        {
            lock (_lock)
            {
                _isTestingMode = false;
                _isLoaded = false;
                _prototypes.Clear();
                EnsureLoaded();
                Program.Log($"[HandwritingProfile] Đã khôi phục hồ sơ người dùng thực tế từ đĩa ({_prototypes.Count} mẫu).");
            }
        }

        /// <summary>
        /// Lấy thống kê số lượng mẫu phân bổ theo từng ký tự (Label Breakdown).
        /// </summary>
        public static Dictionary<string, int> GetProfileSummary()
        {
            lock (_lock)
            {
                return _prototypes
                    .GroupBy(p => p.Label)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }

        /// <summary>
        /// Lấy số lượng mẫu nét đã lưu của một ký tự cụ thể.
        /// </summary>
        public static int GetSampleCountForLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return 0;
            lock (_lock)
            {
                return _prototypes.Count(p => p.Label.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Xóa sạch các mẫu nét của một ký tự cụ thể để người dùng có thể dạy lại từ đầu.
        /// </summary>
        public static int DeleteSamplesForLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return 0;
            lock (_lock)
            {
                string clean = label.Trim();
                int removed = _prototypes.RemoveAll(p => p.Label.Equals(clean, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    SaveProfile();
                    OnProfileUpdated?.Invoke(_prototypes.Count, clean);
                    Program.Log($"[HandwritingProfile] Đã xóa {removed} mẫu nét của ký tự '{clean}'. Còn lại {_prototypes.Count} mẫu.");
                }
                return removed;
            }
        }

        public static int GetSampleCount()
        {
            lock (_lock)
            {
                return _prototypes.Count;
            }
        }

        public static void ClearProfile()
        {
            lock (_lock)
            {
                _prototypes.Clear();
                Spatial = new SpatialCalibration();
                LoadDefaultBasePrototypes();
                SaveProfile();
            }
        }

        /// <summary>
        /// Tách nhãn công thức thành danh sách các token ký tự/ký hiệu toán học.
        /// Ví dụ: "347" -> ["3", "4", "7"], "x+y" -> ["x", "+", "y"], "\alpha + 2" -> ["\alpha", "+", "2"].
        /// </summary>
        public static List<string> TokenizeLabel(string label)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(label)) return tokens;

            string s = label.Trim();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) continue;

                if (c == '\\')
                {
                    int start = i;
                    i++;
                    while (i < s.Length && char.IsLetter(s[i])) i++;
                    tokens.Add(s.Substring(start, i - start));
                    i--;
                }
                else if (c != '{' && c != '}' && c != '^' && c != '_')
                {
                    tokens.Add(c.ToString());
                }
            }
            return tokens;
        }

        /// <summary>
        /// Kiểm tra xem nhãn có phải là một ký hiệu toán học nguyên tử hợp lệ (Atomic Symbol) hay không.
        /// Chống hiện tượng học vẹt cả cụm đa ký tự (như "14 + 32", "18", "abc", ma trận nguyên khối).
        /// </summary>
        public static bool IsValidAtomicLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            string s = label.Trim();
            if (s.Length == 1) return true;
            if (s.StartsWith("\\") && 
                !s.Contains("{") && !s.Contains("}") && 
                !s.Contains(" ") && !s.Contains("\\\\") && 
                !s.Contains("+") && !s.Contains("-") && !s.Contains("*"))
            {
                return true;
            }
            return false;
        }

        private static bool StrokesIntersect(List<List<PointF>> cluster, List<PointF> stroke)
        {
            foreach (var s in cluster)
            {
                for (int i = 0; i < s.Count - 1; i++)
                {
                    for (int j = 0; j < stroke.Count - 1; j++)
                    {
                        if (DoSegmentsIntersect(s[i], s[i + 1], stroke[j], stroke[j + 1]))
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool StrokesTouch(List<List<PointF>> cluster, List<PointF> stroke, float threshold = 5.0f)
        {
            float threshSq = threshold * threshold;
            foreach (var s in cluster)
            {
                foreach (var p1 in s)
                {
                    foreach (var p2 in stroke)
                    {
                        float dx = p1.X - p2.X;
                        float dy = p1.Y - p2.Y;
                        if (dx * dx + dy * dy <= threshSq)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Gom cụm các nét vẽ theo vị trí không gian (từ trái sang phải) kèm khung bao chữ nhật.
        /// Bóc tách độc lập: Hai nét chỉ thuộc cùng một ký tự nếu giao nhau, chạm nhau, hoặc chồng lấn thẳng đứng.
        /// Tuyệt đối KHÔNG gộp nếu đứng cạnh nhau (gapX > 1.5px) mà không giao/chạm (như số 1 và số 8 trong 18).
        /// </summary>
        public static List<(List<List<PointF>> cluster, RectangleF box)> ClusterStrokesWithBoxes(List<List<PointF>> strokes)
        {
            var result = new List<(List<List<PointF>> cluster, RectangleF box)>();
            if (strokes == null || strokes.Count == 0) return result;

            var strokeBoxes = strokes.Select((s, idx) =>
            {
                float minX = s.Min(p => p.X), maxX = s.Max(p => p.X);
                float minY = s.Min(p => p.Y), maxY = s.Max(p => p.Y);
                return new
                {
                    Index = idx,
                    Stroke = s,
                    MinX = minX, MaxX = maxX,
                    MinY = minY, MaxY = maxY,
                    W = Math.Max(1f, maxX - minX),
                    H = Math.Max(1f, maxY - minY)
                };
            }).ToList();

            var sorted = strokeBoxes.OrderBy(s => s.MinX).ToList();
            var currentCluster = new List<List<PointF>> { sorted[0].Stroke };
            var curMinX = sorted[0].MinX;
            var curMaxX = sorted[0].MaxX;
            var curMinY = sorted[0].MinY;
            var curMaxY = sorted[0].MaxY;

            for (int i = 1; i < sorted.Count; i++)
            {
                var cur = sorted[i];
                float overlapX = Math.Min(curMaxX, cur.MaxX) - Math.Max(curMinX, cur.MinX);
                float gapX = cur.MinX - curMaxX;
                float avgH = ((curMaxY - curMinY) + cur.H) / 2f;

                // Kiểm tra độ gần về phương dọc (Y) để tránh gộp các dòng/hàng khác nhau trong ma trận
                float overlapY = Math.Min(curMaxY, cur.MaxY) - Math.Max(curMinY, cur.MinY);
                float gapY = Math.Max(0f, Math.Max(curMinY, cur.MinY) - Math.Min(curMaxY, cur.MaxY));
                bool nearY = overlapY > 0 || gapY < avgH * 0.60f;

                // Kiểm tra xem nét cur có cắt (intersect) hoặc chạm (touch) nét nào trong currentCluster không
                bool doIntersect = StrokesIntersect(currentCluster, cur.Stroke);
                bool doTouch = StrokesTouch(currentCluster, cur.Stroke, 5.0f);

                // Chồng lấn phương ngang đáng kể (như dấu = hai nét song song, dấu : hai chấm, hoặc dấu gạch ngang số 7)
                float minW = Math.Min(curMaxX - curMinX, cur.W);
                bool verticalStack = overlapX >= Math.Max(4f, minW * 0.40f) && nearY;

                // Chỉ thuộc cùng ký tự nếu: Giao nhau, Chạm nhau, hoặc Chồng lấn thẳng đứng đáng kể.
                // TUYỆT ĐỐI KHÔNG GỘP nếu tách biệt về hoành độ (gapX > 1.5px) mà không cắt/chạm!
                bool belongs = (doIntersect || doTouch || verticalStack) && currentCluster.Count < 4;

                if (belongs)
                {
                    currentCluster.Add(cur.Stroke);
                    curMinX = Math.Min(curMinX, cur.MinX);
                    curMaxX = Math.Max(curMaxX, cur.MaxX);
                    curMinY = Math.Min(curMinY, cur.MinY);
                    curMaxY = Math.Max(curMaxY, cur.MaxY);
                }
                else
                {
                    result.Add((currentCluster, new RectangleF(curMinX, curMinY, curMaxX - curMinX, curMaxY - curMinY)));
                    currentCluster = new List<List<PointF>> { cur.Stroke };
                    curMinX = cur.MinX; curMaxX = cur.MaxX;
                    curMinY = cur.MinY; curMaxY = cur.MaxY;
                }
            }
            if (currentCluster.Count > 0)
            {
                result.Add((currentCluster, new RectangleF(curMinX, curMinY, curMaxX - curMinX, curMaxY - curMinY)));
            }

            return result;
        }

        public static List<List<List<PointF>>> ClusterStrokes(List<List<PointF>> strokes)
        {
            return ClusterStrokesWithBoxes(strokes).Select(x => x.cluster).ToList();
        }

        /// <summary>
        /// Thuật toán sinh biến thể hình học tự động (Data Augmentation) từ một nét vẽ.
        /// Tạo 7 biến thể thực tế: Nghiêng trái/phải, Xoay nhẹ ±5°, Kéo dãn ngang/dọc, và Rung nét vi mô.
        /// </summary>
        public static List<List<List<PointF>>> GenerateStrokeAugmentations(List<List<PointF>> cluster)
        {
            var variants = new List<List<List<PointF>>>();
            if (cluster == null || cluster.Count == 0) return variants;

            var allPts = cluster.SelectMany(s => s).ToList();
            if (allPts.Count < 2) return variants;

            float minX = allPts.Min(p => p.X), maxX = allPts.Max(p => p.X);
            float minY = allPts.Min(p => p.Y), maxY = allPts.Max(p => p.Y);
            float cx = (minX + maxX) / 2f, cy = (minY + maxY) / 2f;

            // 1. Nghiêng phải (+6 độ ~ shear k = +0.10)
            variants.Add(TransformStrokes(cluster, p => new PointF(cx + (p.X - cx) + 0.10f * (cy - p.Y), p.Y)));

            // 2. Nghiêng trái (-6 độ ~ shear k = -0.10)
            variants.Add(TransformStrokes(cluster, p => new PointF(cx + (p.X - cx) - 0.10f * (cy - p.Y), p.Y)));

            // 3. Xoay theo chiều kim đồng hồ (+5 độ)
            float rad1 = 0.087f, cos1 = (float)Math.Cos(rad1), sin1 = (float)Math.Sin(rad1);
            variants.Add(TransformStrokes(cluster, p =>
            {
                float dx = p.X - cx, dy = p.Y - cy;
                return new PointF(cx + dx * cos1 - dy * sin1, cy + dx * sin1 + dy * cos1);
            }));

            // 4. Xoay ngược chiều kim đồng hồ (-5 độ)
            float rad2 = -0.087f, cos2 = (float)Math.Cos(rad2), sin2 = (float)Math.Sin(rad2);
            variants.Add(TransformStrokes(cluster, p =>
            {
                float dx = p.X - cx, dy = p.Y - cy;
                return new PointF(cx + dx * cos2 - dy * sin2, cy + dx * sin2 + dy * cos2);
            }));

            // 5. Kéo dãn ngang (Béo hơn 8%, lùn 4%)
            variants.Add(TransformStrokes(cluster, p => new PointF(cx + (p.X - cx) * 1.08f, cy + (p.Y - cy) * 0.96f)));

            // 6. Kéo dãn dọc (Gầy hơn 8%, cao 6%)
            variants.Add(TransformStrokes(cluster, p => new PointF(cx + (p.X - cx) * 0.92f, cy + (p.Y - cy) * 1.06f)));

            // 7. Chống rung vi mô (Micro-jitter)
            int ptCounter = 0;
            variants.Add(TransformStrokes(cluster, p =>
            {
                float jitterX = (float)Math.Sin(ptCounter * 0.6f) * 1.1f;
                float jitterY = (float)Math.Cos(ptCounter * 0.6f) * 0.9f;
                ptCounter++;
                return new PointF(p.X + jitterX, p.Y + jitterY);
            }));

            return variants;
        }

        private static List<List<PointF>> TransformStrokes(List<List<PointF>> strokes, Func<PointF, PointF> transform)
        {
            var res = new List<List<PointF>>(strokes.Count);
            foreach (var s in strokes)
            {
                var sNew = new List<PointF>(s.Count);
                foreach (var p in s)
                {
                    sNew.Add(transform(p));
                }
                res.Add(sNew);
            }
            return res;
        }

        private static int LearnClusterWithAugmentation(string label, List<List<PointF>> cluster, bool isDistilled = false)
        {
            if (string.IsNullOrWhiteSpace(label) || cluster == null || cluster.Count == 0) return 0;

            int count = 0;
            // 1. Học mẫu gốc
            if (LearnSinglePrototype(label, cluster, isDistilled: isDistilled, isAugmented: false))
            {
                count++;
            }

            // 2. Tự động sinh 7 biến thể hình học và nạp vào profile
            var variants = GenerateStrokeAugmentations(cluster);
            foreach (var variant in variants)
            {
                if (LearnSinglePrototype(label, variant, isDistilled: isDistilled, isAugmented: true))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Bộ máy Chắt lọc tri thức (Knowledge Distillation) & Sinh biến thể tự động (Data Augmentation).
        /// Tự động học ngầm khi AI Cloud nhận diện thành công hoặc khi người dùng sửa lỗi.
        /// Đồng thời cân chỉnh các tham số không gian 2D (mũ, phân số, vector, ma trận).
        /// </summary>
        public static int DistillAndAugmentSample(string rawLatex, List<List<PointF>> strokes, bool isFromCloud = false)
        {
            if (string.IsNullOrWhiteSpace(rawLatex) || strokes == null || strokes.Count == 0)
                return 0;

            EnsureLoaded();

            string cleanLabel = rawLatex.Trim();
            if (cleanLabel.StartsWith("$") && cleanLabel.EndsWith("$") && cleanLabel.Length >= 2)
            {
                cleanLabel = cleanLabel.Substring(1, cleanLabel.Length - 2).Trim();
            }

            int learnedCount = 0;
            bool spatialChanged = false;

            // 1. Nhận diện & bóc tách Vector: \vec{v} hoặc \overrightarrow{AB}
            if ((cleanLabel.StartsWith("\\vec{") || cleanLabel.StartsWith("\\overrightarrow{")) && cleanLabel.EndsWith("}"))
            {
                int start = cleanLabel.IndexOf('{') + 1;
                string innerSymbol = cleanLabel.Substring(start, cleanLabel.Length - start - 1).Trim();

                if (strokes.Count >= 2 && !string.IsNullOrEmpty(innerSymbol))
                {
                    var sortedByY = strokes.OrderBy(s => s.Min(p => p.Y)).ToList();
                    var arrowStroke = sortedByY.First();
                    var baseStrokes = sortedByY.Skip(1).ToList();

                    float arrowH = Math.Max(1f, arrowStroke.Max(p => p.Y) - arrowStroke.Min(p => p.Y));
                    float baseH = Math.Max(1f, baseStrokes.SelectMany(s => s).Max(p => p.Y) - baseStrokes.SelectMany(s => s).Min(p => p.Y));

                    Spatial.VectorArrowMaxHeightRatio = Math.Min(0.55f, Math.Max(0.20f, (arrowH / baseH) * 1.15f));
                    spatialChanged = true;

                    learnedCount += LearnClusterWithAugmentation(innerSymbol, baseStrokes, isFromCloud);
                }
            }
            // 2. Nhận diện & bóc tách Phân số: \frac{A}{B}
            else if (cleanLabel.StartsWith("\\frac{") && cleanLabel.Contains("}{") && cleanLabel.EndsWith("}"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cleanLabel, @"^\\frac\{([^{}]+)\}\{([^{}]+)\}$");
                if (match.Success)
                {
                    string numLabel = match.Groups[1].Value.Trim();
                    string denLabel = match.Groups[2].Value.Trim();

                    var strokeBoxes = strokes.Select(s => new
                    {
                        Stroke = s,
                        MinX = s.Min(p => p.X), MaxX = s.Max(p => p.X),
                        MinY = s.Min(p => p.Y), MaxY = s.Max(p => p.Y),
                        W = s.Max(p => p.X) - s.Min(p => p.X),
                        H = s.Max(p => p.Y) - s.Min(p => p.Y)
                    }).ToList();

                    var barCandidate = strokeBoxes
                        .Where(s => s.W > 20 && s.W > s.H * 2.2f)
                        .OrderByDescending(s => s.W)
                        .FirstOrDefault();

                    if (barCandidate != null)
                    {
                        var numStrokes = strokeBoxes.Where(s => s != barCandidate && s.MaxY <= barCandidate.MinY + 6).Select(s => s.Stroke).ToList();
                        var denStrokes = strokeBoxes.Where(s => s != barCandidate && s.MinY >= barCandidate.MaxY - 6).Select(s => s.Stroke).ToList();

                        if (numStrokes.Count > 0)
                        {
                            learnedCount += DistillAndAugmentSample(numLabel, numStrokes, isFromCloud);
                        }
                        if (denStrokes.Count > 0)
                        {
                            learnedCount += DistillAndAugmentSample(denLabel, denStrokes, isFromCloud);
                        }
                    }
                }
            }
            // 3. Nhận diện & bóc tách Số mũ (Superscript): X^{Y} hoặc X^Y
            else if (cleanLabel.Contains("^") && !cleanLabel.Contains("\\"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cleanLabel, @"^([a-zA-Z0-9]+)\^\{?([a-zA-Z0-9]+)\}?$");
                if (match.Success)
                {
                    string baseLabel = match.Groups[1].Value.Trim();
                    string expLabel = match.Groups[2].Value.Trim();

                    var clustered = ClusterStrokesWithBoxes(strokes);
                    if (clustered.Count == 2)
                    {
                        var baseBox = clustered[0].box;
                        var expBox = clustered[1].box;

                        float ratio = expBox.Height / Math.Max(1f, baseBox.Height);
                        float shift = ((baseBox.Y + baseBox.Height / 2f) - (expBox.Y + expBox.Height / 2f)) / Math.Max(1f, baseBox.Height);
                        Spatial.ExponentMaxHeightRatio = Math.Min(0.95f, Math.Max(0.40f, ratio * 1.15f));
                        Spatial.ExponentMinVerticalShift = Math.Min(0.45f, Math.Max(0.10f, shift * 0.85f));
                        spatialChanged = true;

                        learnedCount += LearnClusterWithAugmentation(baseLabel, clustered[0].cluster, isFromCloud);
                        learnedCount += LearnClusterWithAugmentation(expLabel, clustered[1].cluster, isFromCloud);
                    }
                }
            }

            // 4. Bóc tách dãy ký tự tuyến tính (Tokenize): ví dụ "347" -> "3", "4", "7"
            var tokens = TokenizeLabel(cleanLabel);
            var clustersWithBoxes = ClusterStrokesWithBoxes(strokes);

            if (tokens.Count > 1 && clustersWithBoxes.Count == tokens.Count)
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    learnedCount += LearnClusterWithAugmentation(tokens[i], clustersWithBoxes[i].cluster, isFromCloud);
                }
            }
            else if (tokens.Count == 1 && clustersWithBoxes.Count == 1)
            {
                learnedCount += LearnClusterWithAugmentation(tokens[0], clustersWithBoxes[0].cluster, isFromCloud);
            }
            else if (IsValidAtomicLabel(cleanLabel))
            {
                // Fallback chỉ chạy khi không bóc tách được cụm đơn lẻ nhưng cả biểu thức là 1 ký hiệu nguyên tử
                learnedCount += LearnSinglePrototype(cleanLabel, strokes, isDistilled: isFromCloud, isAugmented: false) ? 1 : 0;
            }

            if (learnedCount > 0 || spatialChanged)
            {
                SaveProfile();
                OnProfileUpdated?.Invoke(GetSampleCount(), cleanLabel);
            }

            return learnedCount;
        }

        public static bool LearnSample(string label, List<List<PointF>> strokes)
        {
            return DistillAndAugmentSample(label, strokes, isFromCloud: false) > 0;
        }

        public const int MaxPrototypesPerLabel = 14;
        public const float MergeThresholdDistance = 0.38f;

        private static bool LearnSinglePrototype(string label, List<List<PointF>> strokes, bool isDistilled = false, bool isAugmented = false)
        {
            if (string.IsNullOrWhiteSpace(label) || strokes == null || strokes.Count == 0)
                return false;

            string cleanLabel = label.Trim();
            if (!IsValidAtomicLabel(cleanLabel))
            {
                Program.Log($"[HandwritingProfile] Từ chối lưu nhãn không phải nguyên tử: '{cleanLabel}' để chống học vẹt cả cụm.");
                return false;
            }

            float[] features = ExtractStrokeFeatures(strokes, out float aspect);
            if (features.Length == 0) return false;

            var topo = ExtractTopologicalProfile(strokes);

            lock (_lock)
            {
                // QUY TẮC NGHIỆP VỤ 1: Kiểm tra xem nét mới có gần như trùng khớp hoàn toàn với một mẫu ĐÃ CÓ CỦA RIÊNG KÝ TỰ NÀY không (dist < 0.38f)
                var existing = _prototypes.FirstOrDefault(p =>
                    p.Label.Equals(cleanLabel, StringComparison.OrdinalIgnoreCase) &&
                    ComputeDistance(p.Features, features) < MergeThresholdDistance);

                if (existing != null)
                {
                    // Nét tương đồng cao -> Củng cố mẫu hiện có thay vì tạo rác trùng lặp
                    existing.HitCount++;
                    existing.MissCount = Math.Max(0, existing.MissCount - 1);
                    existing.IsUserTrained = true;
                    existing.LoopCount = topo.Loops;
                    existing.CrossingCount = topo.Crossings;
                    existing.CuspCount = topo.Cusps;
                    if (!isAugmented)
                    {
                        existing.Source = isDistilled ? "distilled" : "user";
                    }
                    // Cập nhật trung bình động nhẹ (80% nét cũ, 20% nét mới) để giữ ổn định
                    for (int i = 0; i < features.Length; i++)
                    {
                        existing.Features[i] = (existing.Features[i] * 0.80f) + (features[i] * 0.20f);
                    }
                    return true;
                }

                // QUY TẮC NGHIỆP VỤ 2: Nét vẽ là một biến thể mới (dist >= 0.38f).
                // Kiểm tra hạn ngạch mẫu (Quota) CỦA RIÊNG KÝ TỰ NÀY.
                var labelProtos = _prototypes
                    .Where(p => p.Label.Equals(cleanLabel, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (labelProtos.Count >= MaxPrototypesPerLabel)
                {
                    // Nếu là nét sinh biến thể (augmented) và đã đủ quota thì dừng lại
                    if (isAugmented)
                    {
                        return false;
                    }

                    // Nếu là nét do người dùng trực tiếp dạy: thay thế 1 mẫu cũ nhất/ít tin cậy nhất CỦA RIÊNG KÝ TỰ NÀY (FIFO / Low-Hit).
                    // TUYỆT ĐỐI KHÔNG ĐƯỢC XÓA MẪU CỦA KÝ TỰ KHÁC!
                    var candidateToRemove = labelProtos
                        .OrderBy(p => p.HitCount - p.MissCount)
                        .ThenBy(p => p.CreatedAt)
                        .FirstOrDefault();

                    if (candidateToRemove != null)
                    {
                        _prototypes.Remove(candidateToRemove);
                    }
                }

                // Thêm mẫu biến thể mới cho ký tự này
                _prototypes.Add(new HandwritingPrototype
                {
                    Label = cleanLabel,
                    Features = features,
                    StrokeCount = strokes.Count,
                    AspectRatio = aspect,
                    CreatedAt = DateTime.Now,
                    HitCount = isAugmented ? 1 : 2,
                    MissCount = 0,
                    IsUserTrained = true,
                    IsAugmented = isAugmented,
                    Source = isAugmented ? (isDistilled ? "distilled_aug" : "user_aug") : (isDistilled ? "distilled" : "user"),
                    LoopCount = topo.Loops,
                    CrossingCount = topo.Crossings,
                    CuspCount = topo.Cusps
                });

                return true;
            }
        }

        /// <summary>
        /// Cơ chế Phạt phản hồi tiêu cực (Contrastive Negative Penalty & Prototype Repulsion):
        /// Khi người dùng hiệu chỉnh kết quả sai, mẫu dự đoán sai bị phạt:
        /// 1. Tăng MissCount, giảm HitCount
        /// 2. Đẩy lùi nhẹ vector đặc trưng (Feature Repulsion) xa khỏi nét vẽ bị nhầm:
        ///    F_wrong = F_wrong + alpha * (F_wrong - F_query)
        /// NGUYÊN TẮC BẤT DI BẤT DỊCH: KHÔNG BAO GIỜ XÓA (PRUNE) DỮ LIỆU CỦA NGƯỜI DÙNG!
        /// </summary>
        public static int PenalizeWrongPrediction(string wrongLabel, List<List<PointF>> strokes)
        {
            if (string.IsNullOrWhiteSpace(wrongLabel) || strokes == null || strokes.Count == 0)
                return 0;

            EnsureLoaded();

            float[] queryFeatures = ExtractStrokeFeatures(strokes, out _);
            if (queryFeatures.Length == 0) return 0;

            int penalizedCount = 0;
            lock (_lock)
            {
                // Chỉ tìm các prototype CỦA RIÊNG nhãn wrongLabel
                var matchingProtos = _prototypes
                    .Where(p => p.Label.Equals(wrongLabel.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var p in matchingProtos)
                {
                    float dist = ComputeDistance(p.Features, queryFeatures);
                    if (dist < 2.0f) // Chỉ tác động các mẫu thực sự gần với nét gây nhầm lẫn
                    {
                        p.MissCount++;
                        p.HitCount = Math.Max(0, p.HitCount - 1);

                        // Feature Repulsion nhẹ nhàng: đẩy nhẹ vector của prototype sai ra xa nét vẽ hiện tại
                        const float alpha = 0.08f;
                        for (int i = 0; i < p.Features.Length && i < queryFeatures.Length; i++)
                        {
                            float diff = p.Features[i] - queryFeatures[i];
                            p.Features[i] = Math.Clamp(p.Features[i] + alpha * diff, 0f, 1f);
                        }

                        penalizedCount++;
                    }
                }

                // NGUYÊN TẮC NGHIỆP VỤ BẤT DI BẤT DỊCH:
                // KHÔNG BAO GIỜ XÓA (PRUNE) DỮ LIỆU DO NGƯỜI DÙNG DẠY KHI PHẠT!
                // MissCount đã được tích hợp vào công thức tính khoảng cách weightedDist,
                // tự động đẩy độ ưu tiên của mẫu sai xuống thấp mà không làm mất mẫu cũ.

                if (penalizedCount > 0)
                {
                    SaveProfile();
                    Program.Log($"[HandwritingProfile] Đã áp dụng cơ chế Phạt (Contrastive Repulsion) cho nhãn '{wrongLabel}' ({penalizedCount} prototype bị điều chỉnh vector, không xóa mẫu).");
                }
            }

            return penalizedCount;
        }

        /// <summary>
        /// Nhận diện một cụm nét vẽ và trả về danh sách ứng viên Top-K (Candidate Ranking).
        /// </summary>
        public static List<(string label, float confidence)> ClassifyClusterWithCandidates(List<List<PointF>> strokes, int topK = 3)
        {
            if (strokes == null || strokes.Count == 0)
                return new List<(string, float)>();

            float[] queryFeatures = ExtractStrokeFeatures(strokes, out float queryAspect);
            if (queryFeatures.Length == 0)
                return new List<(string, float)>();

            lock (_lock)
            {
                if (_prototypes.Count == 0)
                {
                    if (OnnxMathVisionEngine.IsModelLoaded)
                    {
                        return OnnxMathVisionEngine.Predict(strokes, topK);
                    }
                    return new List<(string, float)>();
                }

                var scoredProtos = new List<(HandwritingPrototype proto, float weightedDist)>();

                foreach (var p in _prototypes)
                {
                    // Lọc sơ bộ theo số nét (+-1 nét) nếu mẫu có nhiều nét
                    if (Math.Abs(p.StrokeCount - strokes.Count) > 1 && strokes.Count > 1 && p.StrokeCount > 1)
                        continue;

                    float dist = ComputeDistance(p.Features, queryFeatures);

                    // Ưu tiên HitCount, phạt MissCount
                    float weightedDist = dist / (1f + (float)Math.Log(1 + p.HitCount) * 0.18f);
                    weightedDist *= (1f + p.MissCount * 0.25f);

                    // Mức ưu tiên phân tầng:
                    // 1. Mẫu người dùng / distilled gốc: giảm 35% khoảng cách (hệ số 0.65)
                    // 2. Mẫu sinh biến thể (Augmented): giảm 22% khoảng cách (hệ số 0.78)
                    // 3. Mẫu cơ sở mặc định: hệ số 1.0
                    if (p.IsUserTrained)
                    {
                        weightedDist *= p.IsAugmented ? 0.78f : 0.65f;
                    }

                    scoredProtos.Add((p, weightedDist));
                }

                var candidates = new List<(string label, float confidence)>();
                var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in scoredProtos.OrderBy(x => x.weightedDist))
                {
                    if (seenLabels.Add(item.proto.Label))
                    {
                        float confidence = Math.Max(0f, 1f - (item.weightedDist / 6.0f));
                        candidates.Add((item.proto.Label, confidence));
                        if (candidates.Count >= topK) break;
                    }
                }

                // Hòa trộn với dự đoán từ Deep Learning ONNX Engine nếu có
                if (OnnxMathVisionEngine.IsModelLoaded)
                {
                    var onnxCands = OnnxMathVisionEngine.Predict(strokes, topK);
                    if (onnxCands.Count > 0)
                    {
                        candidates = BlendWithOnnxCandidates(candidates, onnxCands, topK);
                    }
                }

                return candidates;
            }
        }

        private static List<(string label, float confidence)> BlendWithOnnxCandidates(
            List<(string label, float confidence)> localCands,
            List<(string label, float confidence)> onnxCands,
            int topK)
        {
            var combined = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            // Kiểm tra xem mẫu Top 1 local có phải do người dùng tự học không
            bool isUserTrainedTop = false;
            if (localCands.Count > 0)
            {
                var topLabel = localCands[0].label;
                isUserTrainedTop = _prototypes.Any(p => p.Label.Equals(topLabel, StringComparison.OrdinalIgnoreCase) && p.IsUserTrained);
            }

            // Trọng số: Mẫu người dùng đã dạy ưu tiên 75%, nếu là mẫu cơ sở chia 50-50 với ONNX
            float wLocal = isUserTrainedTop ? 0.75f : 0.50f;
            float wOnnx = 1.0f - wLocal;

            foreach (var c in localCands)
            {
                combined[c.label] = c.confidence * wLocal;
            }

            foreach (var o in onnxCands)
            {
                if (combined.ContainsKey(o.label))
                {
                    combined[o.label] += o.confidence * wOnnx;
                }
                else
                {
                    combined[o.label] = o.confidence * wOnnx;
                }
            }

            return combined
                .OrderByDescending(kv => kv.Value)
                .Take(topK)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        /// <summary>
        /// Nhận diện một cụm nét vẽ bằng đối chiếu vector với từ điển cá nhân hóa.
        /// </summary>
        public static (string label, float confidence) ClassifyCluster(List<List<PointF>> strokes)
        {
            var candidates = ClassifyClusterWithCandidates(strokes, topK: 1);
            return candidates.Count > 0 ? candidates[0] : ("", 0f);
        }

        /// <summary>
        /// Trích xuất vector đặc trưng chuẩn hóa (64 chiều) từ mảng nét vẽ.
        /// Bao gồm 24 điểm tọa độ x,y resampled chuẩn hóa [0,1] + 16 góc hướng vector tiếp tuyến.
        /// </summary>
        public static float[] ExtractStrokeFeatures(List<List<PointF>> strokes, out float aspectRatio)
        {
            aspectRatio = 1f;
            var allPts = strokes.SelectMany(s => s).ToList();
            if (allPts.Count < 2) return Array.Empty<float>();

            float minX = allPts.Min(p => p.X);
            float maxX = allPts.Max(p => p.X);
            float minY = allPts.Min(p => p.Y);
            float maxY = allPts.Max(p => p.Y);

            float w = Math.Max(1f, maxX - minX);
            float h = Math.Max(1f, maxY - minY);
            aspectRatio = w / h;

            float maxDim = Math.Max(w, h);

            // Resample chuỗi nét về đúng 24 điểm chuẩn hóa
            var resampled = ResamplePolyline(strokes, 24);
            var features = new float[64];

            for (int i = 0; i < 24; i++)
            {
                features[i * 2] = (resampled[i].X - minX) / maxDim;
                features[i * 2 + 1] = (resampled[i].Y - minY) / maxDim;
            }

            // 16 góc vector tiếp tuyến liên tiếp để bắt hướng uốn lượn nét bút
            for (int i = 0; i < 16; i++)
            {
                float dx = resampled[i + 1].X - resampled[i].X;
                float dy = resampled[i + 1].Y - resampled[i].Y;
                float angle = (float)Math.Atan2(dy, dx); // [-PI, PI]
                features[48 + i] = angle / (float)Math.PI; // [-1, 1]
            }

            return features;
        }

        private static List<PointF> ResamplePolyline(List<List<PointF>> strokes, int numPoints)
        {
            var pts = new List<PointF>();
            foreach (var stroke in strokes)
            {
                if (stroke.Count > 0)
                {
                    pts.AddRange(stroke);
                }
            }

            if (pts.Count == 0)
            {
                return Enumerable.Repeat(PointF.Empty, numPoints).ToList();
            }

            float totalLen = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                totalLen += Distance(pts[i - 1], pts[i]);
            }

            if (totalLen <= 0f)
            {
                return Enumerable.Repeat(pts[0], numPoints).ToList();
            }

            float interval = totalLen / (numPoints - 1);
            float currentDist = 0f;
            var result = new List<PointF> { pts[0] };
            var workingPts = new List<PointF>(pts);

            for (int i = 1; i < workingPts.Count; i++)
            {
                float d = Distance(workingPts[i - 1], workingPts[i]);
                if (currentDist + d >= interval && d > 0f)
                {
                    float qx = workingPts[i - 1].X + ((interval - currentDist) / d) * (workingPts[i].X - workingPts[i - 1].X);
                    float qy = workingPts[i - 1].Y + ((interval - currentDist) / d) * (workingPts[i].Y - workingPts[i - 1].Y);
                    var q = new PointF(qx, qy);
                    result.Add(q);
                    workingPts.Insert(i, q);
                    currentDist = 0f;
                }
                else
                {
                    currentDist += d;
                }
            }

            while (result.Count < numPoints)
            {
                result.Add(workingPts.Last());
            }

            return result;
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float ComputeDistance(float[] f1, float[] f2)
        {
            int len = Math.Min(f1.Length, f2.Length);
            float sum = 0f;
            for (int i = 0; i < len; i++)
            {
                float diff = f1[i] - f2[i];
                sum += diff * diff;
            }
            return (float)Math.Sqrt(sum);
        }

        private static bool DoSegmentsIntersect(PointF p1, PointF p2, PointF p3, PointF p4)
        {
            float Ccw(PointF a, PointF b, PointF c) =>
                (c.Y - a.Y) * (b.X - a.X) - (b.Y - a.Y) * (c.X - a.X);

            float d1 = Ccw(p3, p4, p1);
            float d2 = Ccw(p3, p4, p2);
            float d3 = Ccw(p1, p2, p3);
            float d4 = Ccw(p1, p2, p4);

            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                   ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>
        /// Trích xuất đặc trưng hình thái học Topo bất biến (Euler loop, crossings, cusps).
        /// </summary>
        public static TopologicalProfile ExtractTopologicalProfile(List<List<PointF>> strokes)
        {
            var topo = new TopologicalProfile();
            if (strokes == null || strokes.Count == 0) return topo;

            var allPts = strokes.SelectMany(s => s).ToList();
            if (allPts.Count < 2) return topo;

            float minX = allPts.Min(p => p.X);
            float maxX = allPts.Max(p => p.X);
            float minY = allPts.Min(p => p.Y);
            float maxY = allPts.Max(p => p.Y);
            float w = Math.Max(1f, maxX - minX);
            float h = Math.Max(1f, maxY - minY);
            float maxDim = Math.Max(w, h);
            float diag = (float)Math.Sqrt(w * w + h * h);

            int loopCount = 0;
            int cuspCount = 0;

            // 1. Phân tích từng nét: vòng kín khép đuôi, tự cắt nét, và góc nhọn
            foreach (var stroke in strokes)
            {
                if (stroke.Count < 3) continue;

                // 1.1. Khép đầu đuôi (Closure Loop): điểm đầu và cuối gặp nhau (ví dụ: 0, O)
                float endGap = Distance(stroke[0], stroke[^1]);
                float strokeLen = 0f;
                for (int i = 1; i < stroke.Count; i++)
                {
                    strokeLen += Distance(stroke[i - 1], stroke[i]);
                }

                bool isClosed = endGap <= maxDim * 0.35f && strokeLen >= maxDim * 1.5f;
                if (isClosed)
                {
                    loopCount++;
                }

                // 1.2. Tự giao cắt (Self-intersection): nét tự cắt thân nó (ví dụ: 8, 6, 9)
                // Nếu nét đã khép kín đầu đuôi (isClosed), giới hạn biên để không tính trùng điểm khép kín
                int maxJ = isClosed ? stroke.Count - 2 : stroke.Count - 1;
                int startI = isClosed ? 1 : 0;
                for (int i = startI; i < maxJ; i++)
                {
                    var p1 = stroke[i];
                    var p2 = stroke[i + 1];

                    for (int j = i + 3; j < maxJ; j++)
                    {
                        var p3 = stroke[j];
                        var p4 = stroke[j + 1];

                        if (DoSegmentsIntersect(p1, p2, p3, p4))
                        {
                            loopCount++;
                            break;
                        }
                    }
                    if (loopCount >= 2) break;
                }

                // 1.3. Góc nhọn (Sharp Cusps): vector đổi hướng gập lại > 105 độ
                int step = Math.Max(1, stroke.Count / 8);
                for (int i = step; i < stroke.Count - step; i += step)
                {
                    var pPrev = stroke[i - step];
                    var pCur = stroke[i];
                    var pNext = stroke[i + step];

                    float ux = pCur.X - pPrev.X, uy = pCur.Y - pPrev.Y;
                    float vx = pNext.X - pCur.X, vy = pNext.Y - pCur.Y;
                    float lenU = (float)Math.Sqrt(ux * ux + uy * uy);
                    float lenV = (float)Math.Sqrt(vx * vx + vy * vy);

                    if (lenU > 0.05f * diag && lenV > 0.05f * diag)
                    {
                        float dot = (ux * vx + uy * vy) / (lenU * lenV);
                        if (dot < -0.25f) // Góc nhọn gập lại
                        {
                            cuspCount++;
                        }
                    }
                }
            }

            // 2. Điểm giao cắt giữa các nét khác nhau (Crossings: ví dụ dấu +, x)
            int crossingCount = 0;
            for (int a = 0; a < strokes.Count; a++)
            {
                var sa = strokes[a];
                for (int b = a + 1; b < strokes.Count; b++)
                {
                    var sb = strokes[b];
                    bool crossed = false;

                    for (int i = 0; i < sa.Count - 1 && !crossed; i++)
                    {
                        for (int j = 0; j < sb.Count - 1; j++)
                        {
                            if (DoSegmentsIntersect(sa[i], sa[i + 1], sb[j], sb[j + 1]))
                            {
                                crossingCount++;
                                crossed = true;
                                break;
                            }
                        }
                    }
                }
            }

            topo.Loops = Math.Min(2, loopCount);
            topo.Crossings = crossingCount;
            topo.Cusps = Math.Min(3, cuspCount);
            topo.Endpoints = strokes.Count * 2;

            return topo;
        }

        private static void LoadDefaultBasePrototypes()
        {
            // Nạp tập các chữ số và ký hiệu toán chuẩn (có các biến thể viết tay phổ biến như số 7 gạch ngang, số 4 một nét...)
            var baseSamples = new List<(string Label, List<List<PointF>> Strokes)>
            {
                ("0", new() { CreateCircleStroke(50, 50, 40) }),
                ("1", new() { new() { new(50, 10), new(50, 90) } }),
                ("1", new() { new() { new(35, 30), new(50, 10), new(50, 90) } }),
                ("2", new() { new() { new(20, 30), new(50, 10), new(80, 30), new(20, 90), new(80, 90) } }),
                ("3", new() { new() { new(20, 20), new(80, 20), new(50, 50), new(80, 70), new(20, 90) } }),
                // 4 (2 nét chuẩn)
                ("4", new() { new() { new(70, 10), new(20, 60), new(90, 60) }, new() { new(70, 30), new(70, 90) } }),
                // 4 (1 nét liền tay)
                ("4", new() { new() { new(70, 10), new(20, 60), new(85, 60), new(70, 30), new(70, 90) } }),
                ("5", new() { new() { new(80, 15), new(30, 15), new(30, 50), new(80, 60), new(80, 85), new(25, 90) } }),
                ("6", new() { new() { new(70, 15), new(30, 50), new(30, 85), new(75, 85), new(75, 55), new(30, 55) } }),
                // 7 (1 nét kiểu Mỹ)
                ("7", new() { new() { new(20, 15), new(85, 15), new(35, 90) } }),
                // 7 (2 nét kiểu Việt Nam / Châu Âu có gạch ngang bụng)
                ("7", new() { new() { new(20, 15), new(85, 15), new(40, 90) }, new() { new(25, 50), new(70, 50) } }),
                ("8", new() { new() { new(50, 10), new(25, 25), new(50, 50), new(75, 75), new(50, 90), new(25, 75), new(50, 50), new(75, 25), new(50, 10) } }),
                ("9", new() { new() { new(75, 50), new(30, 50), new(30, 15), new(75, 15), new(75, 90) } }),
                ("x", new() { new() { new(20, 20), new(80, 80) }, new() { new(80, 20), new(20, 80) } }),
                ("y", new() { new() { new(20, 20), new(50, 55) }, new() { new(80, 20), new(30, 95) } }),
                ("z", new() { new() { new(20, 20), new(80, 20), new(20, 80), new(80, 80) } }),
                ("a", new() { new() { new(75, 30), new(30, 45), new(30, 75), new(75, 75), new(75, 30), new(75, 90) } }),
                ("b", new() { new() { new(25, 10), new(25, 90), new(75, 90), new(75, 55), new(25, 55) } }),
                ("c", new() { new() { new(75, 35), new(35, 35), new(25, 60), new(35, 85), new(75, 85) } }),
                ("u", new() { new() { new(25, 25), new(25, 75), new(50, 90), new(75, 75), new(75, 25) } }),
                ("v", new() { new() { new(25, 25), new(50, 90), new(75, 25) } }),
                ("+", new() { new() { new(50, 20), new(50, 80) }, new() { new(20, 50), new(80, 50) } }),
                ("-", new() { new() { new(20, 50), new(80, 50) } }),
                ("=", new() { new() { new(20, 38), new(80, 38) }, new() { new(20, 62), new(80, 62) } }),
                ("/", new() { new() { new(25, 90), new(75, 10) } }),
                ("(", new() { new() { new(65, 15), new(35, 50), new(65, 85) } }),
                (")", new() { new() { new(35, 15), new(65, 50), new(35, 85) } })
            };

            foreach (var item in baseSamples)
            {
                float[] feat = ExtractStrokeFeatures(item.Strokes, out float asp);
                var topo = ExtractTopologicalProfile(item.Strokes);
                if (feat.Length > 0)
                {
                    _prototypes.Add(new HandwritingPrototype
                    {
                        Label = item.Label,
                        Features = feat,
                        StrokeCount = item.Strokes.Count,
                        AspectRatio = asp,
                        HitCount = 1,
                        IsUserTrained = false,
                        LoopCount = topo.Loops,
                        CrossingCount = topo.Crossings,
                        CuspCount = topo.Cusps
                    });
                }
            }
        }

        private static List<PointF> CreateCircleStroke(float cx, float cy, float r)
        {
            var pts = new List<PointF>();
            for (int a = 0; a <= 360; a += 15)
            {
                float rad = a * (float)Math.PI / 180f;
                pts.Add(new PointF(cx + r * (float)Math.Cos(rad), cy + r * (float)Math.Sin(rad)));
            }
            return pts;
        }
    }
}
