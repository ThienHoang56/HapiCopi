using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace HapiCopi
{
    public static class WebViewManager
    {
        private static Form? _hiddenForm;
        private static Microsoft.Web.WebView2.WinForms.WebView2? _webView;
        private static bool _isInitialized = false;
        private static readonly TaskCompletionSource<bool> _initTcs = new TaskCompletionSource<bool>();
        private static System.Threading.SynchronizationContext? _syncContext;

        public static bool IsInitialized => _isInitialized;
        public static Task<bool> InitializationTask => _initTcs.Task;

        public static void Initialize()
        {
            // Cần khởi tạo WebView2 trên UI STA Thread của WinForms
            try
            {
                Program.Log("[WebViewManager] Bắt đầu khởi tạo ngầm...");
                
                if (System.Threading.SynchronizationContext.Current == null)
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
                }
                _syncContext = System.Threading.SynchronizationContext.Current;
                
                _hiddenForm = new Form
                {
                    Opacity = 0,
                    ShowInTaskbar = false,
                    Width = 1,
                    Height = 1,
                    WindowState = FormWindowState.Minimized
                };

                _webView = new Microsoft.Web.WebView2.WinForms.WebView2
                {
                    Dock = DockStyle.Fill
                };

                _hiddenForm.Controls.Add(_webView);
                
                // Ép buộc tạo Window Handle ngay lập tức cho cả Form và WebView2
                var formHandle = _hiddenForm.Handle;
                var webViewHandle = _webView.Handle;

                // Chạy tác vụ khởi tạo bất đồng bộ
                InitializeWebViewAsync();
            }
            catch (Exception ex)
            {
                Program.Log($"[WebViewManager LỖI] Khởi tạo thất bại: {ex.Message}");
                _initTcs.TrySetResult(false);
            }
        }

        private static async void InitializeWebViewAsync()
        {
            try
            {
                if (_webView == null) return;

                // Tạo thư mục cache chứa dữ liệu người dùng của WebView2
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "HapiCopiWebViewCache"
                );
                
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                var env = await CoreWebView2Environment.CreateAsync(null, cacheDir);
                await _webView.EnsureCoreWebView2Async(env);

                // Ánh xạ thư mục ảo hapicopi.local để nạp tài nguyên cục bộ bảo mật (tránh CORS/File scheme restrictions)
                string localFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebViewResources");
                
                if (!Directory.Exists(localFolder))
                {
                    // Đề phòng nếu chạy trực tiếp debug mà chưa copy resources
                    localFolder = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.FullName ?? "", "WebViewResources");
                }

                Program.Log($"[WebViewManager] Trỏ virtual host vào thư mục: {localFolder}");
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hapicopi.local", 
                    localFolder, 
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // Thiết lập cấu hình tối giản, tắt thanh trạng thái, menu chuột phải
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                
                // Tiết kiệm bộ nhớ Chromium Target
                _webView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

                // Nạp trang index.html điều khiển
                _webView.Source = new Uri("http://hapicopi.local/index.html");

                // Đợi trang HTML nạp xong hoàn toàn
                _webView.NavigationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        Program.Log("[WebViewManager] Đã nạp thành công trang index.html ngầm.");
                        _isInitialized = true;
                        _initTcs.TrySetResult(true);
                    }
                    else
                    {
                        Program.Log($"[WebViewManager LỖI] Không thể nạp index.html. Lỗi định vị: {e.WebErrorStatus}");
                        _initTcs.TrySetResult(false);
                    }
                };
            }
            catch (Exception ex)
            {
                Program.Log($"[WebViewManager LỖI] Lỗi tiến trình khởi động: {ex.Message}\n{ex.StackTrace}");
                _initTcs.TrySetResult(false);
            }
        }

        // Helper để gọi lệnh script JS từ bất kỳ Thread nào và trả về kết quả
        private static Task<string> ExecuteScriptAsync(string jsScript)
        {
            var tcs = new TaskCompletionSource<string>();

            if (_hiddenForm == null || _webView == null || !_isInitialized)
            {
                tcs.SetResult("");
                return tcs.Task;
            }

            if (_syncContext != null)
            {
                _syncContext.Post(async _ =>
                {
                    try
                    {
                        string resultJson = await _webView.ExecuteScriptAsync(jsScript);
                        tcs.SetResult(ParseJsonString(resultJson));
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[WebViewManager LỖI] Lỗi chạy JS: {ex.Message}");
                        tcs.SetResult("");
                    }
                }, null);
            }
            else
            {
                _hiddenForm.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        string resultJson = await _webView.ExecuteScriptAsync(jsScript);
                        tcs.SetResult(ParseJsonString(resultJson));
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[WebViewManager LỖI] Lỗi chạy JS: {ex.Message}");
                        tcs.SetResult("");
                    }
                }));
            }

            return tcs.Task;
        }

        private static string ParseJsonString(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null") return "";
            try
            {
                // Unescape chuỗi trả về từ JS sử dụng JsonSerializer
                return JsonSerializer.Deserialize<string>(json) ?? "";
            }
            catch
            {
                if (json.StartsWith("\"") && json.EndsWith("\"") && json.Length >= 2)
                {
                    return json.Substring(1, json.Length - 2)
                               .Replace("\\\"", "\"")
                               .Replace("\\\\", "\\")
                               .Replace("\\n", "\n")
                               .Replace("\\r", "\r")
                               .Replace("\\t", "\t");
                }
                return json;
            }
        }

        private static string EscapeJsString(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\")
                       .Replace("'", "\\'")
                       .Replace("\"", "\\\"")
                       .Replace("\r", "\\r")
                       .Replace("\n", "\\n")
                       .Replace("\t", "\\t");
        }

        // --- CÁC HÀM CUNG CẤP CHO LOGIC ĐỊNH DẠNG ---

        public static async Task<string> ConvertLatexToMathMLAsync(string latex)
        {
            if (!_isInitialized) return "";
            string escaped = EscapeJsString(latex);
            return await ExecuteScriptAsync($"convertLatexToMathML('{escaped}')");
        }

        public static async Task<string> ConvertMarkdownToHtmlAsync(string markdown, bool isWordTarget)
        {
            if (!_isInitialized) return "";
            string escaped = EscapeJsString(markdown);
            string wordTargetParam = isWordTarget ? "true" : "false";
            return await ExecuteScriptAsync($"convertMarkdownToHtml('{escaped}', {wordTargetParam})");
        }

        public static async Task<string> CleanHtmlAsync(string html, string fontName, double fontSize, double lineSpacing, int parentHeadingLevel, bool isWordTarget, string mermaidCacheJson)
        {
            if (!_isInitialized) return "";
            string escapedHtml = EscapeJsString(html);
            string escapedCache = EscapeJsString(mermaidCacheJson);
            string wordTargetParam = isWordTarget ? "true" : "false";
            
            string id = Guid.NewGuid().ToString("N");
            await ExecuteScriptAsync($"startCleanHtml('{id}', '{escapedHtml}', '{fontName}', {fontSize}, {lineSpacing}, {parentHeadingLevel}, {wordTargetParam}, '{escapedCache}')");

            int timeoutMs = 15000;
            int elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                await Task.Delay(50);
                elapsedMs += 50;
                
                string statusJson = await ExecuteScriptAsync($"JSON.stringify(window.cleanHtmlResults['{id}'])");
                if (!string.IsNullOrEmpty(statusJson) && statusJson != "null" && statusJson != "{}")
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(statusJson))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("status", out var statusProp))
                            {
                                string status = statusProp.GetString() ?? "";
                                if (status == "success")
                                {
                                    if (root.TryGetProperty("result", out var resultProp))
                                        return resultProp.GetString() ?? "";
                                    return "";
                                }
                                else if (status == "error")
                                {
                                    Program.Log($"[WebViewManager LỖI] Lỗi JS CleanHtmlAsync");
                                    return html;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[WebViewManager LỖI] Lỗi phân tích JSON kết quả CleanHtml: {ex.Message}");
                    }
                }
            }
            
            Program.Log($"[WebViewManager LỖI] Quá thời gian CleanHtmlAsync");
            return html;
        }

        public static async Task<string> ProcessLatexInPlainTextAsync(string text)
        {
            if (!_isInitialized) return text;
            string escaped = EscapeJsString(text);
            return await ExecuteScriptAsync($"processLatexInPlainText('{escaped}')");
        }

        public static async Task<string> RenderMermaidToPngAsync(string code, int id)
        {
            if (!_isInitialized) return "";
            string escaped = EscapeJsString(code);
            
            // 1. Khởi chạy tác vụ render trong WebView2
            await ExecuteScriptAsync($"startRenderMermaid('{escaped}', {id})");
            
            // 2. Vòng lặp chờ kết quả (polling)
            int timeoutMs = 6000;
            int elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                await Task.Delay(50);
                elapsedMs += 50;
                
                string statusJson = await ExecuteScriptAsync($"JSON.stringify(window.mermaidResults[{id}])");
                if (!string.IsNullOrEmpty(statusJson) && statusJson != "null" && statusJson != "{}")
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(statusJson))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("status", out var statusProp))
                            {
                                string status = statusProp.GetString() ?? "";
                                if (status == "success")
                                {
                                    if (root.TryGetProperty("result", out var resultProp))
                                    {
                                        return resultProp.GetString() ?? "";
                                    }
                                    return "";
                                }
                                else if (status == "error")
                                {
                                    if (root.TryGetProperty("result", out var resultProp))
                                    {
                                        return resultProp.GetString() ?? "";
                                    }
                                    return "ERROR: Unknown JavaScript error";
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[WebViewManager LỖI] Lỗi phân tích JSON kết quả Mermaid: {ex.Message}");
                    }
                }
            }
            
            return "ERROR: Render timeout";
        }

        public static async Task<string> RenderSvgToPngAsync(string svg, int id)
        {
            if (!_isInitialized) return "";
            string escaped = EscapeJsString(svg);

            // 1. Khởi chạy tác vụ rasterize SVG -> PNG trong WebView2
            await ExecuteScriptAsync($"startRenderSvg('{escaped}', {id})");

            // 2. Vòng lặp chờ kết quả (polling)
            int timeoutMs = 8000;
            int elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                await Task.Delay(50);
                elapsedMs += 50;

                string statusJson = await ExecuteScriptAsync($"JSON.stringify(window.svgResults[{id}])");
                if (!string.IsNullOrEmpty(statusJson) && statusJson != "null" && statusJson != "{}")
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(statusJson))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("status", out var statusProp))
                            {
                                string status = statusProp.GetString() ?? "";
                                if (status == "success")
                                {
                                    if (root.TryGetProperty("result", out var resultProp))
                                        return resultProp.GetString() ?? "";
                                    return "";
                                }
                                else if (status == "error")
                                {
                                    if (root.TryGetProperty("result", out var resultProp))
                                        return resultProp.GetString() ?? "";
                                    return "ERROR: Unknown JavaScript error";
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"[WebViewManager LỖI] Lỗi phân tích JSON kết quả SVG: {ex.Message}");
                    }
                }
            }

            return "ERROR: Render timeout";
        }
    }
}
