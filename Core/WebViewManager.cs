using System;
using System.Drawing;
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
        private static CoreWebView2Environment? _environment;
        private static bool _isInitialized = false;
        private static readonly TaskCompletionSource<bool> _initTcs = new TaskCompletionSource<bool>();
        private static readonly TaskCompletionSource<CoreWebView2Environment?> _envTcs =
            new TaskCompletionSource<CoreWebView2Environment?>();
        private static System.Threading.SynchronizationContext? _syncContext;

        public static bool IsInitialized => _isInitialized;
        public static Task<bool> InitializationTask => _initTcs.Task;
        public static Task<CoreWebView2Environment?> EnvironmentTask => _envTcs.Task;

        /// <summary>
        /// Môi trường Chromium đã khởi động cùng app. Soạn toán/nhạc phải dùng lại cái này,
        /// không CreateAsync lần nữa — tạo environment mới mỗi lần mở cửa sổ mất vài giây.
        /// </summary>
        public static CoreWebView2Environment? SharedEnvironment => _environment;

        public static uint BrowserProcessId
        {
            get
            {
                try { return _webView?.CoreWebView2?.BrowserProcessId ?? 0; }
                catch { return 0; }
            }
        }

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
                _envTcs.TrySetResult(null);
            }
        }

        private static CoreWebView2EnvironmentOptions CreateEnvironmentOptions()
        {
            return new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-features=Translate,OptimizationHints,MediaRouter,CalculateNativeWinOcclusion,SpareRendererForSitePerProcess " +
                    "--disable-background-networking " +
                    "--disable-component-update " +
                    "--disable-sync " +
                    "--renderer-process-limit=1 " +
                    "--js-flags=\"--max-old-space-size=64\""
            };
        }

        private static async void InitializeWebViewAsync()
        {
            try
            {
                if (_webView == null)
                {
                    _initTcs.TrySetResult(false);
                    _envTcs.TrySetResult(null);
                    return;
                }

                string cacheDir = ResourcePaths.WebViewCacheDir;
                Directory.CreateDirectory(cacheDir);

                _environment = await CoreWebView2Environment.CreateAsync(null, cacheDir, CreateEnvironmentOptions());
                await _webView.EnsureCoreWebView2Async(_environment);
                // Chromium đã sống — cửa sổ soạn được phép gắn ngay, không chờ index.html
                // (trang đó còn kéo MathJax + Mermaid, chậm hơn nhiều soạn toán/nhạc).
                _envTcs.TrySetResult(_environment);

                // Ánh xạ thư mục ảo hapicopi.local để nạp tài nguyên cục bộ bảo mật (tránh CORS/File scheme restrictions)
                string localFolder = ResourcePaths.GetWebViewResourcesFolder();

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
                _webView.Source = new Uri("http://hapicopi.local/index.html?v=4");

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
                _envTcs.TrySetResult(null);
            }
        }

        /// <summary>
        /// Đóng WebView2 khi thoát app. Tiến trình msedgewebview2.exe giữ khoá thư mục cache, còn
        /// sống thì bộ gỡ cài đặt không xoá được %LocalAppData%\HapiCopi.
        /// </summary>
        public static void Shutdown()
        {
            _isInitialized = false;

            try
            {
                _webView?.Dispose();
            }
            catch { }

            try
            {
                _hiddenForm?.Dispose();
            }
            catch { }

            _webView = null;
            _hiddenForm = null;
            _environment = null;
        }

        /// <summary>
        /// Gắn WebView2 cửa sổ soạn vào environment đã ấm rồi điều hướng ngay.
        /// Không chờ trang tải xong — để khung soạn hiện như các bản trước.
        /// </summary>
        public static async Task AttachEditorAsync(
            Microsoft.Web.WebView2.WinForms.WebView2 web,
            string pageFileName,
            EventHandler<CoreWebView2WebMessageReceivedEventArgs> onMessage)
        {
            CoreWebView2Environment? env = await _envTcs.Task;
            if (env == null)
            {
                string cacheDir = ResourcePaths.WebViewCacheDir;
                Directory.CreateDirectory(cacheDir);
                env = await CoreWebView2Environment.CreateAsync(null, cacheDir, CreateEnvironmentOptions());
                _environment = env;
            }

            if (web.CoreWebView2 != null)
            {
                return;
            }

            await web.EnsureCoreWebView2Async(env);

            string localFolder = ResourcePaths.GetWebViewResourcesFolder();
            if (!File.Exists(Path.Combine(localFolder, pageFileName)))
            {
                throw new FileNotFoundException("Thiếu " + pageFileName + " trong " + localFolder);
            }

            web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "hapicopi.local", localFolder, CoreWebView2HostResourceAccessKind.Allow);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.DefaultBackgroundColor = Color.White;
            web.CoreWebView2.WebMessageReceived += onMessage;
            web.Source = new Uri("http://hapicopi.local/" + pageFileName + "?v=3");
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
                                    _ = ExecuteScriptAsync($"delete window.cleanHtmlResults['{id}']");
                                    if (root.TryGetProperty("result", out var resultProp))
                                        return resultProp.GetString() ?? "";
                                    return "";
                                }
                                else if (status == "error")
                                {
                                    _ = ExecuteScriptAsync($"delete window.cleanHtmlResults['{id}']");
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
            
            _ = ExecuteScriptAsync($"delete window.cleanHtmlResults['{id}']");
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
                                    _ = ExecuteScriptAsync($"delete window.mermaidResults[{id}]");
                                    if (root.TryGetProperty("result", out var resultProp))
                                    {
                                        return resultProp.GetString() ?? "";
                                    }
                                    return "";
                                }
                                else if (status == "error")
                                {
                                    _ = ExecuteScriptAsync($"delete window.mermaidResults[{id}]");
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
            
            _ = ExecuteScriptAsync($"delete window.mermaidResults[{id}]");
            return "ERROR: Render timeout";
        }

        public static Task<string> RenderSvgToPngAsync(string svg, int id)
        {
            return RenderSvgToPngAsync(svg, id, null);
        }

        public static async Task<string> RenderSvgToPngAsync(string svg, int id, SvgRasterOptions? options)
        {
            SvgRasterResult result = await RenderSvgDetailedAsync(svg, id, options);
            return result.Success ? result.DataUri : result.Error;
        }

        /// <summary>
        /// Như <see cref="RenderSvgToPngAsync(string, int, SvgRasterOptions?)"/> nhưng trả kèm kích thước
        /// nội tại của SVG, để nơi gọi biết nên hiển thị ảnh to bằng nào.
        /// </summary>
        public static async Task<SvgRasterResult> RenderSvgDetailedAsync(string svg, int id, SvgRasterOptions? options)
        {
            if (!_isInitialized)
            {
                return new SvgRasterResult { Error = "ERROR: WebView2 chưa sẵn sàng" };
            }

            // 1. Khởi chạy tác vụ rasterize SVG -> PNG trong WebView2
            bool started = await PostSvgJobAsync(svg, id, options ?? new SvgRasterOptions());
            if (!started)
            {
                return new SvgRasterResult { Error = "ERROR: Không gửi được SVG sang WebView2" };
            }

            // 2. Vòng lặp chờ kết quả (polling)
            int timeoutMs = options?.TimeoutMs ?? 8000;
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
                                    _ = ExecuteScriptAsync($"delete window.svgResults[{id}]");
                                    return new SvgRasterResult
                                    {
                                        DataUri = root.TryGetProperty("result", out var resultProp)
                                            ? resultProp.GetString() ?? ""
                                            : "",
                                        IntrinsicWidth = ReadIntProperty(root, "width"),
                                        IntrinsicHeight = ReadIntProperty(root, "height")
                                    };
                                }
                                else if (status == "error")
                                {
                                    string error = root.TryGetProperty("result", out var errorProp)
                                        ? errorProp.GetString() ?? "ERROR: Unknown JavaScript error"
                                        : "ERROR: Unknown JavaScript error";
                                    _ = ExecuteScriptAsync($"delete window.svgResults[{id}]");
                                    return new SvgRasterResult { Error = error };
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

            return new SvgRasterResult { Error = "ERROR: Render timeout" };
        }

        private static int ReadIntProperty(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var prop) && prop.TryGetInt32(out int value) ? value : 0;
        }

        /// <summary>
        /// Gửi SVG sang WebView2 bằng PostWebMessage thay vì nội suy vào chuỗi ExecuteScript,
        /// vì file .svg thật có thể nặng vài MB.
        /// </summary>
        private static Task<bool> PostSvgJobAsync(string svg, int id, SvgRasterOptions options)
        {
            var tcs = new TaskCompletionSource<bool>();

            if (_hiddenForm == null || _webView == null || !_isInitialized)
            {
                tcs.SetResult(false);
                return tcs.Task;
            }

            string message = JsonSerializer.Serialize(new
            {
                kind = "svg-render",
                id,
                svg,
                options = new
                {
                    background = options.Background,
                    minWidth = options.MinWidth,
                    maxScale = options.MaxScale,
                    maxPixels = options.MaxPixels
                }
            });

            void Post()
            {
                try
                {
                    _webView!.CoreWebView2.PostWebMessageAsString(message);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Program.Log($"[WebViewManager LỖI] Không gửi được SVG job: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            }

            if (_syncContext != null)
            {
                _syncContext.Post(_ => Post(), null);
            }
            else
            {
                _hiddenForm.BeginInvoke(new Action(Post));
            }

            return tcs.Task;
        }
    }

    /// <summary>
    /// Kết quả rasterize: ảnh PNG (độ phân giải cao) kèm kích thước nội tại của SVG (cỡ hiển thị đúng).
    /// </summary>
    public sealed class SvgRasterResult
    {
        public string DataUri { get; init; } = "";
        public int IntrinsicWidth { get; init; }
        public int IntrinsicHeight { get; init; }
        public string Error { get; init; } = "";

        public bool Success => !string.IsNullOrEmpty(DataUri) && !DataUri.StartsWith("ERROR", StringComparison.Ordinal);
    }

    /// <summary>
    /// Tham số rasterize SVG sang PNG.
    /// </summary>
    public sealed class SvgRasterOptions
    {
        /// <summary>Màu nền dạng CSS, hoặc null để giữ nền trong suốt (logo, icon).</summary>
        public string? Background { get; set; } = "#ffffff";

        /// <summary>Bề rộng tối thiểu của PNG đầu ra, để icon nhỏ không bị vỡ khi phóng to trong Word.</summary>
        public int MinWidth { get; set; } = 0;

        /// <summary>Hệ số phóng tối thiểu áp dụng cho mọi ảnh.</summary>
        public double MaxScale { get; set; } = 2;

        /// <summary>Trần tổng số điểm ảnh, chặn SVG khổ lớn làm cạn bộ nhớ.</summary>
        public long MaxPixels { get; set; } = 24_000_000;

        public int TimeoutMs { get; set; } = 8000;
    }
}
