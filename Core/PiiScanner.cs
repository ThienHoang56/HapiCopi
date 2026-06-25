using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    /// <summary>
    /// Kết quả quét của một thông tin nhạy cảm được phát hiện.
    /// </summary>
    public class PiiMatch
    {
        public string Type { get; set; } = "";       // Ví dụ: "API Key", "Phone", "Email", "Credit Card", "CCCD", "Secret (Entropy)"
        public string Value { get; set; } = "";       // Giá trị gốc tìm thấy
        public string MaskedValue { get; set; } = ""; // Giá trị đã che (ví dụ: AIza****Dkla)
        public int StartIndex { get; set; }           // Vị trí bắt đầu trong chuỗi gốc
        public int Length { get; set; }               // Độ dài chuỗi gốc
    }

    /// <summary>
    /// Kết quả tổng hợp sau khi quét toàn bộ văn bản.
    /// </summary>
    public class PiiScanResult
    {
        public List<PiiMatch> Matches { get; set; } = new List<PiiMatch>();
        public bool HasPii => Matches.Count > 0;
        public bool HasCriticalPii => Matches.Any(m =>
            m.Type == "API Key" || m.Type == "Secret (Entropy)" || m.Type == "Credit Card" || m.Type == "Connection String");

        /// <summary>
        /// Trả về chuỗi đã che toàn bộ thông tin nhạy cảm.
        /// </summary>
        public string GetSanitizedText(string originalText)
        {
            if (!HasPii || string.IsNullOrEmpty(originalText)) return originalText;

            char[] chars = originalText.ToCharArray();
            // Sắp xếp matches theo vị trí từ cuối lên đầu để tránh lệch index
            foreach (var match in Matches.OrderByDescending(m => m.StartIndex))
            {
                if (match.StartIndex >= 0 && match.StartIndex + match.Length <= chars.Length)
                {
                    string masked = match.MaskedValue;
                    string before = new string(chars, 0, match.StartIndex);
                    string after = new string(chars, match.StartIndex + match.Length, chars.Length - match.StartIndex - match.Length);
                    string newText = before + masked + after;
                    chars = newText.ToCharArray();
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Mô tả tóm tắt các loại PII tìm thấy cho Toast notification.
        /// </summary>
        public string GetSummary()
        {
            var types = Matches.Select(m => m.Type).Distinct().ToList();
            if (types.Count == 0) return "";
            return string.Join(", ", types);
        }
    }

    /// <summary>
    /// Bộ quét thông tin nhạy cảm cục bộ (PII Scanner).
    /// Chạy hoàn toàn offline bằng Regex + Shannon Entropy + Composite detection.
    /// Tốc độ xử lý: dưới 1ms cho văn bản thông thường.
    /// </summary>
    public static class PiiScanner
    {
        // =============================================
        // ENGINE 1: Regex Pattern Matching
        // =============================================

        // API Keys — Định dạng cố định, không bao giờ xuất hiện trong văn bản tự nhiên
        private static readonly Regex GeminiKeyRegex = new Regex(
            @"AIzaSy[A-Za-z0-9_-]{33}",
            RegexOptions.Compiled);

        private static readonly Regex OpenAiKeyRegex = new Regex(
            @"sk-(proj-)?[a-zA-Z0-9_-]{32,}",
            RegexOptions.Compiled);

        private static readonly Regex GitHubTokenRegex = new Regex(
            @"gh[ps]_[A-Za-z0-9_]{36,}",
            RegexOptions.Compiled);

        private static readonly Regex AwsKeyRegex = new Regex(
            @"AKIA[0-9A-Z]{16}",
            RegexOptions.Compiled);

        private static readonly Regex GenericBearerTokenRegex = new Regex(
            @"(?i)Bearer\s+[A-Za-z0-9_\-\.]{20,}",
            RegexOptions.Compiled);

        // Connection Strings
        private static readonly Regex ConnectionStringRegex = new Regex(
            @"(?i)(Server|Host|Data\s*Source)\s*=\s*[^;]+;\s*.*(Password|Pwd)\s*=\s*[^;]+",
            RegexOptions.Compiled);

        // Số điện thoại Việt Nam
        private static readonly Regex PhoneVnRegex = new Regex(
            @"(?<!\d)(?:\+84|0)(3|5|7|8|9)\d{8}(?!\d)",
            RegexOptions.Compiled);

        // Email
        private static readonly Regex EmailRegex = new Regex(
            @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled);

        // Số thẻ tín dụng (16 chữ số, có thể có dấu cách/gạch ngang)
        private static readonly Regex CreditCardRegex = new Regex(
            @"(?<!\d)\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}(?!\d)",
            RegexOptions.Compiled);

        // Số CCCD (12 chữ số) hoặc CMND (9 chữ số)
        private static readonly Regex CccdRegex = new Regex(
            @"(?<!\d)(0\d{2}[12349]\d{8})(?!\d)",
            RegexOptions.Compiled);

        private static readonly Regex CmndRegex = new Regex(
            @"(?<!\d)\d{9}(?!\d)",
            RegexOptions.Compiled);

        // =============================================
        // ENGINE 2: Shannon Entropy
        // =============================================

        private static readonly HashSet<string> EntropyContextKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "key", "api", "api_key", "apikey", "password", "passwd", "pwd", "pass",
            "secret", "token", "auth", "authorization", "credential", "credentials",
            "private", "private_key", "access_key", "access_token", "refresh_token",
            "client_secret", "client_id", "connection", "connectionstring"
        };

        /// <summary>
        /// Tính Shannon Entropy của một chuỗi ký tự.
        /// Chuỗi ngẫu nhiên (API key, mật khẩu) có entropy > 4.0
        /// Từ ngữ bình thường có entropy ~ 2.0 - 3.5
        /// </summary>
        public static double CalculateEntropy(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            var freq = new Dictionary<char, int>();
            foreach (char c in value)
            {
                if (!freq.ContainsKey(c)) freq[c] = 0;
                freq[c]++;
            }

            double entropy = 0;
            foreach (var kvp in freq)
            {
                double p = (double)kvp.Value / value.Length;
                entropy -= p * Math.Log2(p);
            }
            return entropy;
        }

        // =============================================
        // ENGINE 3: Luhn Algorithm (Thẻ tín dụng)
        // =============================================

        /// <summary>
        /// Kiểm tra tính hợp lệ của số thẻ tín dụng bằng thuật toán Luhn.
        /// </summary>
        private static bool PassesLuhnCheck(string digits)
        {
            // Loại bỏ dấu cách và gạch ngang
            string clean = digits.Replace(" ", "").Replace("-", "");
            if (clean.Length != 16 || !clean.All(char.IsDigit)) return false;

            int sum = 0;
            bool alternate = false;
            for (int i = clean.Length - 1; i >= 0; i--)
            {
                int n = clean[i] - '0';
                if (alternate)
                {
                    n *= 2;
                    if (n > 9) n -= 9;
                }
                sum += n;
                alternate = !alternate;
            }
            return (sum % 10 == 0);
        }

        // =============================================
        // QUÉT TỔNG HỢP
        // =============================================

        /// <summary>
        /// Quét toàn bộ văn bản để tìm thông tin nhạy cảm.
        /// Chạy hoàn toàn offline, tốc độ dưới 1ms.
        /// </summary>
        public static PiiScanResult Scan(string text)
        {
            var result = new PiiScanResult();
            if (string.IsNullOrWhiteSpace(text)) return result;

            // --- Engine 1: Regex ---

            // API Keys
            ScanWithRegex(result, text, GeminiKeyRegex, "API Key", MaskApiKey);
            ScanWithRegex(result, text, OpenAiKeyRegex, "API Key", MaskApiKey);
            ScanWithRegex(result, text, GitHubTokenRegex, "API Key", MaskApiKey);
            ScanWithRegex(result, text, AwsKeyRegex, "API Key", MaskApiKey);
            ScanWithRegex(result, text, GenericBearerTokenRegex, "API Key", MaskApiKey);

            // Connection Strings
            ScanWithRegex(result, text, ConnectionStringRegex, "Connection String", MaskConnectionString);

            // SĐT Việt Nam
            ScanWithRegex(result, text, PhoneVnRegex, "Số điện thoại", MaskPhone);

            // Email
            ScanWithRegex(result, text, EmailRegex, "Email", MaskEmail);

            // Thẻ tín dụng (kiểm tra Luhn)
            foreach (Match match in CreditCardRegex.Matches(text))
            {
                if (PassesLuhnCheck(match.Value))
                {
                    result.Matches.Add(new PiiMatch
                    {
                        Type = "Credit Card",
                        Value = match.Value,
                        MaskedValue = MaskCreditCard(match.Value),
                        StartIndex = match.Index,
                        Length = match.Length
                    });
                }
            }

            // CCCD (12 chữ số, bắt đầu bằng 0XX + chữ số giới tính)
            ScanWithRegex(result, text, CccdRegex, "CCCD", MaskCccd);

            // --- Engine 2: Entropy ---
            ScanHighEntropyTokens(result, text);

            return result;
        }

        private static void ScanWithRegex(PiiScanResult result, string text, Regex regex, string type, Func<string, string> maskFunc)
        {
            foreach (Match match in regex.Matches(text))
            {
                // Kiểm tra xem match này đã bị bao phủ bởi match trước đó chưa (tránh trùng lặp)
                bool alreadyCovered = result.Matches.Any(m =>
                    match.Index >= m.StartIndex && match.Index < m.StartIndex + m.Length);
                if (alreadyCovered) continue;

                result.Matches.Add(new PiiMatch
                {
                    Type = type,
                    Value = match.Value,
                    MaskedValue = maskFunc(match.Value),
                    StartIndex = match.Index,
                    Length = match.Length
                });
            }
        }

        /// <summary>
        /// Quét các token có entropy cao kết hợp với từ khóa ngữ cảnh nhạy cảm.
        /// </summary>
        private static void ScanHighEntropyTokens(PiiScanResult result, string text)
        {
            // Tách các "token" là chuỗi liền mạch (không chứa khoảng trắng) dài từ 16 ký tự trở lên
            var tokenRegex = new Regex(@"\S{16,}");
            foreach (Match match in tokenRegex.Matches(text))
            {
                string token = match.Value;

                // Bỏ qua nếu đã bị Engine 1 phát hiện
                bool alreadyCovered = result.Matches.Any(m =>
                    match.Index >= m.StartIndex && match.Index < m.StartIndex + m.Length);
                if (alreadyCovered) continue;

                // Bỏ qua URL
                if (token.StartsWith("http://") || token.StartsWith("https://") || token.StartsWith("file://")) continue;

                double entropy = CalculateEntropy(token);
                if (entropy < 4.2) continue;

                // Kiểm tra ngữ cảnh: tìm từ khóa nhạy cảm trong phạm vi ±80 ký tự
                int contextStart = Math.Max(0, match.Index - 80);
                int contextEnd = Math.Min(text.Length, match.Index + match.Length + 80);
                string context = text.Substring(contextStart, contextEnd - contextStart).ToLowerInvariant();

                bool hasContextKeyword = EntropyContextKeywords.Any(kw => context.Contains(kw));

                // Hoặc kiểm tra nếu token nằm trong pattern key=value hoặc key: value
                bool hasAssignment = Regex.IsMatch(
                    text.Substring(Math.Max(0, match.Index - 30), Math.Min(30, match.Index)),
                    @"(?i)\w+\s*[:=]\s*[""']?\s*$");

                if (hasContextKeyword || hasAssignment)
                {
                    result.Matches.Add(new PiiMatch
                    {
                        Type = "Secret (Entropy)",
                        Value = token,
                        MaskedValue = MaskApiKey(token),
                        StartIndex = match.Index,
                        Length = match.Length
                    });
                }
            }
        }

        // =============================================
        // HÀM CHE (MASKING)
        // =============================================

        private static string MaskApiKey(string value)
        {
            if (value.Length <= 8) return new string('*', value.Length);
            return value.Substring(0, 4) + new string('*', value.Length - 8) + value.Substring(value.Length - 4);
        }

        private static string MaskPhone(string value)
        {
            if (value.Length <= 4) return new string('*', value.Length);
            return value.Substring(0, 3) + new string('*', value.Length - 6) + value.Substring(value.Length - 3);
        }

        private static string MaskEmail(string value)
        {
            int atIndex = value.IndexOf('@');
            if (atIndex <= 1) return "***" + value.Substring(atIndex);
            return value[0] + new string('*', atIndex - 1) + value.Substring(atIndex);
        }

        private static string MaskCreditCard(string value)
        {
            string clean = value.Replace(" ", "").Replace("-", "");
            if (clean.Length < 8) return new string('*', clean.Length);
            return clean.Substring(0, 4) + " **** **** " + clean.Substring(clean.Length - 4);
        }

        private static string MaskCccd(string value)
        {
            if (value.Length <= 4) return new string('*', value.Length);
            return value.Substring(0, 3) + new string('*', value.Length - 6) + value.Substring(value.Length - 3);
        }

        private static string MaskConnectionString(string value)
        {
            // Che phần Password/Pwd
            return Regex.Replace(value, @"(?i)(Password|Pwd)\s*=\s*[^;]+", "$1=********");
        }
    }
}
