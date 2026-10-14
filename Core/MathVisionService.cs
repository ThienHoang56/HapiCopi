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
                if (!string.IsNullOrWhiteSpace(pureBase64) && EndToEndMathEngine.IsAvailable)
                {
                    try
                    {
                        string cleanB64 = pureBase64;
                        int commaIdx = cleanB64.IndexOf(',');
                        if (commaIdx >= 0) cleanB64 = cleanB64.Substring(commaIdx + 1);

                        byte[] bytes = Convert.FromBase64String(cleanB64);
                        using var ms = new MemoryStream(bytes);
                        using var bmp = new Bitmap(ms);
                        var (bmpOk, bmpLatex, bmpMs) = EndToEndMathEngine.RecognizeBitmap(bmp);
                        if (bmpOk && !string.IsNullOrEmpty(bmpLatex))
                        {
                            sw.Stop();
                            return (true, bmpLatex, new List<string>(), "", sw.ElapsedMilliseconds);
                        }
                    }
                    catch { }
                }

                sw.Stop();
                return (false, "", new List<string>(), "ChÆ°a cÃ³ nÃ©t váº½ Ä‘á»ƒ nháº­n diá»‡n cá»¥c bá»™.", sw.ElapsedMilliseconds);
            }

            try
            {
                // Ưu tiên 1: Bóc tách cấu trúc không gian 2D (Ma trận 2D & Hệ phương trình)
                var (spatialSuccess, spatialLatex) = TryRecognize2DSpatialStructure(strokes);
                if (spatialSuccess && !string.IsNullOrEmpty(spatialLatex))
                {
                    sw.Stop();
                    return (true, spatialLatex, new List<string>(), "", sw.ElapsedMilliseconds);
                }

                // Ưu tiên 2: Chạy mô hình End-to-End Sequence (A4 - Vision Transformer / BTTR)
                if (EndToEndMathEngine.IsAvailable)
                {
                    var (e2eSuccess, e2eLatex, e2eMs) = EndToEndMathEngine.RecognizeStrokes(strokes);
                    if (e2eSuccess && !string.IsNullOrEmpty(e2eLatex))
                    {
                        sw.Stop();
                        return (true, e2eLatex, new List<string>(), "", sw.ElapsedMilliseconds);
                    }
                }

                // Khởi tạo ONNX engine nếu chưa nạp
                OnnxMathVisionEngine.EnsureInitialized();

                // Chạy toàn diện pipeline phân tầng (Tầng 1 -> Tầng 2 -> Tầng 3):
                // Topo + Prototype Adapter + Deep Learning ONNX + Spatial Layout + Cú pháp toán học (MathSyntaxPrior)
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

            // 1. Nhận diện cấu trúc không gian 2D (Ma trận 2D & Hệ phương trình)
            var (spatialOk, spatialRes) = TryRecognize2DSpatialStructure(strokes);
            if (spatialOk && !string.IsNullOrEmpty(spatialRes))
            {
                return (spatialRes, new List<string>());
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

            // 3. Nhận diện Căn thức (\sqrt{X}): Nét căn thức có thanh ngang dài che phủ phần tử bên trong
            for (int i = 0; i < strokeBoxes.Count; i++)
            {
                var sqrtBox = strokeBoxes[i];
                if (sqrtBox.W >= 24 && strokeBoxes.Count > 1)
                {
                    var insideStrokes = new List<List<PointF>>();
                    for (int j = 0; j < strokeBoxes.Count; j++)
                    {
                        if (i == j) continue;
                        var other = strokeBoxes[j];
                        // Nằm bên trong phạm vi ngang của dấu căn (sau phần móc và dưới xà ngang)
                        if (other.MinX >= sqrtBox.MinX + 6 && other.MaxX <= sqrtBox.MaxX + 8 &&
                            other.MinY >= sqrtBox.MinY - 4 && other.MaxY <= sqrtBox.MaxY + 14)
                        {
                            insideStrokes.Add(other.Stroke);
                        }
                    }

                    if (insideStrokes.Count > 0 && insideStrokes.Count == strokeBoxes.Count - 1)
                    {
                        var (candLabel, _) = HandwritingProfileService.ClassifyCluster(new List<List<PointF>> { sqrtBox.Stroke });
                        if (candLabel == "\\sqrt" || (sqrtBox.W > sqrtBox.H * 1.05f))
                        {
                            string inner = RecognizeWithPersonalizedProfile(insideStrokes);
                            if (string.IsNullOrEmpty(inner)) inner = "x";
                            return ($"\\sqrt{{{inner}}}", new List<string>());
                        }
                    }
                }
            }

            // 3.1 Nhận diện Tích phân (\int) & Cận: Dấu tích phân có chiều cao lớn
            for (int i = 0; i < strokeBoxes.Count; i++)
            {
                var intBox = strokeBoxes[i];
                if (intBox.H >= 36 && intBox.H >= intBox.W * 1.6f && strokeBoxes.Count > 1)
                {
                    var intCands = HandwritingProfileService.ClassifyClusterWithCandidates(new List<List<PointF>> { intBox.Stroke }, 3);
                    string candLabel = intCands.Count > 0 ? intCands[0].label : "";
                    if (candLabel == "\\int" || intCands.Any(c => c.label == "\\int"))
                    {
                        var prefixStrokes = new List<List<PointF>>();
                        var lowerStrokes = new List<List<PointF>>();
                        var upperStrokes = new List<List<PointF>>();
                        var bodyStrokes = new List<List<PointF>>();

                        float intTop = intBox.MinY;
                        float intBottom = intBox.MaxY;
                        float intLeft = intBox.MinX;
                        float intRight = intBox.MaxX;

                        for (int j = 0; j < strokeBoxes.Count; j++)
                        {
                            if (i == j) continue;
                            var other = strokeBoxes[j];

                            // 1. Tiền tố bên trái dấu tích phân (ví dụ: 3x \int hoặc 2 \int)
                            if (other.MaxX <= intLeft + 2f)
                            {
                                prefixStrokes.Add(other.Stroke);
                            }
                            // 2. Cận trên: nằm phía trên dấu tích phân hoặc góc trên bên phải
                            else if (other.MinY <= intTop + intBox.H * 0.38f && other.MinX <= intRight + intBox.W * 1.5f && other.MaxX >= intLeft - 6f)
                            {
                                upperStrokes.Add(other.Stroke);
                            }
                            // 3. Cận dưới: nằm phía dưới dấu tích phân hoặc góc dưới bên phải
                            else if (other.MaxY >= intBottom - intBox.H * 0.38f && other.MinX <= intRight + intBox.W * 1.5f && other.MaxX >= intLeft - 6f)
                            {
                                lowerStrokes.Add(other.Stroke);
                            }
                            // 4. Biểu thức dưới dấu tích phân (Body)
                            else if (other.MinX >= intRight - 10f)
                            {
                                bodyStrokes.Add(other.Stroke);
                            }
                        }

                        if (bodyStrokes.Count > 0 && (prefixStrokes.Count + lowerStrokes.Count + upperStrokes.Count + bodyStrokes.Count + 1 == strokeBoxes.Count))
                        {
                            string prefix = prefixStrokes.Count > 0 ? RecognizeWithPersonalizedProfile(prefixStrokes) : "";
                            string body = RecognizeWithPersonalizedProfile(bodyStrokes);
                            if (string.IsNullOrEmpty(body)) body = "x";

                            string lower = lowerStrokes.Count > 0 ? RecognizeWithPersonalizedProfile(lowerStrokes) : "";
                            string upper = upperStrokes.Count > 0 ? RecognizeWithPersonalizedProfile(upperStrokes) : "";

                            string intPart;
                            if (!string.IsNullOrEmpty(lower) && !string.IsNullOrEmpty(upper))
                            {
                                intPart = $"\\int_{{{lower}}}^{{{upper}}} {body}";
                            }
                            else if (!string.IsNullOrEmpty(lower))
                            {
                                intPart = $"\\int_{{{lower}}} {body}";
                            }
                            else if (!string.IsNullOrEmpty(upper))
                            {
                                intPart = $"\\int^{{{upper}}} {body}";
                            }
                            else
                            {
                                intPart = $"\\int {body}";
                            }

                            string finalResult = string.IsNullOrEmpty(prefix) ? intPart : $"{prefix} {intPart}";
                            return (finalResult, new List<string>());
                        }
                    }
                }
            }

            // 4. Gom cụm các nét theo thứ tự viết từ trái sang phải bằng thuật toán chuẩn hóa chung
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

            // 5. Nhận diện từng ký tự qua hồ sơ cá nhân hóa (HandwritingProfileService) kèm ứng viên thay thế
            var tokenList = new List<(string charLabel, List<string> alts, RectangleF box)>();
            for (int i = 0; i < clusters.Count; i++)
            {
                var cands = HandwritingProfileService.ClassifyClusterWithCandidates(clusters[i], topK: 5);
                string lbl = cands.Count > 0 ? cands[0].label : "x";
                var alts = cands.Skip(1).Select(c => c.label).ToList();
                tokenList.Add((lbl, alts, clusterBoxes[i]));
            }

            // 6. Ghép nối biểu thức toán học chính kèm theo ngữ pháp MathSyntaxPrior và baseline tracking
            string primaryResult = BuildFormulaFromTokens(tokenList);

            // 7. Xây dựng danh sách biểu thức ứng viên thay thế (Alternative Candidate Formulas)
            var candidateFormulas = new List<string>();


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
            if (tokens == null || tokens.Count == 0) return "";

            // Áp dụng bộ lọc ngữ pháp toán học để khử nhầm lẫn (ví dụ x 7 y -> x + y, khử ++ cạnh nhau)
            var preparedTokens = new List<(string charLabel, List<string> alts, RectangleF box)>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                string lbl = (i == replaceIdx) ? replacement : tokens[i].charLabel;
                preparedTokens.Add((lbl, tokens[i].alts, tokens[i].box));
            }
            var refinedTokens = MathSyntaxPrior.DisambiguateTokens(preparedTokens);

            var sb = new StringBuilder();
            string[] mathOps = { "+", "-", "=", "<", ">", "\\times", "/", "\\pm", "\\le", "\\ge", "\\neq" };
            float expShift = HandwritingProfileService.Spatial.ExponentMinVerticalShift;
            float expMaxRatio = HandwritingProfileService.Spatial.ExponentMaxHeightRatio;
            float subShift = HandwritingProfileService.Spatial.SubscriptMinVerticalShift;
            float subMaxRatio = HandwritingProfileService.Spatial.SubscriptMaxHeightRatio;
            float effectiveExpMaxRatio = Math.Max(0.78f, expMaxRatio);
            float effectiveSubMaxRatio = Math.Max(0.78f, subMaxRatio);

            // Baseline Tracking: Theo dõi dòng cơ sở để không bị dồn số mũ/chỉ số dưới sai lệch
            float baselineCy = refinedTokens[0].box.Y + refinedTokens[0].box.Height / 2f;
            float baselineH = refinedTokens[0].box.Height;
            bool inExp = false;
            bool inSub = false;

            for (int i = 0; i < refinedTokens.Count; i++)
            {
                var cur = refinedTokens[i];
                string label = cur.charLabel;
                float curCy = cur.box.Y + cur.box.Height / 2f;

                if (i == 0)
                {
                    baselineCy = curCy;
                    baselineH = cur.box.Height;
                    sb.Append(label);
                    continue;
                }

                // Kiểm tra xem nét này có hạ về baseline không (ví dụ gặp dấu +, -, = hoặc ký tự nằm ngang baselineCy)
                bool isBackToBaseline = Math.Abs(curCy - baselineCy) < baselineH * 0.45f || mathOps.Contains(label);

                if (isBackToBaseline)
                {
                    inExp = false;
                    inSub = false;
                    baselineCy = (baselineCy * 0.7f) + (curCy * 0.3f);
                    baselineH = (baselineH * 0.7f) + (cur.box.Height * 0.3f);

                    if (mathOps.Contains(label))
                    {
                        sb.Append(" " + label + " ");
                    }
                    else
                    {
                        sb.Append(label);
                    }
                    continue;
                }

                // Kiểm tra số mũ (Superscript)
                if (!inSub && (curCy < baselineCy - baselineH * expShift) && (cur.box.Height <= baselineH * effectiveExpMaxRatio))
                {
                    sb.Append("^{" + label + "}");
                    inExp = true;
                    continue;
                }

                // Kiểm tra chỉ số dưới (Subscript)
                if (!inExp && (curCy > baselineCy + baselineH * subShift) && (cur.box.Height <= baselineH * effectiveSubMaxRatio))
                {
                    sb.Append("_{" + label + "}");
                    inSub = true;
                    continue;
                }

                // Nằm trên dòng thông thường
                inExp = false;
                inSub = false;
                baselineCy = (baselineCy * 0.7f) + (curCy * 0.3f);
                baselineH = (baselineH * 0.7f) + (cur.box.Height * 0.3f);

                if (mathOps.Contains(label))
                {
                    sb.Append(" " + label + " ");
                }
                else
                {
                    sb.Append(label);
                }
            }

            string result = sb.ToString();
            while (result.Contains("  "))
            {
                result = result.Replace("  ", " ");
            }

            // Cân bằng ngoặc mở/đóng qua ngữ pháp
            result = MathSyntaxPrior.BalanceBrackets(result);

            return result.Trim();
        }

        /// <summary>
        /// Khung chạy suy luận mô hình Deep Learning ONNX qua OnnxMathVisionEngine.
        /// </summary>
        public static string? RunOnnxInference(List<List<PointF>> strokes)
        {
            try
            {
                if (!OnnxMathVisionEngine.IsModelLoaded) return null;
                var predictions = OnnxMathVisionEngine.Predict(strokes, topK: 1);
                return predictions.Count > 0 ? predictions[0].label : null;
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

        private sealed class MathStrokeBox
        {
            public int Index { get; init; }
            public List<PointF> Stroke { get; init; } = null!;
            public float MinX { get; init; }
            public float MaxX { get; init; }
            public float MinY { get; init; }
            public float MaxY { get; init; }
            public float W { get; init; }
            public float H { get; init; }
            public float Cx { get; init; }
            public float Cy { get; init; }
        }

        /// <summary>
        /// Nhận diện các cấu trúc không gian 2D rời rạc:
        /// - Phân nhánh 1: Ma trận 2D (M x N) với cặp dấu ngoặc bao quanh [ ], ( ), { }, | |
        /// - Phân nhánh 2: Hệ phương trình với dấu ngoặc nhọn { bên trái và các dòng phương trình bên phải.
        /// Áp dụng kiến trúc phân tầng không gian (Spatial Hierarchy): Bóc tách Hàng (Rows) trước, rồi bóc tách Ô (Cells) trong từng hàng.
        /// </summary>
        public static (bool success, string latex) TryRecognize2DSpatialStructure(List<List<PointF>> strokes)
        {
            if (strokes == null || strokes.Count < 3) return (false, "");

            var strokeBoxes = strokes.Select((s, idx) =>
            {
                float sMinX = s.Min(p => p.X), sMaxX = s.Max(p => p.X);
                float sMinY = s.Min(p => p.Y), sMaxY = s.Max(p => p.Y);
                return new MathStrokeBox
                {
                    Index = idx,
                    Stroke = s,
                    MinX = sMinX, MaxX = sMaxX,
                    MinY = sMinY, MaxY = sMaxY,
                    W = Math.Max(1f, sMaxX - sMinX),
                    H = Math.Max(1f, sMaxY - sMinY),
                    Cx = (sMinX + sMaxX) / 2f,
                    Cy = (sMinY + sMaxY) / 2f
                };
            }).ToList();

            float totalMinX = strokeBoxes.Min(s => s.MinX);
            float totalMaxX = strokeBoxes.Max(s => s.MaxX);
            float totalMinY = strokeBoxes.Min(s => s.MinY);
            float totalMaxY = strokeBoxes.Max(s => s.MaxY);
            float totalW = Math.Max(1f, totalMaxX - totalMinX);
            float totalH = Math.Max(1f, totalMaxY - totalMinY);

            if (totalH < 30 || totalW < 25) return (false, "");

            // Delimiters
            float minDelimH = totalH * 0.48f;
            var leftCandidates = strokeBoxes
                .Where(s => s.MinX <= totalMinX + totalW * 0.35f && s.H >= minDelimH)
                .OrderBy(s => s.MinX)
                .ToList();

            var rightCandidates = strokeBoxes
                .Where(s => s.MaxX >= totalMaxX - totalW * 0.35f && s.H >= minDelimH)
                .OrderByDescending(s => s.MaxX)
                .ToList();

            var leftDelim = leftCandidates.FirstOrDefault();
            var rightDelim = rightCandidates.FirstOrDefault();

            if (leftDelim != null && rightDelim != null && leftDelim.Index == rightDelim.Index)
            {
                rightDelim = null;
            }

            // =========================================================================
            // PHÂN NHÁNH 1: MA TRẬN 2D (Có cả ngoặc trái và ngoặc phải cao)
            // =========================================================================
            if (leftDelim != null && rightDelim != null)
            {
                var innerStrokes = strokeBoxes
                    .Where(s => s.Index != leftDelim.Index && s.Index != rightDelim.Index)
                    .ToList();

                if (innerStrokes.Count >= 2)
                {
                    float avgInnerH = innerStrokes.Average(s => s.H);

                    // BƯỚC 1: Phân cụm các nét bên trong theo từng HÀNG (Y-center & overlap)
                    var sortedByY = innerStrokes.OrderBy(s => s.Cy).ToList();
                    var rows = new List<List<MathStrokeBox>>();
                    var currentRow = new List<MathStrokeBox> { sortedByY[0] };
                    float rowAvgCy = sortedByY[0].Cy;
                    float rowMinY = sortedByY[0].MinY;
                    float rowMaxY = sortedByY[0].MaxY;

                    for (int i = 1; i < sortedByY.Count; i++)
                    {
                        var item = sortedByY[i];
                        float dy = item.Cy - rowAvgCy;
                        float overlapY = Math.Min(rowMaxY, item.MaxY) - Math.Max(rowMinY, item.MinY);
                        float minBoxH = Math.Min(item.H, rowMaxY - rowMinY);

                        if (dy < Math.Max(16f, avgInnerH * 0.55f) || (overlapY > minBoxH * 0.30f && dy < avgInnerH * 0.85f))
                        {
                            currentRow.Add(item);
                            rowMinY = Math.Min(rowMinY, item.MinY);
                            rowMaxY = Math.Max(rowMaxY, item.MaxY);
                            rowAvgCy = currentRow.Average(s => s.Cy);
                        }
                        else
                        {
                            rows.Add(currentRow);
                            currentRow = new List<MathStrokeBox> { item };
                            rowMinY = item.MinY;
                            rowMaxY = item.MaxY;
                            rowAvgCy = item.Cy;
                        }
                    }
                    if (currentRow.Count > 0) rows.Add(currentRow);

                    bool isMultiRow = rows.Count >= 2;
                    bool isMultiColRowVector = rows.Count == 1 && rows[0].Count >= 2;

                    if (isMultiRow || isMultiColRowVector)
                    {
                        string env = DetermineMatrixEnv(leftDelim.Stroke, rightDelim.Stroke);

                        var matrixRows = new List<string>();
                        foreach (var row in rows)
                        {
                            var sortedRow = row.OrderBy(s => s.MinX).ToList();

                            // BƯỚC 2: Trong mỗi hàng, gom các nét thành từng Ô (Cell) theo khoảng cách ngang
                            var cells = new List<List<List<PointF>>>();
                            var curCellStrokes = new List<List<PointF>> { sortedRow[0].Stroke };
                            float curCellMaxX = sortedRow[0].MaxX;

                            for (int j = 1; j < sortedRow.Count; j++)
                            {
                                var curStroke = sortedRow[j];
                                float gapX = curStroke.MinX - curCellMaxX;

                                if (gapX < Math.Max(12f, avgInnerH * 0.38f))
                                {
                                    curCellStrokes.Add(curStroke.Stroke);
                                    curCellMaxX = Math.Max(curCellMaxX, curStroke.MaxX);
                                }
                                else
                                {
                                    cells.Add(curCellStrokes);
                                    curCellStrokes = new List<List<PointF>> { curStroke.Stroke };
                                    curCellMaxX = curStroke.MaxX;
                                }
                            }
                            if (curCellStrokes.Count > 0) cells.Add(curCellStrokes);

                            var cellLatexList = new List<string>();
                            foreach (var cell in cells)
                            {
                                string cellText = RecognizeMatrixCell(cell);
                                cellLatexList.Add(cellText);
                            }

                            matrixRows.Add(string.Join(" & ", cellLatexList));
                        }

                        string matrixResult = $"\\begin{{{env}}} {string.Join(" \\\\ ", matrixRows)} \\end{{{env}}}";
                        return (true, matrixResult);
                    }
                }
            }

            // =========================================================================
            // PHÂN NHÁNH 2: HỆ PHƯƠNG TRÌNH (Chỉ có ngoặc trái cao, bên phải là các phương trình)
            // =========================================================================
            if (leftDelim != null && rightDelim == null)
            {
                var (leftLabel, _) = HandwritingProfileService.ClassifyCluster(new List<List<PointF>> { leftDelim.Stroke });
                bool isCurly = leftLabel is "\\{" or "{" || IsCurlyBrace(leftDelim.Stroke, isLeft: true);
                bool isSquare = leftLabel == "[" || IsSquareBracket(leftDelim.Stroke, isLeft: true);

                if (isCurly || isSquare || leftDelim.H >= totalH * 0.55f)
                {
                    var eqStrokes = strokeBoxes
                        .Where(s => s.Index != leftDelim.Index && s.MinX >= leftDelim.MinX + 5)
                        .ToList();

                    if (eqStrokes.Count >= 2)
                    {
                        float avgEqH = eqStrokes.Average(s => s.H);

                        var sortedByY = eqStrokes.OrderBy(s => s.Cy).ToList();
                        var eqRows = new List<List<MathStrokeBox>>();
                        var curEqRow = new List<MathStrokeBox> { sortedByY[0] };
                        float rowAvgCy = sortedByY[0].Cy;
                        float rowMinY = sortedByY[0].MinY;
                        float rowMaxY = sortedByY[0].MaxY;

                        for (int i = 1; i < sortedByY.Count; i++)
                        {
                            var item = sortedByY[i];
                            float dy = item.Cy - rowAvgCy;
                            float overlapY = Math.Min(rowMaxY, item.MaxY) - Math.Max(rowMinY, item.MinY);
                            float minBoxH = Math.Min(item.H, rowMaxY - rowMinY);

                            if (dy < Math.Max(18f, avgEqH * 0.60f) || (overlapY > minBoxH * 0.30f && dy < avgEqH * 0.90f))
                            {
                                curEqRow.Add(item);
                                rowMinY = Math.Min(rowMinY, item.MinY);
                                rowMaxY = Math.Max(rowMaxY, item.MaxY);
                                rowAvgCy = curEqRow.Average(s => s.Cy);
                            }
                            else
                            {
                                eqRows.Add(curEqRow);
                                curEqRow = new List<MathStrokeBox> { item };
                                rowMinY = item.MinY;
                                rowMaxY = item.MaxY;
                                rowAvgCy = item.Cy;
                            }
                        }
                        if (curEqRow.Count > 0) eqRows.Add(curEqRow);

                        if (eqRows.Count >= 2)
                        {
                            var linesLatex = new List<string>();
                            foreach (var eqRow in eqRows)
                            {
                                var rowStrokes = eqRow.Select(s => s.Stroke).ToList();
                                string lineLatex = RecognizeEquationLine(rowStrokes);
                                if (!string.IsNullOrWhiteSpace(lineLatex))
                                {
                                    linesLatex.Add(lineLatex.Trim());
                                }
                            }

                            if (linesLatex.Count >= 2)
                            {
                                string casesResult = $"\\begin{{cases}} {string.Join(" \\\\ ", linesLatex)} \\end{{cases}}";
                                return (true, casesResult);
                            }
                        }
                    }
                }
            }

            return (false, "");
        }

        private static string RecognizeMatrixCell(List<List<PointF>> cellStrokes)
        {
            if (cellStrokes == null || cellStrokes.Count == 0) return "0";

            var (label, _) = HandwritingProfileService.ClassifyCluster(cellStrokes);
            if (!string.IsNullOrEmpty(label)) return label;

            if (EndToEndMathEngine.IsAvailable)
            {
                var (e2eOk, e2eLatex, _) = EndToEndMathEngine.RecognizeStrokes(cellStrokes);
                if (e2eOk && !string.IsNullOrWhiteSpace(e2eLatex))
                {
                    return e2eLatex.Trim();
                }
            }

            string fallback = RecognizeWithPersonalizedProfile(cellStrokes);
            return string.IsNullOrEmpty(fallback) ? "1" : fallback;
        }

        private static string RecognizeEquationLine(List<List<PointF>> lineStrokes)
        {
            if (lineStrokes == null || lineStrokes.Count == 0) return "";

            if (EndToEndMathEngine.IsAvailable)
            {
                var (e2eOk, e2eLatex, _) = EndToEndMathEngine.RecognizeStrokes(lineStrokes);
                if (e2eOk && !string.IsNullOrWhiteSpace(e2eLatex))
                {
                    return e2eLatex.Trim();
                }
            }

            return RecognizeWithPersonalizedProfile(lineStrokes);
        }

        private static string DetermineMatrixEnv(List<PointF> leftBracket, List<PointF> rightBracket)
        {
            if (IsSquareBracket(leftBracket, isLeft: true) || IsSquareBracket(rightBracket, isLeft: false))
            {
                return "bmatrix";
            }

            var (leftLabel, _) = HandwritingProfileService.ClassifyCluster(new List<List<PointF>> { leftBracket });
            var (rightLabel, _) = HandwritingProfileService.ClassifyCluster(new List<List<PointF>> { rightBracket });

            if (leftLabel == "[" || rightLabel == "]") return "bmatrix";
            if (leftLabel is "\\{" or "{" || rightLabel is "\\}" or "}") return "Bmatrix";
            if (leftLabel == "|" || rightLabel == "|") return "vmatrix";
            if (leftLabel == "(" || rightLabel == ")") return "pmatrix";

            return "pmatrix";
        }

        private static List<PointF> ResampleStroke(List<PointF> pts, int numPoints = 32)
        {
            if (pts == null || pts.Count == 0) return new List<PointF>();
            if (pts.Count == 1) return Enumerable.Repeat(pts[0], numPoints).ToList();

            float totalLen = 0f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float dx = pts[i + 1].X - pts[i].X;
                float dy = pts[i + 1].Y - pts[i].Y;
                totalLen += (float)Math.Sqrt(dx * dx + dy * dy);
            }

            if (totalLen < 1e-4f) return Enumerable.Repeat(pts[0], numPoints).ToList();

            var result = new List<PointF> { pts[0] };
            float interval = totalLen / (numPoints - 1);
            float accumulated = 0f;
            int curIdx = 0;

            for (int i = 1; i < numPoints - 1; i++)
            {
                float targetDist = i * interval;
                while (curIdx < pts.Count - 1)
                {
                    float dx = pts[curIdx + 1].X - pts[curIdx].X;
                    float dy = pts[curIdx + 1].Y - pts[curIdx].Y;
                    float segLen = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (accumulated + segLen >= targetDist)
                    {
                        float t = (targetDist - accumulated) / (segLen > 1e-4f ? segLen : 1f);
                        result.Add(new PointF(pts[curIdx].X + t * dx, pts[curIdx].Y + t * dy));
                        break;
                    }
                    else
                    {
                        accumulated += segLen;
                        curIdx++;
                    }
                }
            }
            result.Add(pts[^1]);
            return result;
        }

        private static bool IsSquareBracket(List<PointF> rawPts, bool isLeft)
        {
            if (rawPts == null || rawPts.Count < 3) return false;

            var pts = ResampleStroke(rawPts, 32);
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            float w = maxX - minX, h = maxY - minY;

            if (h < 25 || w < 4 || w < h * 0.04f) return false;

            var topPts = pts.Where(p => p.Y <= minY + h * 0.20f).ToList();
            var midPts = pts.Where(p => p.Y >= minY + h * 0.25f && p.Y <= maxY - h * 0.25f).ToList();
            var botPts = pts.Where(p => p.Y >= maxY - h * 0.20f).ToList();

            if (topPts.Count == 0 || botPts.Count == 0 || midPts.Count == 0) return false;

            float midXSpan = midPts.Max(p => p.X) - midPts.Min(p => p.X);
            // Sống lưng của ngoặc vuông [ là đường thẳng đứng, độ biến thiên X ở thân giữa rất bé
            if (midXSpan > w * 0.30f) return false;

            // Móc ngang trên và dưới phải vươn rõ rệt
            float topW = topPts.Max(p => p.X) - topPts.Min(p => p.X);
            float botW = botPts.Max(p => p.X) - botPts.Min(p => p.X);
            if (topW < w * 0.35f || botW < w * 0.35f) return false;

            if (isLeft)
            {
                float midMeanX = midPts.Average(p => p.X);
                float topMaxX = topPts.Max(p => p.X);
                float botMaxX = botPts.Max(p => p.X);
                return (topMaxX > midMeanX + w * 0.35f) && (botMaxX > midMeanX + w * 0.35f);
            }
            else
            {
                float midMeanX = midPts.Average(p => p.X);
                float topMinX = topPts.Min(p => p.X);
                float botMinX = botPts.Min(p => p.X);
                return (topMinX < midMeanX - w * 0.35f) && (botMinX < midMeanX - w * 0.35f);
            }
        }

        private static bool IsCurlyBrace(List<PointF> rawPts, bool isLeft)
        {
            if (rawPts == null || rawPts.Count < 3) return false;

            var pts = ResampleStroke(rawPts, 32);
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            float w = maxX - minX, h = maxY - minY;

            if (h < 25 || w < 4) return false;

            var midPts = pts.Where(p => p.Y >= minY + h * 0.35f && p.Y <= minY + h * 0.65f).ToList();
            if (midPts.Count == 0) return false;

            var topEnd = pts[0];
            var botEnd = pts[^1];

            var upperArch = pts.Where(p => p.Y >= minY + h * 0.15f && p.Y <= minY + h * 0.35f).ToList();
            var lowerArch = pts.Where(p => p.Y >= minY + h * 0.65f && p.Y <= minY + h * 0.85f).ToList();

            if (upperArch.Count == 0 || lowerArch.Count == 0) return false;

            if (isLeft)
            {
                float midMinX = midPts.Min(p => p.X);
                float upperArchMeanX = upperArch.Average(p => p.X);
                float lowerArchMeanX = lowerArch.Average(p => p.X);
                bool isCuspLeft = (midMinX < upperArchMeanX - w * 0.10f) && (midMinX < lowerArchMeanX - w * 0.10f);
                return isCuspLeft && (topEnd.X > midMinX + w * 0.30f) && (botEnd.X > midMinX + w * 0.30f);
            }
            else
            {
                float midMaxX = midPts.Max(p => p.X);
                float upperArchMeanX = upperArch.Average(p => p.X);
                float lowerArchMeanX = lowerArch.Average(p => p.X);
                bool isCuspRight = (midMaxX > upperArchMeanX + w * 0.10f) && (midMaxX > lowerArchMeanX + w * 0.10f);
                return isCuspRight && (topEnd.X < midMaxX - w * 0.30f) && (botEnd.X < midMaxX - w * 0.30f);
            }
        }
    }

    /// <summary>
    /// Phân loại nhóm cú pháp toán học cho các token ký hiệu.
    /// </summary>
    public enum MathTokenType
    {
        Digit,
        Variable,
        BinaryOp,
        PrefixFunc,
        OpenBracket,
        CloseBracket,
        Other
    }

    /// <summary>
    /// Bộ lọc tiên nghiệm cú pháp toán học (Math Syntax Prior & Grammar Disambiguation).
    /// Áp dụng các ràng buộc toán học để loại bỏ trạng thái vô nghĩa và tự sửa lỗi nhầm nét:
    /// 1. Khử hai toán tử hai ngôi đứng liền kề (+ + -> chọn biến thể tiếp theo).
    /// 2. Sửa lỗi số kẹp giữa 2 biến (x 7 y -> x + y).
    /// 3. Cân bằng ngoặc mở/đóng (Bracket Balance).
    /// </summary>
    public static class MathSyntaxPrior
    {
        private static readonly HashSet<string> Digits = new HashSet<string>(new[]
        {
            "0", "1", "2", "3", "4", "5", "6", "7", "8", "9"
        });

        private static readonly HashSet<string> Variables = new HashSet<string>(new[]
        {
            "x", "y", "z", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j",
            "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w",
            "A", "C", "G", "H", "M", "N", "R", "S", "T",
            "\\alpha", "\\beta", "\\gamma", "\\theta", "\\pi", "\\infty",
            "\\Delta", "\\lambda", "\\mu", "\\phi", "\\sigma"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> BinaryOps = new HashSet<string>(new[]
        {
            "+", "-", "=", "\\times", "/", "\\div", "\\pm", "<", ">",
            "\\le", "\\ge", "\\neq", "\\to", "\\in"
        });

        private static readonly HashSet<string> PrefixFuncs = new HashSet<string>(new[]
        {
            "\\sqrt", "\\int", "\\sum", "\\sin", "\\cos", "\\tan", "\\log", "\\ln", "\\lim"
        });

        public static MathTokenType GetTokenType(string token)
        {
            if (string.IsNullOrEmpty(token)) return MathTokenType.Other;
            if (Digits.Contains(token)) return MathTokenType.Digit;
            if (Variables.Contains(token)) return MathTokenType.Variable;
            if (BinaryOps.Contains(token)) return MathTokenType.BinaryOp;
            if (PrefixFuncs.Contains(token)) return MathTokenType.PrefixFunc;
            if (token is "(" or "[" or "{") return MathTokenType.OpenBracket;
            if (token is ")" or "]" or "}") return MathTokenType.CloseBracket;
            return MathTokenType.Other;
        }

        public static bool IsBinaryOp(string token) => BinaryOps.Contains(token);
        public static bool IsVariable(string token) => Variables.Contains(token);
        public static bool IsDigit(string token) => Digits.Contains(token);
        public static bool IsTerm(string token) => IsVariable(token) || IsDigit(token) || token is ")" or "]";

        /// <summary>
        /// Áp dụng các ràng buộc cú pháp toán học để khử nhiễu và tự sửa lỗi nhầm nhãn.
        /// </summary>
        public static List<(string charLabel, List<string> alts, RectangleF box)> DisambiguateTokens(
            List<(string charLabel, List<string> alts, RectangleF box)> tokens)
        {
            if (tokens == null || tokens.Count <= 1) return tokens ?? new List<(string, List<string>, RectangleF)>();

            var refined = new List<(string charLabel, List<string> alts, RectangleF box)>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                refined.Add((tokens[i].charLabel, new List<string>(tokens[i].alts), tokens[i].box));
            }

            // Quy tắc 1: Ký tự gây nhầm lẫn kẹp giữa 2 số hạng / biến (ví dụ x 7 y -> x + y, hoặc x^2 x y -> x^2 + y)
            for (int i = 1; i < refined.Count - 1; i++)
            {
                var prev = refined[i - 1];
                var cur = refined[i];
                var next = refined[i + 1];

                bool prevIsTerm = IsTerm(prev.charLabel);
                bool nextIsTerm = IsTerm(next.charLabel);
                bool curIsNotOp = !IsBinaryOp(cur.charLabel);

                if (prevIsTerm && nextIsTerm && curIsNotOp)
                {
                    bool isAmbiguousShape = cur.charLabel is "7" or "t";
                    if (isAmbiguousShape)
                    {
                        string? opAlt = cur.alts.FirstOrDefault(a => IsBinaryOp(a));
                        if (opAlt != null)
                        {
                            var newAlts = new List<string>(cur.alts);
                            newAlts.Remove(opAlt);
                            newAlts.Insert(0, cur.charLabel);
                            refined[i] = (opAlt, newAlts, cur.box);
                        }
                    }
                }
            }

            // Quy tắc 2: Hai toán tử hai ngôi đứng liền kề (ví dụ: + + hoặc + =)
            for (int i = 0; i < refined.Count - 1; i++)
            {
                var cur = refined[i];
                var next = refined[i + 1];

                if (IsBinaryOp(cur.charLabel) && IsBinaryOp(next.charLabel))
                {
                    // Thử chuyển next sang ứng viên phi-toán-tử (ví dụ + biến thành t)
                    string? nonOpAlt = next.alts.FirstOrDefault(a => !IsBinaryOp(a));
                    if (nonOpAlt != null)
                    {
                        var newAlts = new List<string>(next.alts);
                        newAlts.Remove(nonOpAlt);
                        newAlts.Insert(0, next.charLabel);
                        refined[i + 1] = (nonOpAlt, newAlts, next.box);
                    }
                    else
                    {
                        string? curNonOp = cur.alts.FirstOrDefault(a => !IsBinaryOp(a));
                        if (curNonOp != null)
                        {
                            var newAlts = new List<string>(cur.alts);
                            newAlts.Remove(curNonOp);
                            newAlts.Insert(0, cur.charLabel);
                            refined[i] = (curNonOp, newAlts, cur.box);
                        }
                    }
                }
            }

            return refined;
        }

        /// <summary>
        /// Cân bằng các cặp ngoặc mở chưa có ngoặc đóng.
        /// </summary>
        public static string BalanceBrackets(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return expr;

            int roundOpen = 0, roundClose = 0;
            int squareOpen = 0, squareClose = 0;

            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (c == '(') roundOpen++;
                else if (c == ')') roundClose++;
                else if (c == '[') squareOpen++;
                else if (c == ']') squareClose++;
            }

            var sb = new StringBuilder(expr);
            while (roundClose < roundOpen)
            {
                sb.Append(")");
                roundClose++;
            }
            while (squareClose < squareOpen)
            {
                sb.Append("]");
                squareClose++;
            }

            return sb.ToString();
        }
    }
}
