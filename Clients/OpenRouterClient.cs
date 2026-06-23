using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HapiCopi
{
    public static class OpenRouterClient
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public static async Task<GeminiResult> ReformatWithOpenRouterAsync(string text, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = new GeminiResult { SuggestedText = text, Evaluation = "" };
            if (string.IsNullOrWhiteSpace(text)) return result;

            // 1. Lấy API Key từ cấu hình (ENV > config người dùng > .env)
            string? apiKey = AppConfig.Get("OPENROUTER_API_KEY");

            if (string.IsNullOrEmpty(apiKey) || apiKey.Trim() == "YOUR_OPENROUTER_API_KEY_HERE")
            {
                throw new InvalidOperationException("OpenRouter API Key chưa được cấu hình. Vui lòng mở menu HapiCopi ở khay hệ thống > Cài đặt... để nhập OpenRouter API Key.");
            }

            // 2. Thiết lập yêu cầu API
            string model = AppConfig.Get("OPENROUTER_MODEL") ?? "openai/gpt-oss-120b:free";

            string url = "https://openrouter.ai/api/v1/chat/completions";

            var systemInstruction = GeminiClient.SystemInstruction;

            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemInstruction },
                    new { role = "user", content = text }
                },
                response_format = new { type = "json_object" }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            
            HttpResponseMessage? response = null;
            int maxRetries = 3;
            int delayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                    request.Headers.Add("HTTP-Referer", "https://github.com/ThienHoang56/HapiCopi");
                    request.Headers.Add("X-Title", "HapiCopi");
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    Program.Log($"Đang gửi yêu cầu định dạng tới OpenRouter API (lần {attempt}/{maxRetries}, model={model})...");
                    response = await HttpClient.SendAsync(request, cancellationToken);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                    
                    int statusCode = (int)response.StatusCode;
                    if (statusCode == 502 || statusCode == 503 || statusCode == 429 || (statusCode >= 500 && statusCode < 600))
                    {
                        if (attempt < maxRetries)
                        {
                            Program.Log($"OpenRouter API trả về mã lỗi transient {statusCode}. Thử lại sau {delayMs}ms...");
                            await Task.Delay(delayMs, cancellationToken);
                            delayMs *= 2;
                        }
                        else
                        {
                            Program.Log($"OpenRouter API trả về mã lỗi transient {statusCode} ở lần thử cuối cùng.");
                        }
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
                catch (Exception ex) when (attempt < maxRetries && (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException))
                {
                    Program.Log($"Lỗi kết nối OpenRouter API (lần {attempt}): {ex.Message}. Thử lại sau {delayMs}ms...");
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                }
            }

            if (response == null)
            {
                throw new Exception("Không thể kết nối tới OpenRouter API sau nhiều lần thử.");
            }
            
            response.EnsureSuccessStatusCode();
            string responseJson = await response.Content.ReadAsStringAsync();

            // 3. Phân tích JSON trả về
            using (var doc = JsonDocument.Parse(responseJson))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var messageProp) &&
                        messageProp.TryGetProperty("content", out var contentProp))
                    {
                        string? resultText = contentProp.GetString();
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
                                Program.Log($"[OpenRouter] Lỗi parse JSON con: {parseEx.Message}");
                                result.SuggestedText = resultText.Trim(); // fallback
                            }
                            return result;
                        }
                    }
                }
            }

            throw new Exception("Không thể phân tích dữ liệu trả về từ OpenRouter API.");
        }
    }
}
