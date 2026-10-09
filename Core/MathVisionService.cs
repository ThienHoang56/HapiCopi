using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HapiCopi
{
    /// <summary>
    /// Dịch vụ nhận diện công thức toán viết tay (Handwriting Math Recognition).
    /// Hỗ trợ cả 2 chế độ:
    /// 1. Gói Mua (AI Vision Pro - Gemini 2.5 Flash): Nhận diện siêu thông minh mọi công thức phức tạp, tích phân, ma trận, chữ xấu.
    /// 2. Gói Thường (Cục bộ Offline): Nhận diện offline học máy cá nhân hóa (Personalized Adaptation) & ONNX Runtime.
    /// </summary>
    public static class MathVisionService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// Nhận diện nét vẽ sang mã LaTeX.
        /// </summary>
        public static async Task<(bool success, string latex, List<string> candidates, string error, long elapsedMs)> RecognizeHandwrittenMathAsync(
            string base64Png, 
            string engine = "ai",
            List<List<PointF>>? strokes = null)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                if (engine.Equals("local", StringComparison.OrdinalIgnoreCase))
                {
                    // Chế độ cục bộ / Học máy cá nhân hóa
                    return RecognizeOffline(base64Png, strokes, sw);
                }
                else
                {
                    // Chế độ AI Vision Pro
                    if (string.IsNullOrWhiteSpace(base64Png))
                    {
                        return (false, "", new List<string>(), "Ảnh nét vẽ trống.", sw.ElapsedMilliseconds);
                    }

                    string pureBase64 = base64Png;
                    int commaIdx = pureBase64.IndexOf(',');
                    if (commaIdx >= 0)
                    {
                        pureBase64 = pureBase64.Substring(commaIdx + 1);
                    }

                    var aiResult = await RecognizeWithGeminiAsync(pureBase64, sw);
                    if (aiResult.success && !string.IsNullOrEmpty(aiResult.latex) && strokes != null && strokes.Count > 0)
                    {
                        // Kích hoạt Knowledge Distillation & Augmentation chạy ngầm
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                int distilled = HandwritingProfileService.DistillAndAugmentSample(aiResult.latex, strokes, isFromCloud: true);
                                Program.Log($"[MathVision Distill] Đã tự động chắt lọc và sinh biến thể {distilled} mẫu từ AI Vision -> Local Profile.");
                            }
                            catch (Exception ex)
                            {
                                Program.Log($"[MathVision Distill Lỗi] {ex.Message}");
                            }
                        });
                    }
                    return aiResult;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                Program.Log($"[MathVision LỖI] {ex.Message}");
                return (false, "", new List<string>(), $"Lỗi xử lý: {ex.Message}", sw.ElapsedMilliseconds);
            }
        }

        private static async Task<(bool success, string latex, List<string> candidates, string error, long elapsedMs)> RecognizeWithGeminiAsync(string pureBase64, Stopwatch sw)
        {
            string? apiKey = AppConfig.Get("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                sw.Stop();
                return (false, "", new List<string>(), "Chưa cấu hình GEMINI_API_KEY trong file .env hoặc Cài đặt.", sw.ElapsedMilliseconds);
            }

            // Danh sách model ưu tiên
            string[] candidateModels = { "gemini-2.5-flash" };

            string systemPrompt = 
                "You are an expert mathematical handwriting OCR engine. " +
                "Convert the handwritten mathematical equation or expression in this image into standard, valid LaTeX code. " +
                "Output ONLY the pure LaTeX string without markdown code fences (no ```latex), without dollar signs ($), " +
                "and without any explanations, conversational text, or prefixes. " +
                "If multiple formulas or systems are present, format using \\begin{cases}...\\end{cases} or \\begin{aligned}...\\end{aligned}. " +
                "If the image has no math or is empty, output \\empty.";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = systemPrompt },
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = "image/png",
                                    data = pureBase64
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.0,
                    maxOutputTokens = 64,
                    thinkingConfig = new
                    {
                        thinkingBudget = 0
                    }
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);

            Exception? lastEx = null;
            foreach (var model in candidateModels)
            {
                try
                {
                    string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    using var response = await _httpClient.PostAsync(endpoint, content);

                    string respString = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Program.Log($"[MathVision] Model {model} trả về HTTP {response.StatusCode}: {respString}");
                        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            sw.Stop();
                            return (false, "", new List<string>(), "Key Google Gemini miễn phí đã hết lượt gọi trong ngày (HTTP 429: Quota Exceeded). Hãy dùng chế độ Cục bộ hoặc cập nhật API key mới.", sw.ElapsedMilliseconds);
                        }
                        continue;
                    }

                    using var doc = JsonDocument.Parse(respString);
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out var contentElem) &&
                            contentElem.TryGetProperty("parts", out var parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            string rawText = parts[0].GetProperty("text").GetString() ?? "";
                            string cleanLatex = CleanLatexOutput(rawText);

                            sw.Stop();
                            Program.Log($"[MathVision] Nhận diện thành công ({model}) trong {sw.ElapsedMilliseconds}ms: {cleanLatex}");
                            return (true, cleanLatex, new List<string>(), "", sw.ElapsedMilliseconds);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Program.Log($"[MathVision] Thử model {model} thất bại: {ex.Message}");
                }
            }

            sw.Stop();
            string errMsg = lastEx != null ? lastEx.Message : "Không nhận được phản hồi hợp lệ từ máy chủ AI.";
            return (false, "", new List<string>(), errMsg, sw.ElapsedMilliseconds);
        }

        private static (bool success, string latex, List<string> candidates, string error, long elapsedMs) RecognizeOffline(
            string pureBase64, 
            List<List<PointF>>? strokes, 
            Stopwatch sw)
        {
            if (strokes == null || strokes.Count == 0)
            {
                sw.Stop();
                return (false, "", new List<string>(), "Chưa có nét vẽ để nhận diện cục bộ.", sw.ElapsedMilliseconds);
            }

            try
            {
                // 1. Kiểm tra nếu có file ONNX Model Deep Learning đặt sẵn trong app
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "math_rec.onnx");
                if (File.Exists(modelPath))
                {
                    string? onnxResult = RunOnnxInference(pureBase64, modelPath);
                    if (!string.IsNullOrEmpty(onnxResult))
                    {
                        sw.Stop();
                        return (true, onnxResult, new List<string>(), "", sw.ElapsedMilliseconds);
                    }
                }

                // 2. Chạy Engine Học máy cá nhân hóa (Personalized Prototype Cache & Feature Matcher)
                var (result, candidates) = RecognizeWithPersonalizedProfileWithCandidates(strokes);
                sw.Stop();

                if (!string.IsNullOrEmpty(result))
                {
                    return (true, result, candidates, "", sw.ElapsedMilliseconds);
                }

                return (false, "", new List<string>(), "Không phân tích được nét vẽ cục bộ. Bạn có thể nhấn 'Lưu mẫu nét' để dạy app hoặc chuyển sang Đám mây.", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return (false, "", new List<string>(), $"Lỗi phân tích cục bộ: {ex.Message}", sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Phân tích cấu trúc 2D và đối chiếu vector nét vẽ với từ điển cá nhân hóa của người dùng.
        /// </summary>
        public static string RecognizeWithPersonalizedProfile(List<List<PointF>> strokes)
        {
            return RecognizeWithPersonalizedProfileWithCandidates(strokes).latex;
        }

        /// <summary>
        /// Phân tích cấu trúc 2D, nhận diện công thức và trả về danh sách công thức/ký tự thay thế (Candidate Ranking).
        /// </summary>
        public static (string latex, List<string> candidates) RecognizeWithPersonalizedProfileWithCandidates(List<List<PointF>> strokes)
        {
            if (strokes == null || strokes.Count == 0) return ("", new List<string>());

            // Tính toán bounding box cho từng nét
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
                    H = Math.Max(1f, maxY - minY),
                    Cx = (minX + maxX) / 2f,
                    Cy = (minY + maxY) / 2f
                };
            }).ToList();

            // 0. Nhận diện Vector (\vec{v}): Nét mũi tên/ngang nhỏ nằm ngay trên đỉnh của ký tự
            for (int i = 0; i < strokeBoxes.Count; i++)
            {
                var arrow = strokeBoxes[i];
                if (arrow.W >= 10 && arrow.W >= arrow.H * HandwritingProfileService.Spatial.VectorArrowMinAspect)
                {
                    var underStrokes = new List<List<PointF>>();
                    for (int j = 0; j < strokeBoxes.Count; j++)
                    {
                        if (i == j) continue;
                        var other = strokeBoxes[j];
                        float overlapX = Math.Min(arrow.MaxX, other.MaxX) - Math.Max(arrow.MinX, other.MinX);
                        if (other.MinY >= arrow.MaxY - 8 && overlapX > other.W * 0.35f)
                        {
                            underStrokes.Add(other.Stroke);
                        }
                    }

                    if (underStrokes.Count > 0 && underStrokes.Count == strokeBoxes.Count - 1)
                    {
                        string baseSymbol = RecognizeWithPersonalizedProfile(underStrokes);
                        if (!string.IsNullOrEmpty(baseSymbol))
                        {
                            return ($"\\vec{{{baseSymbol}}}", new List<string>());
                        }
                    }
                }
            }

            // 1. Nhận diện Ma trận 2D: Cặp ngoặc lớn bao quanh các phần tử phân dòng và cột
            if (strokeBoxes.Count >= 3)
            {
                var sortedByX = strokeBoxes.OrderBy(s => s.MinX).ToList();
                var leftCand = sortedByX.First();
                var rightCand = sortedByX.Last();
                var innerStrokes = sortedByX.Skip(1).Take(sortedByX.Count - 2).ToList();

                if (innerStrokes.Count >= 2)
                {
                    float avgInnerH = innerStrokes.Average(s => s.H);
                    bool isLeftTall = leftCand.H >= 40 && leftCand.H >= avgInnerH * HandwritingProfileService.Spatial.MatrixBracketMinHeightRatio;
                    bool isRightTall = rightCand.H >= 40 && rightCand.H >= avgInnerH * HandwritingProfileService.Spatial.MatrixBracketMinHeightRatio;

                    if (isLeftTall && isRightTall)
                    {
                        var innerClusters = HandwritingProfileService.ClusterStrokesWithBoxes(innerStrokes.Select(s => s.Stroke).ToList());
                        if (innerClusters.Count >= 2)
                        {
                            // Gom theo dòng (Y-center)
                            var orderedByY = innerClusters.OrderBy(c => c.box.Y + c.box.Height / 2f).ToList();
                            var rows = new List<List<(List<List<PointF>> cluster, RectangleF box)>>();
                            var curRow = new List<(List<List<PointF>> cluster, RectangleF box)> { orderedByY[0] };
                            float curRowCy = orderedByY[0].box.Y + orderedByY[0].box.Height / 2f;

                            for (int r = 1; r < orderedByY.Count; r++)
                            {
                                float cy = orderedByY[r].box.Y + orderedByY[r].box.Height / 2f;
                                if (Math.Abs(cy - curRowCy) < avgInnerH * 0.60f)
                                {
                                    curRow.Add(orderedByY[r]);
                                }
                                else
                                {
                                    rows.Add(curRow);
                                    curRow = new List<(List<List<PointF>> cluster, RectangleF box)> { orderedByY[r] };
                                    curRowCy = cy;
                                }
                            }
                            if (curRow.Count > 0) rows.Add(curRow);

                            if (rows.Count >= 2 && rows.All(r => r.Count >= 1))
                            {
                                var matrixRows = new List<string>();
                                foreach (var row in rows)
                                {
                                    var cols = row.OrderBy(c => c.box.X).Select(c =>
                                    {
                                        var (elemLabel, _) = HandwritingProfileService.ClassifyCluster(c.cluster);
                                        return string.IsNullOrEmpty(elemLabel) ? "1" : elemLabel;
                                    });
                                    matrixRows.Add(string.Join(" & ", cols));
                                }
                                return ("\\begin{pmatrix} " + string.Join(" \\\\ ", matrixRows) + " \\end{pmatrix}", new List<string>());
                            }
                        }
                    }
                }
            }

            // 2. Nhận diện dấu gạch phân số (Fraction Bar): Nét ngang dài có các cụm nằm trên và dưới
            for (int i = 0; i < strokeBoxes.Count; i++)
            {
                var bar = strokeBoxes[i];
                if (bar.W > 25 && bar.W > bar.H * 2.5f)
                {
                    var numStrokes = new List<List<PointF>>();
                    var denStrokes = new List<List<PointF>>();

                    for (int j = 0; j < strokeBoxes.Count; j++)
                    {
                        if (i == j) continue;
                        var other = strokeBoxes[j];
                        float overlapX = Math.Min(bar.MaxX, other.MaxX) - Math.Max(bar.MinX, other.MinX);
                        if (overlapX > -10)
                        {
                            if (other.MaxY < bar.MinY + 8) numStrokes.Add(other.Stroke);
                            else if (other.MinY > bar.MaxY - 8) denStrokes.Add(other.Stroke);
                        }
                    }

                    if (numStrokes.Count > 0 && denStrokes.Count > 0 && 
                        (numStrokes.Count + denStrokes.Count + 1 == strokeBoxes.Count))
                    {
                        string num = RecognizeWithPersonalizedProfile(numStrokes);
                        string den = RecognizeWithPersonalizedProfile(denStrokes);
                        if (string.IsNullOrEmpty(num)) num = "1";
                        if (string.IsNullOrEmpty(den)) den = "1";
                        return ($"\\frac{{{num}}}{{{den}}}", new List<string>());
                    }
                }
            }

            // 3. Gom cụm các nét theo thứ tự viết từ trái sang phải bằng thuật toán chuẩn hóa chung
            var clusteredItems = HandwritingProfileService.ClusterStrokesWithBoxes(strokes);
            var clusters = clusteredItems.Select(x => x.cluster).ToList();
            var clusterBoxes = clusteredItems.Select(x => x.box).ToList();

            // Trường hợp 1 ký tự / 1 cụm đơn lẻ: Trả về ứng viên trực tiếp
            if (clusters.Count <= 1)
            {
                var targetStrokes = clusters.Count == 1 ? clusters[0] : strokes;
                var singleCands = HandwritingProfileService.ClassifyClusterWithCandidates(targetStrokes, topK: 4);
                string singlePrimary = singleCands.Count > 0 ? singleCands[0].label : "x";
                var singleAlts = singleCands.Skip(1).Select(c => c.label).Take(3).ToList();
                return (singlePrimary, singleAlts);
            }

            // Kiểm tra xem toàn bộ nét vẽ có khớp trực tiếp với một mẫu biểu thức nguyên vẹn không
            var wholeCands = HandwritingProfileService.ClassifyClusterWithCandidates(strokes, topK: 3);
            if (wholeCands.Count > 0 && wholeCands[0].confidence > 0.85f && wholeCands[0].label.Length > 1)
            {
                var alts = wholeCands.Skip(1).Select(c => c.label).ToList();
                return (wholeCands[0].label, alts);
            }

            // 4. Nhận diện từng ký tự qua hồ sơ cá nhân hóa (HandwritingProfileService) kèm ứng viên thay thế
            var tokenList = new List<(string charLabel, List<string> alts, RectangleF box)>();
            for (int i = 0; i < clusters.Count; i++)
            {
                var cands = HandwritingProfileService.ClassifyClusterWithCandidates(clusters[i], topK: 3);
                string lbl = cands.Count > 0 ? cands[0].label : "x";
                var alts = cands.Skip(1).Select(c => c.label).ToList();
                tokenList.Add((lbl, alts, clusterBoxes[i]));
            }

            // 5. Ghép nối biểu thức toán học chính
            string primaryResult = BuildFormulaFromTokens(tokenList);

            // 6. Xây dựng danh sách biểu thức ứng viên thay thế (Alternative Candidate Formulas)
            var candidateFormulas = new List<string>();

            // Thêm các gợi ý từ wholeCands nếu có độ tin cậy tốt
            foreach (var wc in wholeCands.Where(w => !string.IsNullOrEmpty(w.label) && w.confidence > 0.45f))
            {
                if (!candidateFormulas.Contains(wc.label) && !wc.label.Equals(primaryResult, StringComparison.OrdinalIgnoreCase))
                {
                    candidateFormulas.Add(wc.label);
                }
            }

            // Sinh biến thể biểu thức bằng cách thế ứng viên á quân của từng ký tự
            for (int i = 0; i < tokenList.Count && candidateFormulas.Count < 3; i++)
            {
                foreach (var altChar in tokenList[i].alts)
                {
                    string altForm = BuildFormulaFromTokens(tokenList, replaceIdx: i, replacement: altChar);
                    if (!string.IsNullOrEmpty(altForm) && 
                        !altForm.Equals(primaryResult, StringComparison.OrdinalIgnoreCase) && 
                        !candidateFormulas.Contains(altForm))
                    {
                        candidateFormulas.Add(altForm);
                        if (candidateFormulas.Count >= 3) break;
                    }
                }
            }

            return (primaryResult, candidateFormulas);
        }

        private static string BuildFormulaFromTokens(
            List<(string charLabel, List<string> alts, RectangleF box)> tokens,
            int replaceIdx = -1,
            string replacement = "")
        {
            var sb = new StringBuilder();
            string[] mathOps = { "+", "-", "=", "<", ">" };
            float expShift = HandwritingProfileService.Spatial.ExponentMinVerticalShift;
            float expMaxRatio = HandwritingProfileService.Spatial.ExponentMaxHeightRatio;
            float subShift = HandwritingProfileService.Spatial.SubscriptMinVerticalShift;
            float subMaxRatio = HandwritingProfileService.Spatial.SubscriptMaxHeightRatio;

            for (int i = 0; i < tokens.Count; i++)
            {
                var cur = tokens[i];
                string label = (i == replaceIdx) ? replacement : cur.charLabel;

                if (i > 0)
                {
                    var prev = tokens[i - 1];
                    float prevCy = prev.box.Y + prev.box.Height / 2f;
                    float curCy = cur.box.Y + cur.box.Height / 2f;

                    // Nhận diện số mũ (Superscript)
                    if (curCy < prevCy - prev.box.Height * expShift && cur.box.Height <= prev.box.Height * expMaxRatio)
                    {
                        sb.Append("^{" + label + "}");
                        continue;
                    }

                    // Nhận diện chỉ số dưới (Subscript)
                    if (curCy > prevCy + prev.box.Height * subShift && cur.box.Height <= prev.box.Height * subMaxRatio)
                    {
                        sb.Append("_{" + label + "}");
                        continue;
                    }

                    // Khoảng trắng quanh dấu toán học
                    if (mathOps.Contains(label) || mathOps.Contains(prev.charLabel))
                    {
                        sb.Append(" " + label + " ");
                        continue;
                    }
                }

                sb.Append(label);
            }

            string result = sb.ToString();
            while (result.Contains("  "))
            {
                result = result.Replace("  ", " ");
            }

            return result.Trim();
        }

        /// <summary>
        /// Khung chạy suy luận mô hình Deep Learning ONNX (khi người dùng đặt file math_rec.onnx vào thư mục models).
        /// </summary>
        private static string? RunOnnxInference(string pureBase64, string modelPath)
        {
            try
            {
                using var session = new Microsoft.ML.OnnxRuntime.InferenceSession(modelPath);
                Program.Log($"[ONNX Engine] Đã nạp thành công mô hình ONNX từ {modelPath}");
                // Khi có file weights ONNX được phân phối, tensor sẽ được feed tại đây.
                return null;
            }
            catch (Exception ex)
            {
                Program.Log($"[ONNX Inference LỖI] {ex.Message}");
                return null;
            }
        }

        private static string CleanLatexOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            string text = raw.Trim();

            // Gỡ bỏ markdown code fences ```latex ... ```
            if (text.StartsWith("```"))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline > 0)
                {
                    text = text.Substring(firstNewline + 1);
                }
                int lastFence = text.LastIndexOf("```");
                if (lastFence >= 0)
                {
                    text = text.Substring(0, lastFence);
                }
                text = text.Trim();
            }

            // Gỡ bỏ dấu $ hoặc $$ bao quanh
            if (text.StartsWith("$$") && text.EndsWith("$$") && text.Length > 4)
            {
                text = text.Substring(2, text.Length - 4).Trim();
            }
            else if (text.StartsWith("$") && text.EndsWith("$") && text.Length > 2)
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            // Gỡ bỏ các thẻ giải thích thừa nếu có
            if (text.StartsWith("LaTeX:", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(6).Trim();
            }

            return text;
        }
    }
}
