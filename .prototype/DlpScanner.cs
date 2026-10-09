using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HapiCopiLite
{
    public class DlpMatch
    {
        public string Type { get; set; } = "";
        public string RawValue { get; set; } = "";
        public string MaskedValue { get; set; } = "";
        public int Index { get; set; }
        public int Length { get; set; }
    }

    public class DlpResult
    {
        public List<DlpMatch> Matches { get; } = new();
        public bool HasPii => Matches.Count > 0;

        public string GetSanitizedText(string original)
        {
            if (!HasPii || string.IsNullOrEmpty(original)) return original;

            // Sắp xếp ngược từ cuối chuỗi lên đầu để không bị lệch index khi replace
            char[] chars = original.ToCharArray();
            foreach (var match in Matches.OrderByDescending(m => m.Index))
            {
                if (match.Index >= 0 && match.Index + match.Length <= chars.Length)
                {
                    string before = new string(chars, 0, match.Index);
                    string after = new string(chars, match.Index + match.Length, chars.Length - match.Index - match.Length);
                    string updated = before + match.MaskedValue + after;
                    chars = updated.ToCharArray();
                }
            }
            return new string(chars);
        }

        public string GetSummary()
        {
            var types = Matches.Select(m => m.Type).Distinct().ToList();
            return types.Count == 0 ? "Không có dữ liệu nhạy cảm" : string.Join(", ", types);
        }
    }

    /// <summary>
    /// Bộ máy quét DLP cục bộ (Local DLP): 100% Offline, chạy trên RAM, tốc độ < 1ms.
    /// Kết hợp 3 kỹ thuật: Regex + Shannon Entropy (Độ hỗn loạn) + Luhn Checksum.
    /// </summary>
    public static class DlpScanner
    {
        // 1. Regex cho các mẫu API key phổ biến
        private static readonly Regex OpenAiKeyRegex = new(@"sk-(?:proj-)?[a-zA-Z0-9_\-]{20,}", RegexOptions.Compiled);
        private static readonly Regex GeminiKeyRegex = new(@"AIzaSy[A-Za-z0-9_\-]{33}", RegexOptions.Compiled);
        private static readonly Regex GitHubTokenRegex = new(@"gh[ps]_[A-Za-z0-9_]{36,}", RegexOptions.Compiled);
        private static readonly Regex AwsKeyRegex = new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled);
        private static readonly Regex GenericBearerRegex = new(@"(?i)Bearer\s+([A-Za-z0-9_\-\.]{20,})", RegexOptions.Compiled);

        // 2. Regex thông tin cá nhân (PII)
        private static readonly Regex PhoneVnRegex = new(@"(?<!\d)(?:\+84|0)(3|5|7|8|9)\d{8}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
        private static readonly Regex CccdRegex = new(@"(?<!\d)(0\d{11}|\d{9})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex CreditCardRegex = new(@"(?<!\d)\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}(?!\d)", RegexOptions.Compiled);

        // 3. Từ khóa ngữ cảnh nhạy cảm cho Shannon Entropy
        private static readonly string[] SecretContextKeywords = new[]
        {
            "api_key", "apikey", "secret", "password", "passwd", "token", "auth", "credential"
        };

        public static DlpResult Scan(string text)
        {
            var result = new DlpResult();
            if (string.IsNullOrWhiteSpace(text)) return result;

            // Quét API Keys
            ScanRegex(result, text, OpenAiKeyRegex, "OpenAI API Key", MaskKey);
            ScanRegex(result, text, GeminiKeyRegex, "Gemini API Key", MaskKey);
            ScanRegex(result, text, GitHubTokenRegex, "GitHub Token", MaskKey);
            ScanRegex(result, text, AwsKeyRegex, "AWS Access Key", MaskKey);
            ScanRegex(result, text, GenericBearerRegex, "Bearer Token", MaskKey);

            // Quét PII (SĐT, Email, CCCD, Thẻ tín dụng)
            ScanRegex(result, text, PhoneVnRegex, "Số điện thoại VN", MaskPhone);
            ScanRegex(result, text, EmailRegex, "Email", MaskEmail);
            ScanRegex(result, text, CccdRegex, "CCCD / CMND", MaskCccd);

            // Thẻ tín dụng: Kiểm tra thêm thuật toán Luhn
            foreach (Match m in CreditCardRegex.Matches(text))
            {
                if (PassesLuhn(m.Value))
                {
                    result.Matches.Add(new DlpMatch
                    {
                        Type = "Thẻ ngân hàng",
                        RawValue = m.Value,
                        MaskedValue = MaskCard(m.Value),
                        Index = m.Index,
                        Length = m.Length
                    });
                }
            }

            // Shannon Entropy: Bắt mật khẩu/secret ngẫu nhiên nằm trong ngữ cảnh key-value
            ScanEntropySecrets(result, text);

            return result;
        }

        private static void ScanRegex(DlpResult result, string text, Regex regex, string type, Func<string, string> maskFunc)
        {
            foreach (Match m in regex.Matches(text))
            {
                result.Matches.Add(new DlpMatch
                {
                    Type = type,
                    RawValue = m.Value,
                    MaskedValue = maskFunc(m.Value),
                    Index = m.Index,
                    Length = m.Length
                });
            }
        }

        /// <summary>
        /// Tính toán Shannon Entropy: Đo lường độ ngẫu nhiên của chuỗi.
        /// Chuỗi ngẫu nhiên cao (mật khẩu mạnh, secret key) có entropy > 3.8.
        /// </summary>
        public static double CalculateEntropy(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            var freq = new Dictionary<char, int>();
            foreach (char c in value)
            {
                freq[c] = freq.GetValueOrDefault(c) + 1;
            }

            double entropy = 0;
            foreach (var kvp in freq)
            {
                double p = (double)kvp.Value / value.Length;
                entropy -= p * Math.Log2(p);
            }
            return entropy;
        }

        private static void ScanEntropySecrets(DlpResult result, string text)
        {
            // Bắt các cặp key-value dạng: password = "xxx" hoặc secret: 'yyy'
            var assignmentRegex = new Regex(@"(?i)(" + string.Join("|", SecretContextKeywords) + @")\s*[:=]\s*[""']?([A-Za-z0-9_\-!@#$%^&*]{12,64})[""']?");
            foreach (Match match in assignmentRegex.Matches(text))
            {
                var valGroup = match.Groups[2];
                string val = valGroup.Value;

                // Nếu giá trị có Entropy cao và chưa bị regex khác bắt
                if (CalculateEntropy(val) >= 3.8 && !result.Matches.Any(m => m.Index == valGroup.Index))
                {
                    result.Matches.Add(new DlpMatch
                    {
                        Type = "Secret / Mật khẩu (Entropy cao)",
                        RawValue = val,
                        MaskedValue = MaskKey(val),
                        Index = valGroup.Index,
                        Length = valGroup.Length
                    });
                }
            }
        }

        private static bool PassesLuhn(string number)
        {
            string clean = Regex.Replace(number, @"[\s\-]", "");
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
            return sum % 10 == 0;
        }

        // ====================== MASKING FUNCTIONS ======================
        public static string MaskKey(string s) => s.Length > 8 ? s.Substring(0, 4) + "****" + s.Substring(s.Length - 4) : "****";
        public static string MaskPhone(string s) => s.Length >= 7 ? s.Substring(0, 3) + "****" + s.Substring(s.Length - 2) : "****";
        public static string MaskCccd(string s) => s.Length >= 6 ? s.Substring(0, 3) + "******" + s.Substring(s.Length - 3) : "******";
        public static string MaskCard(string s) => s.Length >= 8 ? s.Substring(0, 4) + " **** **** " + s.Substring(s.Length - 4) : "****";
        public static string MaskEmail(string s)
        {
            int at = s.IndexOf('@');
            if (at <= 2) return "***" + s.Substring(at);
            return s.Substring(0, 2) + "***" + s.Substring(at);
        }
    }
}
