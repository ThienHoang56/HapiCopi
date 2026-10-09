using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public class FormulaRecord
    {
        public string Hash { get; set; } = "";
        public string Latex { get; set; } = "";
        public float FontSize { get; set; } = 11f;
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public double AspectRatio => PixelHeight > 0 ? (double)PixelWidth / PixelHeight : 1.0;
        public DateTime RegisteredTime { get; set; } = DateTime.Now;
    }

    public static class FormulaTracker
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, FormulaRecord> _byHash = new Dictionary<string, FormulaRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<FormulaRecord> _recentList = new List<FormulaRecord>();
        private const int MaxRecent = 100;

        public static void Register(string base64Png, string latex, float fontSize = 11f, float widthPt = 0, float heightPt = 0)
        {
            if (string.IsNullOrEmpty(base64Png) || string.IsNullOrEmpty(latex)) return;

            try
            {
                string raw = base64Png;
                if (raw.Contains(",")) raw = raw.Substring(raw.IndexOf(",") + 1);
                byte[] bytes = Convert.FromBase64String(raw);

                int w = 0, h = 0;
                try
                {
                    using var ms = new MemoryStream(bytes);
                    using var img = Image.FromStream(ms);
                    w = img.Width;
                    h = img.Height;
                }
                catch { }

                string hash = ComputeHash(bytes);
                var rec = new FormulaRecord
                {
                    Hash = hash,
                    Latex = latex,
                    FontSize = fontSize,
                    WidthPt = widthPt,
                    HeightPt = heightPt,
                    PixelWidth = w,
                    PixelHeight = h,
                    RegisteredTime = DateTime.Now
                };

                lock (_lock)
                {
                    _byHash[hash] = rec;
                    _recentList.Insert(0, rec);
                    while (_recentList.Count > MaxRecent)
                    {
                        var lastRec = _recentList[_recentList.Count - 1];
                        _recentList.RemoveAt(_recentList.Count - 1);
                        if (!string.IsNullOrEmpty(lastRec.Hash))
                        {
                            _byHash.Remove(lastRec.Hash);
                        }
                    }
                }
                Program.Log($"[FormulaTracker] Đã đăng ký nhận diện công thức: '{latex}' ({w}x{h} px, hash={hash.Substring(0, 8)}...)");
            }
            catch (Exception ex)
            {
                Program.Log($"[FormulaTracker] Lỗi đăng ký công thức: {ex.Message}");
            }
        }

        public static void ScanAndRegisterHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return;

            // Fast-path: Bỏ qua nếu HTML không chứa dấu hiệu nhận biết công thức của HapiCopi
            if (html.IndexOf("[HapiCopi:Math]", StringComparison.OrdinalIgnoreCase) < 0 &&
                html.IndexOf("data-latex", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            try
            {
                var imgMatches = Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase);
                foreach (Match imgM in imgMatches)
                {
                    string tag = imgM.Value;
                    var srcM = Regex.Match(tag, @"src=""(data:image\/png;base64,[^""]+)""", RegexOptions.IgnoreCase);
                    var mathM = Regex.Match(tag, @"(?:alt|title|data-latex)=""(?:\[HapiCopi:Math\]\s*)?([^""]+)""", RegexOptions.IgnoreCase);
                    if (srcM.Success && mathM.Success)
                    {
                        string src = srcM.Groups[1].Value;
                        string latex = System.Net.WebUtility.HtmlDecode(mathM.Groups[1].Value);
                        Register(src, latex);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[FormulaTracker] Lỗi quét HTML: {ex.Message}");
            }
        }

        public static bool TryIdentifyFormula(byte[]? pngBytes, Image? clipboardImage, out string formula, out float fontSize)
        {
            formula = "";
            fontSize = 11f;

            int byteLen = pngBytes?.Length ?? 0;
            int imgW = clipboardImage?.Width ?? 0;
            int imgH = clipboardImage?.Height ?? 0;
            int cacheCount = 0;
            lock (_lock) { cacheCount = _recentList.Count; }

            Program.Log($"[FormulaTracker] Đang kiểm tra nhận diện ảnh ({byteLen} bytes, {imgW}x{imgH} px). Số công thức trong cache: {cacheCount}");

            if (pngBytes != null && pngBytes.Length > 0)
            {
                // 1. Kiểm tra PNG tEXt chunk
                string? textChunk = PngChunkReader.ExtractTextChunk(pngBytes, "HapiCopi:Math");
                if (!string.IsNullOrEmpty(textChunk))
                {
                    formula = textChunk;
                    Program.Log($"[FormulaTracker] Nhận diện công thức từ PNG tEXt chunk: {formula}");
                    return true;
                }

                // 2. Kiểm tra SHA256 byte chính xác
                string hash = ComputeHash(pngBytes);
                lock (_lock)
                {
                    if (_byHash.TryGetValue(hash, out var rec))
                    {
                        formula = rec.Latex;
                        fontSize = rec.FontSize;
                        Program.Log($"[FormulaTracker] Nhận diện công thức từ SHA256 cache: {formula}");
                        return true;
                    }
                }
            }

            // 3. Kiểm tra kích thước và tỷ lệ khung hình (dành cho trường hợp Google Docs recompress ảnh PNG)
            if (imgW == 0 && clipboardImage != null)
            {
                imgW = clipboardImage.Width;
                imgH = clipboardImage.Height;
            }
            if (imgW == 0 && pngBytes != null && pngBytes.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(pngBytes);
                    using var img = Image.FromStream(ms);
                    imgW = img.Width;
                    imgH = img.Height;
                }
                catch { }
            }

            if (imgW > 0 && imgH > 0)
            {
                double targetRatio = (double)imgW / imgH;
                lock (_lock)
                {
                    foreach (var rec in _recentList)
                    {
                        // Nếu kích thước pixel trùng khớp hoàn toàn
                        if (rec.PixelWidth == imgW && rec.PixelHeight == imgH)
                        {
                            formula = rec.Latex;
                            fontSize = rec.FontSize;
                            Program.Log($"[FormulaTracker] Nhận diện công thức từ kích thước pixel ({imgW}x{imgH}): {formula}");
                            return true;
                        }

                        // Hoặc tỷ lệ khung hình sai lệch dưới 1.5% và thời gian đăng ký trong vòng 2 giờ
                        if (Math.Abs(rec.AspectRatio - targetRatio) < 0.015 &&
                            (DateTime.Now - rec.RegisteredTime).TotalHours < 2)
                        {
                            formula = rec.Latex;
                            fontSize = rec.FontSize;
                            Program.Log($"[FormulaTracker] Nhận diện công thức từ AspectRatio ({targetRatio:F3}): {formula}");
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string ComputeHash(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
