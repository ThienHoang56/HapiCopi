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

            var sorted = Matches.OrderBy(m => m.StartIndex).ToList();
            var sb = new System.Text.StringBuilder(originalText.Length);
            int cursor = 0;

            foreach (var match in sorted)
            {
                if (match.StartIndex < cursor) continue; // Bỏ qua trùng lặp/overlap
                if (match.StartIndex > cursor)
                {
                    sb.Append(originalText, cursor, match.StartIndex - cursor);
                }
                sb.Append(match.MaskedValue);
                cursor = match.StartIndex + match.Length;
            }

            if (cursor < originalText.Length)
            {
                sb.Append(originalText, cursor, originalText.Length - cursor);
            }

            return sb.ToString();
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
        // Cấu hình từng loại thông tin nhạy cảm được bảo vệ (người dùng bật/tắt trong Cài đặt)
        public static bool CheckKeysEnabled => (AppConfig.Get("PII_CHECK_KEYS") ?? "true").ToLower() != "false";
        public static bool CheckCardsEnabled => (AppConfig.Get("PII_CHECK_CARDS") ?? "true").ToLower() != "false";
        public static bool CheckCccdEnabled => (AppConfig.Get("PII_CHECK_CCCD") ?? "true").ToLower() != "false";
        public static bool CheckPhoneEnabled => (AppConfig.Get("PII_CHECK_PHONE") ?? "true").ToLower() != "false";
        public static bool CheckEmailEnabled => (AppConfig.Get("PII_CHECK_EMAIL") ?? "true").ToLower() != "false";

        // Các ký tự đặc trưng của code được bỏ qua không quét Entropy, tải động từ AppConfig
        private static readonly char[] CodeBypassChars = 
            (AppConfig.Get("PII_BYPASS_CHARS") ?? "()[]{}=,;<>/\\+-*&|^%!~\"'").ToCharArray();

        // API Keys — Định dạng cố định, không bao giờ xuất hiện trong văn bản tự nhiên
        private static readonly Regex GeminiKeyRegex = new Regex(
            @"AIzaSy[A-Za-z0-9_-]{33}",
            RegexOptions.Compiled);

        // OpenAI: sk-... (mọi độ dài từ 16+, có thể có proj- prefix)
        private static readonly Regex OpenAiKeyRegex = new Regex(
            @"sk-(proj-)?[a-zA-Z0-9_-]{16,}",
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

        // Generic Secret Prefix: bắt các key có prefix phổ biến (sk_, pk_, rk_, tvly-, pcsk_, etc.)
        // Format: prefix + phần ngẫu nhiên dài từ 12 ký tự trở lên
        private static readonly Regex GenericSecretPrefixRegex = new Regex(
            @"(?<![a-zA-Z0-9_-])(?:" +
                @"sk[_-]|pk[_-]|rk[_-]|" +                    // Generic secret/public/restricted key
                @"sk_(?:live|test)_|pk_(?:live|test)_|" +      // Stripe
                @"tvly[_-]|" +                                  // Tavily
                @"pcsk[_-]|" +                                  // Prisma Cloud
                @"xoxb[_-]|xoxp[_-]|xoxa[_-]|" +             // Slack
                @"eyJ[A-Za-z0-9]|" +                           // JWT tokens (base64 of {"...)
                @"glpat-|" +                                    // GitLab
                @"nk_|np_|" +                                   // Generic
                @"whsec_|" +                                    // Webhook secret
                @"SG\.|" +                                      // SendGrid
                @"xkeysib-|" +                                  // Sendinblue/Brevo
                @"AC[a-f0-9]{32}|" +                           // Twilio
                @"sk-ant-|" +                                   // Anthropic
                @"hf_[A-Za-z]|" +                              // HuggingFace
                @"ya29\." +                                     // Google OAuth
            @")[A-Za-z0-9_\-\.]{12,}",
            RegexOptions.Compiled);

        // Supabase, Firebase, Vercel tokens
        private static readonly Regex CloudTokenRegex = new Regex(
            @"(?i)(?:sbp_|supabase_|vercel_|firebase[_-])[A-Za-z0-9_\-\.]{12,}",
            RegexOptions.Compiled);

        // Private Key block (PEM)
        private static readonly Regex PrivateKeyBlockRegex = new Regex(
            @"-----BEGIN\s+(?:RSA\s+)?PRIVATE\s+KEY-----",
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
            return sum % 10 == 0;
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

            // --- Engine 1 & 2: Regex & Entropy theo danh mục người dùng cấu hình ---

            // 1. Mật khẩu, API Keys & Tokens
            if (CheckKeysEnabled)
            {
                ScanWithRegex(result, text, GeminiKeyRegex, "API Key (Gemini)", MaskApiKey);
                ScanWithRegex(result, text, OpenAiKeyRegex, "API Key (OpenAI)", MaskApiKey);
                ScanWithRegex(result, text, GitHubTokenRegex, "API Key (GitHub)", MaskApiKey);
                ScanWithRegex(result, text, AwsKeyRegex, "API Key (AWS)", MaskApiKey);
                ScanWithRegex(result, text, GenericBearerTokenRegex, "Bearer Token", MaskApiKey);

                ScanWithRegex(result, text, GenericSecretPrefixRegex, "API Key / Secret", MaskApiKey);
                ScanWithRegex(result, text, CloudTokenRegex, "Cloud Token", MaskApiKey);
                ScanWithRegex(result, text, PrivateKeyBlockRegex, "Private Key", _ => "-----BEGIN PRIVATE KEY-----[MASKED]");

                ScanWithRegex(result, text, ConnectionStringRegex, "Connection String", MaskConnectionString);

                // Quét Shannon Entropy cho mật khẩu / token ngẫu nhiên
                ScanHighEntropyTokens(result, text);
            }

            // 2. Số điện thoại cá nhân (Việt Nam)
            if (CheckPhoneEnabled)
            {
                ScanWithRegex(result, text, PhoneVnRegex, "Số điện thoại", MaskPhone);
            }

            // 3. Địa chỉ Email
            if (CheckEmailEnabled)
            {
                ScanWithRegex(result, text, EmailRegex, "Email", MaskEmail);
            }

            // 4. Thẻ ngân hàng & Thẻ tín dụng (16 số, kiểm tra thuật toán Luhn)
            if (CheckCardsEnabled)
            {
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
            }

            // 5. Căn cước công dân (12 số) & CMND (9 số)
            if (CheckCccdEnabled)
            {
                ScanWithRegex(result, text, CccdRegex, "CCCD", MaskCccd);
                ScanWithRegex(result, text, CmndRegex, "CMND", MaskCccd);
            }

            // --- Custom Keywords từ Settings ---
            ScanCustomKeywords(result, text);

            // --- BƯỚC QUAN TRỌNG: Lọc bỏ các mục nhạy cảm nằm trong danh sách ngoại lệ (Exclusions) ---
            try
            {
                result.Matches = result.Matches
                    .Where(m => !ExclusionManager.IsExcluded(m.Value))
                    .ToList();
            }
            catch (Exception ex)
            {
                Program.Log($"[PiiScanner LỖI] Lỗi lọc ngoại lệ: {ex.Message}");
            }

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
        /// Quét các token có entropy cao — kiểm tra ngữ cảnh và cấu trúc chữ-số cực kỳ chặt chẽ
        /// để tránh tuyệt đối các báo động giả (False Positives) trên mã nguồn.
        /// </summary>
        private static void ScanHighEntropyTokens(PiiScanResult result, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // Tách các token liền mạch dài từ 12 ký tự trở lên.
            var tokenRegex = new Regex(@"['""]?(\S{12,})['""]?");
            string textLower = text.ToLowerInvariant();

            foreach (Match match in tokenRegex.Matches(text))
            {
                string token = match.Groups[1].Value;
                int tokenIndex = match.Groups[1].Index;
                int tokenLength = match.Groups[1].Length;

                // Bỏ qua nếu đã bị Engine 1 (Regex) phát hiện
                bool alreadyCovered = result.Matches.Any(m =>
                    tokenIndex >= m.StartIndex && tokenIndex < m.StartIndex + m.Length);
                if (alreadyCovered) continue;

                // Bỏ qua URL hoặc đường dẫn tệp tin
                if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    token.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                    token.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                    token.Contains("\\") || token.Contains("/")) continue;

                // Loại trừ các trường hợp không phải là key/secret ngẫu nhiên:
                // 1. Chỉ toàn chữ thường, chữ hoa, hoặc chỉ toàn chữ số
                bool isPureLowercase = token.All(c => char.IsLower(c) || c == '_' || c == '-');
                bool isPureUppercase = token.All(c => char.IsUpper(c) || c == '_' || c == '-');
                bool isPureDigits = token.All(char.IsDigit);

                if (isPureLowercase || isPureUppercase || isPureDigits) continue;

                // 2. PHÒNG NGỪA FALSE POSITIVE CỰC MẠNH:
                // Các khóa mật mã, API keys, hoặc mật khẩu ngẫu nhiên hầu hết đều chứa hỗn hợp chữ và số.
                // Nếu một chuỗi dài chỉ gồm chữ cái (không có chữ số nào, ví dụ: "ReconstructWithAiAsync", "ClipboardHistoryForm"),
                // thì đó chắc chắn là tên phương thức/lớp/biến dạng PascalCase/camelCase, không phải secret.
                bool hasDigits = token.Any(char.IsDigit);
                if (!hasDigits) continue;

                // 3. Nếu chứa bất kỳ ký tự đặc trưng nào của code (indexOfAny CodeBypassChars)
                if (token.IndexOfAny(CodeBypassChars) >= 0) continue;

                // 4. Nếu chứa dấu chấm (loại trừ thuộc tính đối tượng)
                if (token.Contains("."))
                {
                    bool isJwt = token.Length > 80 && token.StartsWith("eyJ");
                    if (!isJwt) continue;
                }

                // 5. Kiểm tra bối cảnh nhạy cảm (Context-Aware Entropy):
                // Nếu văn bản dài (ví dụ file code), chỉ quét entropy nếu token nằm gần từ khóa nhạy cảm (như key, password, token...).
                // Nếu văn bản ngắn (dưới 120 ký tự), có thể người dùng chỉ copy độc lập mỗi chuỗi key đó, ta sẽ quét trực tiếp.
                if (text.Length >= 120)
                {
                    bool hasContext = false;
                    // Kiểm tra trong phạm vi 60 ký tự trước và sau token xem có chứa từ khóa nhạy cảm không
                    int searchStart = Math.Max(0, tokenIndex - 60);
                    int searchEnd = Math.Min(text.Length, tokenIndex + tokenLength + 60);
                    string contextArea = textLower.Substring(searchStart, searchEnd - searchStart);

                    foreach (var kw in EntropyContextKeywords)
                    {
                        if (contextArea.Contains(kw))
                        {
                            hasContext = true;
                            break;
                        }
                    }

                    if (!hasContext) continue; // Bỏ qua nếu không có bối cảnh nhạy cảm
                }

                double entropy = CalculateEntropy(token);
                double requiredEntropy = token.Length >= 20 ? 3.2 : 3.5;

                if (entropy >= requiredEntropy)
                {
                    result.Matches.Add(new PiiMatch
                    {
                        Type = "Secret (Entropy)",
                        Value = token,
                        MaskedValue = MaskApiKey(token),
                        StartIndex = tokenIndex,
                        Length = tokenLength
                    });
                }
            }
        }

        /// <summary>
        /// Quét từ khóa tùy chỉnh từ Settings (pipe-separated).
        /// </summary>
        private static void ScanCustomKeywords(PiiScanResult result, string text)
        {
            string keywords = AppConfig.Get("PII_CUSTOM_KEYWORDS") ?? "";
            if (string.IsNullOrWhiteSpace(keywords)) return;

            foreach (string keyword in keywords.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                string kw = keyword.Trim();
                if (string.IsNullOrEmpty(kw) || kw.Length < 3) continue;

                int idx = text.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    bool alreadyCovered = result.Matches.Any(m =>
                        idx >= m.StartIndex && idx < m.StartIndex + m.Length);
                    if (!alreadyCovered)
                    {
                        result.Matches.Add(new PiiMatch
                        {
                            Type = "Từ khóa nhạy cảm",
                            Value = text.Substring(idx, kw.Length),
                            MaskedValue = new string('*', kw.Length),
                            StartIndex = idx,
                            Length = kw.Length
                        });
                    }
                    idx = text.IndexOf(kw, idx + kw.Length, StringComparison.OrdinalIgnoreCase);
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
