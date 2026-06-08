using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HapiCopi
{
    public static class GeminiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        /// <summary>
        /// Gọi Gemini API để định dạng lại văn bản thông minh theo ngữ nghĩa.
        /// </summary>
        public static async Task<string> ReformatWithGeminiAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 1. Lấy API Key từ biến môi trường, file .env hoặc file cục bộ
            string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = LoadApiKeyFromDotEnv();
            }
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
                throw new InvalidOperationException("API Key chưa được cấu hình hoặc đang để mặc định. Vui lòng thiết lập biến môi trường GEMINI_API_KEY hoặc điền vào tệp .env.");
            }

            // 2. Thiết lập yêu cầu API
            string model = "gemini-1.5-flash";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var systemInstruction = "Bạn là một biên tập viên chuyên nghiệp. Hãy định dạng lại đoạn văn bản được cung cấp:\n" +
                                    "1. Tự động ngắt đoạn một cách tự nhiên, hợp lý dựa trên mạch tư duy và logic ngữ nghĩa của nội dung.\n" +
                                    "2. Sửa lại khoảng trắng và dấu câu đúng quy chuẩn tiếng Việt (không có khoảng trắng trước dấu câu, có chính xác một khoảng trắng sau dấu câu, loại trừ số thập phân).\n" +
                                    "3. GIỮ NGUYÊN 100% từ ngữ, câu chữ và nội dung gốc. Tuyệt đối không tóm tắt, không viết lại, không thêm bớt ý kiến cá nhân.\n" +
                                    "4. KHÔNG phản hồi thêm bất kỳ từ ngữ dẫn nhập, giải thích hay kết luận nào. Chỉ trả về duy nhất văn bản sau khi định dạng.";

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
                    temperature = 0.2
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            Program.Log("Đang gửi yêu cầu định dạng tới Gemini API...");
            
            // Thiết lập timeout hợp lý (ví dụ: 10 giây) để tránh treo ứng dụng
            HttpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await HttpClient.PostAsync(url, content);
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
                            return resultText.Trim();
                        }
                    }
                }
            }

            throw new Exception("Không thể phân tích dữ liệu trả về từ Gemini API.");
        }

        private static string? LoadApiKeyFromDotEnv()
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
                            if (trimmed.StartsWith("GEMINI_API_KEY="))
                            {
                                string keyVal = trimmed.Substring("GEMINI_API_KEY=".Length).Trim();
                                if ((keyVal.StartsWith("\"") && keyVal.EndsWith("\"")) ||
                                    (keyVal.StartsWith("'") && keyVal.EndsWith("'")))
                                {
                                    keyVal = keyVal.Substring(1, keyVal.Length - 2);
                                }
                                if (!string.IsNullOrEmpty(keyVal))
                                {
                                    return keyVal;
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
