using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HapiCopi
{
    public class GeminiResult
    {
        public string Evaluation { get; set; } = "";
        public string SuggestedText { get; set; } = "";
    }

    public static class GeminiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public static readonly string SystemInstruction = 
            "Bạn là một biên tập viên văn bản tiếng Việt chuyên nghiệp. Hãy phân tích văn bản gốc của người dùng và cải thiện định dạng, tính logic của nó:\n" +
            "1. Đưa ra nhận xét ngắn gọn (1-2 câu) chỉ rõ các lỗi chính tả, hoặc các lập luận mâu thuẫn, nghịch lý, thiếu logic trong văn bản gốc.\n" +
            "2. Định dạng, sửa lỗi và cải thiện tính logic:\n" +
            "   - Xác định 1 ý chính trong nguyên đoạn văn mà người dùng thực sự muốn truyền tải, giữ lại ý chính đó và xây dựng luận điểm, luận cứ, lập luận logic xung quanh nó. Chấp nhận việc có thể không cần giữ lại chính xác toàn bộ câu từ gốc của người dùng nhằm đảm bảo tính logic, thông minh và nhất quán cao nhất.\n" +
            "   - Đối với văn bản chứa các mệnh đề mâu thuẫn phi lý, lập luận nghịch lý hoặc nước đôi (ví dụ: 'Internet là kho tàng tri thức vô tận nên làm giới trẻ rỗng tuếch', 'đi du lịch để hiểu biết nên tốt nhất ở nhà', 'vận động nhiều để sống lâu nhưng làm rút ngắn tuổi thọ'), bạn BẮT BUỘC phải viết lại các câu này thành các lập luận mạch lạc, có logic rõ ràng và có chính kiến cụ thể (ví dụ: dùng quan hệ từ nhượng bộ, chỉ ra mặt tích cực và tiêu cực một cách khoa học, hợp lý).\n" +
            "   - Ví dụ xử lý câu mâu thuẫn:\n" +
            "     * Gốc: 'Internet là một kho tàng tri thức vô tận của nhân loại, vì vậy nó khiến tư duy của giới trẻ ngày càng trở nên rỗng tuếch và lười suy nghĩ.'\n" +
            "     * Đề xuất sửa đổi: 'Mặc dù Internet là một kho tàng tri thức vô tận của nhân loại, việc lạm dụng nó quá mức đang khiến tư duy của giới trẻ có xu hướng lười suy nghĩ và trở nên rỗng tuếch.'\n" +
            "     * Gốc: 'Để có một sức khỏe dẻo dai và sống lâu, chúng ta cần phải tập thể dục thể thao đều đặn mỗi ngày, tuy nhiên việc vận động quá nhiều sẽ làm cơ thể nhanh hao mòn và rút ngắn tuổi thọ một cách nghiêm trọng.'\n" +
            "     * Đề xuất sửa đổi: 'Để có một sức khỏe dẻo dai và sống lâu, chúng ta cần tập thể dục thể thao đều đặn mỗi ngày, nhưng cần vận động với cường độ hợp lý để tránh làm cơ thể nhanh hao mòn.'\n" +
            "   - Đối với văn bản không có lỗi logic hay mâu thuẫn lớn, hãy giữ nguyên đại ý và sửa lỗi chính tả/khoảng trắng/chuẩn hóa viết hoa.\n" +
            "   - Tự động thêm dấu tiếng Việt đầy đủ nếu gốc là không dấu (unsigned).\n" +
            "3. Trả về kết quả bắt buộc dưới dạng JSON có cấu trúc sau:\n" +
            "{\n" +
            "  \"evaluation\": \"Nhận xét ngắn gọn chỉ rõ điểm mâu thuẫn logic\",\n" +
            "  \"suggested_text\": \"Văn bản sau khi đã sửa đổi và lập luận logic hoàn chỉnh\"\n" +
            "}";

        /// <summary>
        /// Gọi Gemini API để định dạng lại văn bản thông minh và đưa ra đánh giá ngữ nghĩa.
        /// </summary>
        public static async Task<GeminiResult> ReformatWithGeminiAsync(string text, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = new GeminiResult { SuggestedText = text, Evaluation = "" };
            if (string.IsNullOrWhiteSpace(text)) return result;

            // 1. Lấy API Key từ cấu hình (ENV > config người dùng > .env), fallback file gemini_key.txt
            string? apiKey = AppConfig.Get("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gemini_key.txt");
                if (File.Exists(configPath))
                {
                    apiKey = File.ReadAllText(configPath).Trim();
                }
            }

            if (string.IsNullOrEmpty(apiKey) || apiKey.Trim() == "YOUR_GEMINI_API_KEY_HERE")
            {
                throw new InvalidOperationException("API Key chưa được cấu hình. Vui lòng mở menu HapiCopi ở khay hệ thống > Cài đặt... để nhập Gemini API Key.");
            }

            // 2. Thiết lập yêu cầu API
            string model = AppConfig.Get("GEMINI_MODEL") ?? "gemini-3.5-flash";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var systemInstruction = SystemInstruction;

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"{systemInstruction}\n\nVăn bản gốc:\n{text}" }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    responseMimeType = "application/json"
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            
            HttpResponseMessage? response = null;
            int maxRetries = 3;
            int delayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    Program.Log($"Đang gửi yêu cầu định dạng tới Gemini API (lần {attempt}/{maxRetries}, model={model})...");
                    response = await HttpClient.PostAsync(url, content, cancellationToken);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                    
                    int statusCode = (int)response.StatusCode;
                    if (statusCode == 503 || statusCode == 429 || (statusCode >= 500 && statusCode < 600))
                    {
                        if (attempt < maxRetries)
                        {
                            Program.Log($"Gemini API trả về mã lỗi transient {statusCode}. Thử lại sau {delayMs}ms...");
                            await Task.Delay(delayMs, cancellationToken);
                            delayMs *= 2;
                        }
                        else
                        {
                            Program.Log($"Gemini API trả về mã lỗi transient {statusCode} ở lần thử cuối cùng.");
                        }
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode(); // Ném lỗi ngay đối với các mã lỗi cố định (ví dụ: 400, 404)
                    }
                }
                catch (Exception ex) when (attempt < maxRetries && (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException))
                {
                    Program.Log($"Lỗi kết nối Gemini API (lần {attempt}): {ex.Message}. Thử lại sau {delayMs}ms...");
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                }
            }

            if (response == null)
            {
                throw new Exception("Không thể kết nối tới Gemini API sau nhiều lần thử.");
            }
            
            response.EnsureSuccessStatusCode();
            string responseJson = await response.Content.ReadAsStringAsync();

            // 3. Phân tích JSON trả về
            using (var doc = JsonDocument.Parse(responseJson))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var resContent) &&
                        resContent.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                    {
                        string? resultText = parts[0].GetProperty("text").GetString();
                        if (!string.IsNullOrEmpty(resultText))
                        {
                            try
                            {
                                using (var innerDoc = JsonDocument.Parse(resultText))
                                {
                                    var innerRoot = innerDoc.RootElement;
                                    if (innerRoot.TryGetProperty("evaluation", out var evalProp))
                                    {
                                        result.Evaluation = evalProp.GetString() ?? "";
                                    }
                                    if (innerRoot.TryGetProperty("suggested_text", out var textProp))
                                    {
                                        result.SuggestedText = textProp.GetString() ?? text;
                                    }
                                }
                            }
                            catch (Exception parseEx)
                            {
                                Program.Log($"[Gemini] Lỗi parse JSON con: {parseEx.Message}");
                                result.SuggestedText = resultText.Trim(); // fallback
                            }
                            return result;
                        }
                    }
                }
            }

            throw new Exception("Không thể phân tích dữ liệu trả về từ Gemini API.");
        }

        public static string? LoadValueFromDotEnv(string keyName)
        {
            // Danh sách các đường dẫn có thể chứa file .env
            var pathsToSearch = new System.Collections.Generic.List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                // Lên 3 cấp thư mục (từ bin/Debug/net10.0-windows/ lên thư mục gốc dự án)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."),
                Path.Combine(Environment.CurrentDirectory, "..", "..", "..")
            };

            foreach (var basePath in pathsToSearch)
            {
                try
                {
                    string fullPath = Path.GetFullPath(Path.Combine(basePath, ".env"));
                    if (File.Exists(fullPath))
                    {
                        var lines = File.ReadAllLines(fullPath);
                        foreach (var line in lines)
                        {
                            string trimmed = line.Trim();
                            if (trimmed.StartsWith($"{keyName}="))
                            {
                                string val = trimmed.Substring((keyName + "=").Length).Trim();
                                if ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                                    (val.StartsWith("'") && val.EndsWith("'")))
                                {
                                    val = val.Substring(1, val.Length - 2);
                                }
                                if (!string.IsNullOrEmpty(val))
                                {
                                    return val;
                                }
                            }
                        }
                    }
                }
                catch {}
            }

            return null;
        }
    }
}
