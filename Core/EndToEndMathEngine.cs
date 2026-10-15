using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HapiCopi
{
    /// <summary>
    /// Bộ máy nhận diện công thức toán viết tay End-to-End Sequence (A4 - Vision Transformer / BTTR).
    /// Nhận toàn bộ ảnh công thức 2D và giải mã trực tiếp thành chuỗi mã LaTeX chuẩn,
    /// không phân mảnh nét vẽ, không dùng heuristic cắt dán hộp bao (Bounding Box).
    /// </summary>
    public static class EndToEndMathEngine
    {
        private static readonly object _lock = new object();
        private static InferenceSession? _encoderSession;
        private static InferenceSession? _decoderSession;
        private static string[] _vocab = Array.Empty<string>();
        private static bool _initialized = false;
        private static bool _initFailed = false;

        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return _encoderSession != null && _decoderSession != null && _vocab.Length > 0;
            }
        }

        public static string AppBaseModelDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

        public static string DefaultModelDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HapiCopi", "models");

        public static bool EnsureInitialized()
        {
            if (_initialized) return true;
            if (_initFailed) return false;

            lock (_lock)
            {
                if (_initialized) return true;
                if (_initFailed) return false;

                try
                {
                    string encPath = FindModelFile("math_encoder.onnx");
                    string decPath = FindModelFile("math_decoder.onnx");
                    string vocabPath = FindModelFile("math_e2e_vocab.json");

                    if (string.IsNullOrEmpty(encPath) || string.IsNullOrEmpty(decPath) || string.IsNullOrEmpty(vocabPath))
                    {
                        Program.Log("[E2E Math] Chưa tìm thấy đủ file weights math_encoder.onnx, math_decoder.onnx hoặc math_e2e_vocab.json.");
                        _initFailed = true;
                        return false;
                    }

                    // Đọc từ điển
                    string vocabJson = File.ReadAllText(vocabPath);
                    using var doc = JsonDocument.Parse(vocabJson);
                    if (doc.RootElement.TryGetProperty("tokens", out var tokensElem))
                    {
                        _vocab = tokensElem.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    }

                    if (_vocab.Length == 0)
                    {
                        Program.Log("[E2E Math] Lỗi: Từ điển math_e2e_vocab.json trống.");
                        _initFailed = true;
                        return false;
                    }

                    var sessionOptions = new SessionOptions
                    {
                        IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
                    };

                    _encoderSession = new InferenceSession(encPath, sessionOptions);
                    _decoderSession = new InferenceSession(decPath, sessionOptions);

                    _initialized = true;
                    Program.Log($"[E2E Math] Đã khởi tạo thành công End-to-End Engine: Encoder={Path.GetFileName(encPath)}, Decoder={Path.GetFileName(decPath)}, Vocab={_vocab.Length} tokens.");
                    return true;
                }
                catch (Exception ex)
                {
                    _initFailed = true;
                    Program.Log($"[E2E Math LỖI KHỞI TẠO] {ex.Message}");
                    return false;
                }
            }
        }

        private static string FindModelFile(string fileName)
        {
            string[] candidates = new[]
            {
                Path.Combine(AppBaseModelDir, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "models", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName),
                Path.Combine(DefaultModelDir, fileName)
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }
            return "";
        }

        /// <summary>
        /// Nháº­n diá»‡n toÃ n bá»™ cÃ´ng thá»©c toÃ¡n tá»« danh sÃ¡ch nÃ©t váº½ vector thÃ nh chuá»—i mÃ£ LaTeX chuáº©n.
        /// </summary>
        public static (bool success, string latex, long elapsedMs) RecognizeStrokes(List<List<PointF>> strokes)
        {
            if (strokes == null || strokes.Count == 0)
            {
                return (false, "", 0);
            }

            try
            {
                var (imgTensor, maskTensor, _, _) = RasterizeStrokesToTensor(strokes);
                return RecognizeTensor(imgTensor, maskTensor);
            }
            catch (Exception ex)
            {
                Program.Log($"[E2E Math Rasterize Lá»–I] {ex.Message}");
                return (false, "", 0);
            }
        }

        /// <summary>
        /// Nháº­n diá»‡n cÃ´ng thá»©c toÃ¡n tá»« file áº£nh trÃªn Ä‘Ä©a.
        /// </summary>
        public static (bool success, string latex, long elapsedMs) RecognizeImageFile(string filePath)
        {
            if (!File.Exists(filePath)) return (false, "", 0);
            try
            {
                using var bmp = new Bitmap(filePath);
                return RecognizeBitmap(bmp);
            }
            catch (Exception ex)
            {
                Program.Log($"[E2E Math File Lá»–I] {ex.Message}");
                return (false, "", 0);
            }
        }

        /// <summary>
        /// Nháº­n diá»‡n cÃ´ng thá»©c toÃ¡n trá»±c tiáº¿p tá»« Bitmap.
        /// </summary>
        public static (bool success, string latex, long elapsedMs) RecognizeBitmap(Bitmap bmp)
        {
            if (bmp == null) return (false, "", 0);
            try
            {
                var (imgTensor, maskTensor) = BitmapToTensor(bmp);
                return RecognizeTensor(imgTensor, maskTensor);
            }
            catch (Exception ex)
            {
                Program.Log($"[E2E Math Bitmap Lá»–I] {ex.Message}");
                return (false, "", 0);
            }
        }

        /// <summary>
        /// Thá»±c hiá»‡n suy luáº­n End-to-End Encoder-Decoder trÃªn tensor áº£nh Ä‘Ã£ chuáº©n hÃ³a.
        /// </summary>
        public static (bool success, string latex, long elapsedMs) RecognizeTensor(DenseTensor<float> imgTensor, DenseTensor<bool> maskTensor)
        {
            if (!EnsureInitialized() || _encoderSession == null || _decoderSession == null)
            {
                return (false, "", 0);
            }

            var sw = Stopwatch.StartNew();

            try
            {
                // 1. Cháº¡y Encoder trÃ­ch xuáº¥t Ä‘áº·c trÆ°ng báº£n Ä‘á»“ 2D
                var encInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("img", imgTensor),
                    NamedOnnxValue.CreateFromTensor("img_mask", maskTensor)
                };

                using var encResults = _encoderSession.Run(encInputs);
                var featTensor = encResults.First(v => v.Name == "feature").AsTensor<float>();

                // 2. Giáº£i mÃ£ tá»± há»“i quy (Autoregressive Greedy Decoding)
                var generatedTokens = new List<long> { 1 }; // 1 = <sos>
                var decodedLatexTokens = new List<string>();

                int maxTokens = 120;
                for (int step = 0; step < maxTokens; step++)
                {
                    var tgtTensor = new DenseTensor<long>(new[] { 1, generatedTokens.Count });
                    for (int i = 0; i < generatedTokens.Count; i++)
                    {
                        tgtTensor[0, i] = generatedTokens[i];
                    }

                    var decInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("src", featTensor),
                        NamedOnnxValue.CreateFromTensor("tgt", tgtTensor)
                    };

                    using var decResults = _decoderSession.Run(decInputs);
                    var logitsTensor = decResults.First(v => v.Name == "logits").AsTensor<float>();

                    // TÃ¬m token cÃ³ logit cao nháº¥t (ArgMax)
                    int vocabSize = _vocab.Length;
                    int bestToken = 2; // default <eos>
                    float bestScore = float.NegativeInfinity;

                    for (int v = 0; v < vocabSize; v++)
                    {
                        float score = logitsTensor[0, v];
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestToken = v;
                        }
                    }

                    if (bestToken == 2) // 2 = <eos>
                    {
                        break;
                    }

                    generatedTokens.Add(bestToken);
                    if (bestToken >= 0 && bestToken < _vocab.Length)
                    {
                        string word = _vocab[bestToken];
                        if (word != "<pad>" && word != "<sos>" && word != "<eos>")
                        {
                            decodedLatexTokens.Add(word);
                        }
                    }
                }

                sw.Stop();

                // 3. Háº­u xá»­ lÃ½ chuá»—i LaTeX (Format clean)
                string rawLatex = string.Join(" ", decodedLatexTokens);
                string cleanLatex = PostProcessLatex(rawLatex);

                return (true, cleanLatex, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Program.Log($"[E2E Math Suy Luáº­n Lá»–I] {ex.Message}");
                return (false, "", sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Rasterize nét vẽ vector thành DenseTensor [1, 1, H, W] chuẩn nền đen nét trắng (0.0f nền, 1.0f nét).
        /// </summary>
        private static (DenseTensor<float> img, DenseTensor<bool> mask, int h, int w) RasterizeStrokesToTensor(List<List<PointF>> strokes)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var s in strokes)
            {
                foreach (var p in s)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }

            float strokeW = Math.Max(10f, maxX - minX);
            float strokeH = Math.Max(10f, maxY - minY);

            // Scale chiều cao chuẩn ~80-96 px (khớp với phân bố tập huấn luyện CROHME)
            float targetH = 96f;
            float scale = targetH / strokeH;
            float scaledW = strokeW * scale;

            int pad = 16;
            int finalH = (int)Math.Ceiling(targetH) + pad * 2;
            int finalW = Math.Max(64, (int)Math.Ceiling(scaledW) + pad * 2);

            // Đảm bảo kích thước là bội số của 8 để tương thích tối đa với pooling của DenseNet
            finalH = ((finalH + 7) / 8) * 8;
            finalW = ((finalW + 7) / 8) * 8;

            using var bmp = new Bitmap(finalW, finalH);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Black);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float penWidth = Math.Max(3.2f, 4.0f);
            using var pen = new Pen(Color.White, penWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            foreach (var s in strokes)
            {
                if (s.Count == 0) continue;
                if (s.Count == 1)
                {
                    float sx = pad + (s[0].X - minX) * scale;
                    float sy = pad + (s[0].Y - minY) * scale;
                    g.FillEllipse(Brushes.White, sx - 2f, sy - 2f, 4f, 4f);
                    continue;
                }

                var pts = s.Select(p => new PointF(pad + (p.X - minX) * scale, pad + (p.Y - minY) * scale)).ToArray();
                g.DrawLines(pen, pts);
            }

            // Chuyển pixel sang DenseTensor float32
            var imgTensor = new DenseTensor<float>(new[] { 1, 1, finalH, finalW });
            var maskTensor = new DenseTensor<bool>(new[] { 1, finalH, finalW });

            for (int y = 0; y < finalH; y++)
            {
                for (int x = 0; x < finalW; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    float val = c.R / 255.0f;
                    imgTensor[0, 0, y, x] = val;
                    maskTensor[0, y, x] = false; // Không padding, toàn bộ canvas đều hợp lệ
                }
            }

            return (imgTensor, maskTensor, finalH, finalW);
        }

        /// <summary>
        /// Chuáº©n hÃ³a Bitmap báº¥t ká»³ (tá»± Ä‘á»™ng phÃ¡t hiá»‡n ná» n sÃ¡ng/tá»‘i, crop vÃ¹ng nÃ©t váº½, scale height 96px, pad 8x8) thÃ nh DenseTensor.
        /// </summary>
        public static (DenseTensor<float> img, DenseTensor<bool> mask) BitmapToTensor(Bitmap srcBmp)
        {
            int w = srcBmp.Width;
            int h = srcBmp.Height;

            // 1. Kiá»ƒm tra mÃ u ná» n dá»±a trÃªn cÃ¡c pixel viá» n ngoÃ i (ná» n tráº¯ng hay ná» n Ä‘en)
            long borderLumaSum = 0;
            int borderSamples = 0;
            for (int x = 0; x < w; x += Math.Max(1, w / 20))
            {
                borderLumaSum += (srcBmp.GetPixel(x, 0).R + srcBmp.GetPixel(x, h - 1).R);
                borderSamples += 2;
            }
            for (int y = 0; y < h; y += Math.Max(1, h / 20))
            {
                borderLumaSum += (srcBmp.GetPixel(0, y).R + srcBmp.GetPixel(w - 1, y).R);
                borderSamples += 2;
            }
            bool isDarkInkOnLightBg = (borderSamples > 0 && (borderLumaSum / borderSamples) > 128);

            // 2. TÃ¬m Bounding Box cá»§a nÃ©t váº½
            int minX = w, maxX = 0, minY = h, maxY = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = srcBmp.GetPixel(x, y);
                    int gray = (c.R + c.G + c.B) / 3;
                    bool isForeground = isDarkInkOnLightBg ? (gray < 200) : (gray > 50);
                    if (isForeground)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (minX > maxX || minY > maxY)
            {
                return (new DenseTensor<float>(new[] { 1, 1, 96, 96 }), new DenseTensor<bool>(new[] { 1, 96, 96 }));
            }

            int strokeW = Math.Max(10, maxX - minX + 1);
            int strokeH = Math.Max(10, maxY - minY + 1);

            // 3. Chuáº©n hÃ³a chiá» u cao nÃ©t váº½ vá»  ~96px, giá»¯ nguyÃªn tá»· lá»‡ khung hÃ¬nh
            float targetH = 96.0f;
            float scale = targetH / strokeH;
            int scaledW = (int)Math.Max(32, Math.Round(strokeW * scale));
            int scaledH = (int)Math.Round(strokeH * scale);

            int pad = 16;
            int finalH = ((scaledH + pad * 2 + 7) / 8) * 8;
            int finalW = Math.Max(64, ((scaledW + pad * 2 + 7) / 8) * 8);

            using var renderedBmp = new Bitmap(finalW, finalH);
            using (var g = Graphics.FromImage(renderedBmp))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var srcRect = new Rectangle(minX, minY, strokeW, strokeH);
                var dstRect = new Rectangle(pad, pad, scaledW, scaledH);

                using var cropped = new Bitmap(strokeW, strokeH);
                using (var cg = Graphics.FromImage(cropped))
                {
                    cg.DrawImage(srcBmp, new Rectangle(0, 0, strokeW, strokeH), srcRect, GraphicsUnit.Pixel);
                }

                for (int y = 0; y < strokeH; y++)
                {
                    for (int x = 0; x < strokeW; x++)
                    {
                        Color c = cropped.GetPixel(x, y);
                        int gray = (c.R + c.G + c.B) / 3;
                        int finalVal = isDarkInkOnLightBg ? (255 - gray) : gray;
                        cropped.SetPixel(x, y, Color.FromArgb(finalVal, finalVal, finalVal));
                    }
                }

                g.DrawImage(cropped, dstRect);
            }

            // 4. Ä á»• pixel vÃ o DenseTensor float32 [1, 1, H, W]
            var imgTensor = new DenseTensor<float>(new[] { 1, 1, finalH, finalW });
            var maskTensor = new DenseTensor<bool>(new[] { 1, finalH, finalW });

            for (int y = 0; y < finalH; y++)
            {
                for (int x = 0; x < finalW; x++)
                {
                    Color c = renderedBmp.GetPixel(x, y);
                    imgTensor[0, 0, y, x] = c.R / 255.0f;
                    maskTensor[0, y, x] = false;
                }
            }

            return (imgTensor, maskTensor);
        }

        /// <summary>
        /// Chuáº©n hÃ³a chuá»—i LaTeX tá»« cÃ¡c token Ä‘Ã£ Ä‘Æ°á»£c giáº£i mÃ£.
        /// Chá»‰ thá»±c hiá»‡n format cÃº phÃ¡p LaTeX chuáº©n (dáº¥u ngoáº·c, khoáº£ng tráº¯ng),
        /// TUYá»†T Äá»I KHÃ”NG can thiá»‡p rule-base trÃªn tá»«ng kÃ½ tá»± hay cÃ´ng thá»©c cá»¥ thá»ƒ.
        /// </summary>
        public static string PostProcessLatex(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            string s = raw.Trim();

            // 1. Chuan hoa cac dau ngoac va chi so tren/duoi co ban
            s = s.Replace(" {", "{")
                 .Replace("{ ", "{")
                 .Replace(" }", "}")
                 .Replace(" ^ ", "^")
                 .Replace(" _ ", "_")
                 .Replace("( ", "(")
                 .Replace(" )", ")");

            // 2. Format chuan cu phap ten moi truong LaTeX: \begin{m a t r i x} -> \begin{matrix}, \end{m a t r i x} -> \end{matrix}
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\\(begin|end)\{([^}]+)\}", m =>
            {
                string envType = m.Groups[1].Value;
                string envName = m.Groups[2].Value.Replace(" ", "");
                return $"\\{envType}{{{envName}}}";
            });

            // 3. Chuan hoa cu phap toan tu co can (Canonicalization cho Integral, Sum, Prod, Lim...):
            // Mo hinh Vision Transformer doi khi sinh hai khoi dong mo ngoac {can_duoi}{can_tren} ma khong co _ va ^.
            // Cu phap LaTeX chuan cho can toan tu bat buoc phai la: \op_{can_duoi}^{can_tren}
            const string BalancedBlock = @"\{((?>[^{}]+|\{(?<D>)|\}(?<-D>))*(?(D)(?!)))\}";

            // 3.1. Hai khoi ngoac lien tiep sau toan tu: \int{2}{4} -> \int_{2}^{4}, \sum{i=1}{n} -> \sum_{i=1}^{n}
            string twoBlocksPattern = $@"\\(int|iint|iiint|oint|sum|prod|coprod|bigcap|bigcup)\s*{BalancedBlock}\s*{BalancedBlock}";
            s = System.Text.RegularExpressions.Regex.Replace(s, twoBlocksPattern, m =>
                $"\\{m.Groups[1].Value}_{{{m.Groups[2].Value}}}^{{{m.Groups[3].Value}}}");

            // 3.2. Can duoi da co _ nhung can tren thieu ^: \int_{2}{4} -> \int_{2}^{4}
            string subOnlyPattern = $@"\\(int|iint|iiint|oint|sum|prod|coprod|bigcap|bigcup)\s*_{BalancedBlock}\s*{BalancedBlock}";
            s = System.Text.RegularExpressions.Regex.Replace(s, subOnlyPattern, m =>
                $"\\{m.Groups[1].Value}_{{{m.Groups[2].Value}}}^{{{m.Groups[3].Value}}}");

            // 3.3. Can tren da co ^ nhung can duoi thieu _: \int^{4}{2} -> \int^{4}_{2}
            string supOnlyPattern = $@"\\(int|iint|iiint|oint|sum|prod|coprod|bigcap|bigcup)\s*\^{BalancedBlock}\s*{BalancedBlock}";
            s = System.Text.RegularExpressions.Regex.Replace(s, supOnlyPattern, m =>
                $"\\{m.Groups[1].Value}^{{{m.Groups[2].Value}}}_{{{m.Groups[3].Value}}}");

            // 3.4. Toan tu gioi han chi co mot can duoi: \lim{x -> 0} -> \lim_{x -> 0}
            string limPattern = $@"\\lim\s*{BalancedBlock}";
            s = System.Text.RegularExpressions.Regex.Replace(s, limPattern, m =>
                $"\\lim_{{{m.Groups[1].Value}}}");

            // 4. Rut gon khoang trang thua
            while (s.Contains("  "))
            {
                s = s.Replace("  ", " ");
            }

            return s.Trim();
        }
    }
}
