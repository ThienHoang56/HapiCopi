using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HapiCopi
{
    public class HapiCopiApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ClipboardListener _listener;
        private readonly KeyboardHook _keyboardHook;
        private readonly ToolStripMenuItem _pauseMenuItem;
        private bool _isPaused = false;
        private bool _hasShownWelcome = false;
        private bool _isSimulatingClipboard = false;
        private readonly Dictionary<string, string> _mermaidCache = new Dictionary<string, string>();
        private readonly System.Threading.SynchronizationContext _syncContext;

        public static bool IsSimulatingPaste { get; set; } = false;

        // Cache lưu trữ nội dung copy thô để xử lý dán động
        private string _lastRawText = "";
        private string _lastRawHtml = "";
        private bool _lastWasProcessed = false;

        // Lịch sử Clipboard (in-memory, tối đa 30 mục)
        private readonly List<ClipboardHistoryItem> _clipboardHistory = new List<ClipboardHistoryItem>();
        private const int MaxHistoryItems = 30;

        private PiiToastForm? _currentToast;
        private IntPtr _lastPastedWindowHandle = IntPtr.Zero;
        private bool _hasPastedSinceCopy = false;
        private DateTime _lastPasteTime = DateTime.MinValue;
        private bool _isLastPasteTerminal = false;
        private long _currentClipboardSeq = 0; // Mã tuần tự clipboard cho AI ngầm bất đồng bộ

        public HapiCopiApplicationContext()
        {
            _syncContext = System.Threading.SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            // 0. Khởi tạo trình duyệt WebView2 ngầm
            WebViewManager.Initialize();

            // 1. Thiết lập Menu chuột phải cho System Tray Icon
            var contextMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("HapiCopi v2.0 - Clipboard DLP & Auto-Format") { Enabled = false };
            titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);

            _pauseMenuItem = new ToolStripMenuItem("Tạm dừng", null, OnPauseClicked);
            var historyMenuItem = new ToolStripMenuItem("Lịch sử Clipboard...", null, OnHistoryClicked);
            var mathEditorMenuItem = new ToolStripMenuItem("Soạn công thức toán...", null, OnMathEditorClicked);
            var abcEditorMenuItem = new ToolStripMenuItem("Soạn bản nhạc (ABC)...", null, OnAbcEditorClicked);
            var settingsMenuItem = new ToolStripMenuItem("Cài đặt...", null, OnSettingsClicked);
            var exitMenuItem = new ToolStripMenuItem("Thoát", null, OnExitClicked);

            contextMenu.Items.Add(titleItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(mathEditorMenuItem);
            contextMenu.Items.Add(abcEditorMenuItem);
            contextMenu.Items.Add(historyMenuItem);
            contextMenu.Items.Add(settingsMenuItem);
            contextMenu.Items.Add(_pauseMenuItem);
            contextMenu.Items.Add(exitMenuItem);

            // 2. Tạo System Tray Icon
            _notifyIcon = new NotifyIcon()
            {
                Icon = CreateAppIcon(),
                ContextMenuStrip = contextMenu,
                Text = "HapiCopi - Clipboard DLP & Auto-Format",
                Visible = true
            };

            // Hiển thị thông báo chào mừng lần đầu khởi động
            _notifyIcon.ShowBalloonTip(4000, "HapiCopi đã kích hoạt!",
                "Ứng dụng đang bảo vệ clipboard của bạn.\nTự động định dạng Markdown + Cảnh báo lộ lọt thông tin.",
                ToolTipIcon.Info);

            // 3. Đăng ký Listener bắt sự kiện Clipboard
            _listener = new ClipboardListener();
            _listener.ClipboardChanged += OnClipboardChanged;

            // 4. Đăng ký Hook bàn phím bắt sự kiện dán chủ động
            _keyboardHook = new KeyboardHook();
            _keyboardHook.PastePressed += OnPastePressed;

            // Nạp lịch sử từ đĩa vật lý khi khởi động
            try
            {
                var loaded = HistoryManager.LoadHistory();
                _clipboardHistory.AddRange(loaded);
                Program.Log($"[Startup] Đã nạp {_clipboardHistory.Count} mục lịch sử clipboard từ ổ đĩa.");
            }
            catch (Exception ex)
            {
                Program.Log($"[Startup LỖI] Lỗi nạp lịch sử clipboard: {ex.Message}");
            }

            // 5. Thực hiện xử lý clipboard đang có sẵn ngay khi khởi động
            ProcessClipboard(isStartup: true);
        }

        // ============================
        // MATH EDITOR
        // ============================

        private MathEditorForm? _mathEditorForm;

        private void OnMathEditorClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_mathEditorForm != null && !_mathEditorForm.IsDisposed)
                {
                    _mathEditorForm.WindowState = FormWindowState.Normal;
                    _mathEditorForm.BringToFront();
                    _mathEditorForm.Activate();
                    return;
                }

                _mathEditorForm = new MathEditorForm((text, html) => SetClipboardOnStaThread(text, html));
                _mathEditorForm.Show();
            }
            catch (Exception ex)
            {
                Program.Log($"[MathEditor LỖI] Không mở được cửa sổ: {ex.Message}");
            }
        }

        // ============================
        // ABC MUSIC EDITOR
        // ============================

        private AbcEditorForm? _abcEditorForm;

        private void OnAbcEditorClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_abcEditorForm != null && !_abcEditorForm.IsDisposed)
                {
                    _abcEditorForm.WindowState = FormWindowState.Normal;
                    _abcEditorForm.BringToFront();
                    _abcEditorForm.Activate();
                    return;
                }

                _abcEditorForm = new AbcEditorForm((text, html) => SetClipboardOnStaThread(text, html));
                _abcEditorForm.Show();
            }
            catch (Exception ex)
            {
                Program.Log($"[AbcEditor LỖI] Không mở được cửa sổ: {ex.Message}");
            }
        }

        // ============================
        // SETTINGS
        // ============================

        private SettingsForm? _settingsForm;

        private void OnSettingsClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.WindowState = FormWindowState.Normal;
                    _settingsForm.BringToFront();
                    _settingsForm.Activate();
                    return;
                }

                _settingsForm = new SettingsForm(_clipboardHistory);
                _settingsForm.FormClosed += (s, args) => _settingsForm = null;
                _settingsForm.Show();
                _settingsForm.BringToFront();
                _settingsForm.Activate();
            }
            catch (Exception ex)
            {
                Program.Log($"[UI] Lỗi mở cửa sổ Cài đặt: {ex.Message}");
            }
        }

        // ============================
        // CLIPBOARD HISTORY
        // ============================

        private ClipboardHistoryForm? _historyForm;

        private void OnHistoryClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_historyForm != null && !_historyForm.IsDisposed)
                {
                    _historyForm.RefreshHistoryList();
                    _historyForm.WindowState = FormWindowState.Normal;
                    _historyForm.BringToFront();
                    _historyForm.Activate();
                    return;
                }

                _historyForm = new ClipboardHistoryForm(_clipboardHistory);
                _historyForm.FormClosed += (s, args) => _historyForm = null;
                _historyForm.Show();
                _historyForm.BringToFront();
                _historyForm.Activate();
            }
            catch (Exception ex)
            {
                Program.Log($"[UI] Lỗi mở Lịch sử Clipboard: {ex.Message}");
            }
        }

        private void AddToHistory(ClipboardHistoryItem item)
        {
            // Kiểm tra cấu hình có bật tính năng ghi nhớ lịch sử không
            bool historyEnabled = (AppConfig.Get("HISTORY_ENABLED") ?? "true").ToLower() != "false";
            if (!historyEnabled)
            {
                _clipboardHistory.Clear();
                HistoryManager.DeletePhysicalFiles();
                return;
            }

            _clipboardHistory.Add(item);
            while (_clipboardHistory.Count > MaxHistoryItems)
            {
                _clipboardHistory.RemoveAt(0);
            }

            // Lưu lịch sử xuống đĩa thích ứng theo cấu hình
            HistoryManager.SaveHistory(_clipboardHistory);

            // Cập nhật form lịch sử nếu đang mở
            if (_historyForm != null && !_historyForm.IsDisposed)
            {
                _syncContext.Post(_ =>
                {
                    try { _historyForm?.RefreshHistoryList(); } catch { }
                }, null);
            }
        }

        // ============================
        // PAUSE / RESUME
        // ============================

        private void OnPauseClicked(object? sender, EventArgs e)
        {
            _isPaused = !_isPaused;
            _pauseMenuItem.Checked = _isPaused;

            if (_isPaused)
            {
                _notifyIcon.Text = "HapiCopi (Đang tạm dừng)";
                _notifyIcon.ShowBalloonTip(1500, "HapiCopi", "Đã tạm dừng bảo vệ clipboard.", ToolTipIcon.Warning);
            }
            else
            {
                _notifyIcon.Text = "HapiCopi - Clipboard DLP & Auto-Format";
                _notifyIcon.ShowBalloonTip(1500, "HapiCopi", "Đã hoạt động trở lại.", ToolTipIcon.Info);
            }
        }

        // ============================
        // CLIPBOARD EVENT HANDLER
        // ============================

        private void OnClipboardChanged(object? sender, EventArgs e)
        {
            if (_isSimulatingClipboard)
            {
                Program.Log("Bỏ qua sự kiện thay đổi Clipboard do đang giả lập định dạng.");
                return;
            }
            Program.Log("Sự kiện thay đổi Clipboard kích hoạt.");
            ProcessClipboard(isStartup: false);
        }

        private void SetClipboardOnStaThread(string text, string html)
        {
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    _isSimulatingClipboard = true;
                    ClipboardHelper.SetOfficeClipboard(text, html);
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi ghi clipboard từ STA Thread: {ex.Message}");
                }
                finally
                {
                    _isSimulatingClipboard = false;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        // ============================
        // MERMAID PRE-CACHE (Offline)
        // ============================

        private async System.Threading.Tasks.Task RenderAndCacheDiagramAsync(string innerCode, string diagramType)
        {
            if (!_mermaidCache.ContainsKey(innerCode))
            {
                string dataUri = "";
                if (diagramType == "mermaid")
                {
                    if (WebViewManager.IsInitialized)
                    {
                        Program.Log($"[Diagram] WebView2 đã sẵn sàng. Tiến hành render OFFLINE Mermaid...");
                        dataUri = await WebViewManager.RenderMermaidToPngAsync(innerCode, _mermaidCache.Count);
                    }
                    else
                    {
                        Program.Log($"[Diagram] WebView2 chưa sẵn sàng. Tiến hành render ONLINE Mermaid qua API...");
                        dataUri = await MermaidRenderer.GetMermaidDataUriAsync(innerCode);
                    }
                }
                else if (TikzRenderer.IsTikz(diagramType))
                {
                    // TikZ: biên dịch bằng LaTeX cài trên máy -> SVG -> rasterize sang PNG bằng WebView2.
                    if (WebViewManager.IsInitialized)
                    {
                        Program.Log($"[Diagram] Biên dịch TikZ bằng LaTeX cục bộ ({TikzRenderer.EngineInfo})...");
                        string svg = await TikzRenderer.RenderTikzToSvgAsync(innerCode);
                        if (!string.IsNullOrEmpty(svg))
                        {
                            string png = await WebViewManager.RenderSvgToPngAsync(svg, _mermaidCache.Count);
                            if (!string.IsNullOrEmpty(png) && !png.StartsWith("ERROR"))
                            {
                                dataUri = png;
                            }
                            else
                            {
                                Program.Log($"[Diagram] Rasterize TikZ SVG->PNG thất bại ({png}).");
                            }
                        }
                        else
                        {
                            Program.Log("[Diagram] TikZ không render được (thiếu MiKTeX/TeX Live hoặc lỗi biên dịch).");
                        }
                    }
                    else
                    {
                        Program.Log("[Diagram] WebView2 chưa sẵn sàng, không rasterize được TikZ.");
                    }
                }
                else
                {
                    // Kroki trả về SVG mà Word không nhúng được trong HTML clipboard.
                    // Ưu tiên rasterize SVG -> PNG offline bằng WebView2, fallback tải PNG trực tiếp từ Kroki.
                    if (WebViewManager.IsInitialized)
                    {
                        Program.Log($"[Diagram] Lấy SVG {diagramType} từ Kroki rồi rasterize sang PNG bằng WebView2...");
                        string svg = await KrokiRenderer.GetKrokiSvgAsync(innerCode, diagramType);
                        if (!string.IsNullOrEmpty(svg))
                        {
                            string png = await WebViewManager.RenderSvgToPngAsync(svg, _mermaidCache.Count);
                            if (!string.IsNullOrEmpty(png) && !png.StartsWith("ERROR"))
                            {
                                dataUri = png;
                            }
                            else
                            {
                                Program.Log($"[Diagram] Rasterize SVG->PNG thất bại ({png}). Thử tải PNG trực tiếp từ Kroki...");
                                dataUri = await KrokiRenderer.GetKrokiDataUriAsync(innerCode, diagramType, "png");
                            }
                        }
                    }
                    else
                    {
                        Program.Log($"[Diagram] WebView2 chưa sẵn sàng. Tải PNG {diagramType} trực tiếp từ Kroki API...");
                        dataUri = await KrokiRenderer.GetKrokiDataUriAsync(innerCode, diagramType, "png");
                    }
                }

                if (!string.IsNullOrEmpty(dataUri) && !dataUri.StartsWith("ERROR"))
                {
                    _mermaidCache[innerCode] = dataUri;
                    Program.Log($"[Mermaid] Kết xuất sơ đồ thành công. Kích thước base64: {dataUri.Length} ký tự.");
                }
                else
                {
                    Program.Log($"[Mermaid] Kết xuất sơ đồ THẤT BẠI. Chi tiết: {dataUri}");
                }
            }
        }

        private async System.Threading.Tasks.Task PreCacheDiagramsAsync(string text, string html = null)
        {
            try
            {
                var diagramRegex = new Regex(@"```([a-zA-Z0-9]+)\s*[\r\n]+([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
                var matches = diagramRegex.Matches(text);

                bool hasAnyMatch = false;
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        string lang = match.Groups[1].Value.Trim().ToLowerInvariant();
                        if (lang == "mermaid" || KrokiRenderer.IsSupportedDiagram(lang) || TikzRenderer.IsTikz(lang))
                        {
                            string innerCode = match.Groups[2].Value.Trim();
                            await RenderAndCacheDiagramAsync(innerCode, lang);
                            hasAnyMatch = true;
                        }
                    }
                }
                
                // Extract from HTML if provided (e.g. copied from ChatGPT/browser)
                if (!string.IsNullOrEmpty(html))
                {
                    var htmlRegex = new Regex(@"<code[^>]*class=""[^""]*language-([a-zA-Z0-9]+)[^""]*""[^>]*>([\s\S]*?)</code>|<div[^>]*class=""[^""]*mermaid[^""]*""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
                    var htmlMatches = htmlRegex.Matches(html);
                    foreach (Match match in htmlMatches)
                    {
                        string lang = "";
                        string innerCode = "";
                        if (match.Groups[1].Success)
                        {
                            lang = match.Groups[1].Value.Trim().ToLowerInvariant();
                            innerCode = match.Groups[2].Value;
                        }
                        else if (match.Groups[3].Success)
                        {
                            lang = "mermaid";
                            innerCode = match.Groups[3].Value;
                        }

                        // Simulate Javascript's textContent by stripping any inner HTML tags (e.g. syntax highlighting spans)
                        innerCode = Regex.Replace(innerCode, "<.*?>", string.Empty).Trim();
                        innerCode = System.Net.WebUtility.HtmlDecode(innerCode);

                        if (lang == "mermaid" || KrokiRenderer.IsSupportedDiagram(lang) || TikzRenderer.IsTikz(lang))
                        {
                            await RenderAndCacheDiagramAsync(innerCode, lang);
                            
                            // To prevent cache miss when ConvertToOfficeHtmlAsync uses raw text, 
                            // we also cache the raw text if it's available.
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                await RenderAndCacheDiagramAsync(text.Trim(), lang);
                            }
                            
                            hasAnyMatch = true;
                        }
                    }
                }
                
                // Chỉ dò mã sơ đồ THÔ (không fence) khi chưa khớp bất kỳ khối ```lang nào,
                // tránh gửi nhầm cả dấu ``` lên Kroki gây lỗi cú pháp.
                if (text != null && !hasAnyMatch)
                {
                    string trimmed = text.Trim();
                    if (MermaidRenderer.IsRawMermaidCode(trimmed))
                    {
                        await RenderAndCacheDiagramAsync(trimmed, "mermaid");
                        await RenderAndCacheDiagramAsync(text, "mermaid");
                        await RenderAndCacheDiagramAsync(text.TrimEnd(), "mermaid");
                    }
                    else
                    {
                        string detectedLang = KrokiRenderer.DetectRawDiagramLanguage(trimmed);
                        if (detectedLang != null)
                        {
                            await RenderAndCacheDiagramAsync(trimmed, detectedLang);
                            await RenderAndCacheDiagramAsync(text, detectedLang);
                            await RenderAndCacheDiagramAsync(text.TrimEnd(), detectedLang);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[Mermaid Cache LỖI] {ex.Message}");
            }
        }

        // ============================
        // CORE: PROCESS CLIPBOARD
        // ============================

        private void ProcessClipboard(bool isStartup)
        {
            if (_isPaused)
            {
                Program.Log("Bỏ qua: Ứng dụng đang tạm dừng.");
                return;
            }

            if (ClipboardHelper.IsAlreadyProcessed())
            {
                Program.Log("Bỏ qua: Dữ liệu này đã được HapiCopi xử lý.");
                return;
            }

            // Tăng mã tuần tự để đồng bộ hóa luồng AI bất đồng bộ
            long currentSeq = ++_currentClipboardSeq;

            try
            {
                // Reset trạng thái dán của lượt copy mới
                _hasPastedSinceCopy = false;
                _lastPastedWindowHandle = IntPtr.Zero;
                _lastPasteTime = DateTime.MinValue;
                _isLastPasteTerminal = false;

                string originalText = ClipboardHelper.GetPlainText();
                string originalHtml = "";
                bool hasHtml = false;
                if (Clipboard.ContainsText(TextDataFormat.Html))
                {
                    originalHtml = Clipboard.GetText(TextDataFormat.Html);
                    hasHtml = !string.IsNullOrEmpty(originalHtml);
                }

                if (string.IsNullOrEmpty(originalText) && !hasHtml)
                {
                    Program.Log("Bỏ qua: Clipboard trống.");
                    return;
                }



                // 2. Xác định các cờ phân tích định dạng
                ActiveAppInfo sourceApp = ActiveWindowHelper.GetActiveAppInfo();
                string appName = sourceApp.ProcessName ?? "Unknown";

                bool mlFormatEnabled = (AppConfig.Get("ML_FORMAT_ENABLED") ?? "true").ToLower() != "false";
                bool isUnstructured = mlFormatEnabled && !isStartup && !string.IsNullOrEmpty(originalText) && AutoFormatter.IsUnstructuredRawText(originalText);
                bool isMarkdownOrAi = IsMarkdownOrAiHtml(originalText, originalHtml);
                bool isPdfText = !isStartup && PdfHeuristicCleaner.IsLikelyPdfText(originalText, originalHtml);

                // Phát hiện mã sơ đồ (Mermaid/Kroki) SỚM: mã sơ đồ cực nhạy với xuống dòng và fence ```,
                // nên phải bỏ qua AutoFormatter (vốn nối dòng/dọn rác) để không phá hỏng cú pháp sơ đồ.
                bool containsDiagram = (!string.IsNullOrEmpty(originalText) && KrokiRenderer.ContainsAnyDiagram(originalText)) ||
                                       KrokiRenderer.ContainsAnyDiagramHtml(originalHtml);

                bool shouldFormat = isMarkdownOrAi || isUnstructured || isPdfText || containsDiagram;

                // Cache dữ liệu gốc để xử lý dán động
                _lastRawText = originalText;
                _lastRawHtml = originalHtml;
                _lastWasProcessed = shouldFormat;

                // Khởi tạo history item
                var historyItem = new ClipboardHistoryItem
                {
                    AppName = appName,
                    OriginalText = originalText,
                    OriginalHtml = originalHtml
                };

                // Capture all clipboard formats for deep inspection (Phase 1)
                try
                {
                    IDataObject dataObj = Clipboard.GetDataObject();
                    if (dataObj != null)
                    {
                        foreach (string format in dataObj.GetFormats(false))
                        {
                            try
                            {
                                object? data = dataObj.GetData(format);
                                if (data is string strData)
                                {
                                    historyItem.ClipboardFormats[format] = strData;
                                }
                                else if (data is byte[] bytes)
                                {
                                    string hex = string.Join(" ", bytes.Take(32).Select(b => b.ToString("X2")));
                                    string ascii = System.Text.Encoding.ASCII.GetString(bytes.Take(32).ToArray());
                                    ascii = new string(ascii.Select(c => char.IsControl(c) ? '.' : c).ToArray());
                                    historyItem.ClipboardFormats[format] = $"[Mảng Byte: {bytes.Length} bytes]\r\nHex: {hex}{(bytes.Length > 32 ? "..." : "")}\r\nASCII: {ascii}";
                                }
                                else if (data != null)
                                {
                                    historyItem.ClipboardFormats[format] = $"[{data.GetType().Name}] {data}";
                                }
                                else
                                {
                                    historyItem.ClipboardFormats[format] = "(null)";
                                }
                            }
                            catch (Exception ex)
                            {
                                historyItem.ClipboardFormats[format] = $"[Lỗi đọc dữ liệu: {ex.Message}]";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"[DLP] Lỗi đọc danh sách formats từ clipboard: {ex.Message}");
                }

                // Nếu là Startup hoặc không cần định dạng và không quét bảo mật (hoặc chỉ quét bảo mật)
                bool piiScanEnabled = (AppConfig.Get("PII_SCAN_ENABLED") ?? "true").ToLower() != "false";

                // Chạy luồng xử lý ngầm (đảm bảo thứ tự trước sau rõ ràng: Định dạng -> Bảo mật)
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        string formattedText = originalText;
                        string formattedHtml = originalHtml;
                        string status = "Bỏ qua";

                        // ==================================================
                        // BƯỚC 1: ĐỊNH DẠNG & TÁI CẤU TRÚC (Làm việc trên văn bản GỐC sạch sẽ)
                        // ==================================================
                        if (shouldFormat)
                        {
                            // Kiểm tra xem HTML gốc có chứa cấu trúc bảng/danh sách thực sự không.
                            // Nếu có, ưu tiên giữ nguyên cấu trúc HTML thay vì tái tạo từ plain text bị vỡ.
                            bool htmlHasRichStructure = hasHtml && !string.IsNullOrEmpty(originalHtml) &&
                                (originalHtml.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 originalHtml.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 originalHtml.IndexOf("<ol", StringComparison.OrdinalIgnoreCase) >= 0);

                            if (containsDiagram)
                            {
                                // Giữ nguyên văn bản gốc để bảo toàn fence ``` và các dòng của mã sơ đồ.
                                Program.Log("Phát hiện mã sơ đồ (Mermaid/Kroki). Bỏ qua AutoFormatter, giữ nguyên mã nguồn sơ đồ.");
                                status = "Đã xử lý (Sơ đồ)";
                            }
                            else if (isUnstructured && !htmlHasRichStructure)
                            {
                                Program.Log("Phát hiện văn bản thô phi cấu trúc. Chạy AutoFormatter.FormatTextSmart...");
                                formattedText = AutoFormatter.FormatTextSmart(originalText);
                                status = "Heuristic Reconstruct";
                            }
                            else if (isUnstructured && htmlHasRichStructure)
                            {
                                Program.Log("Văn bản thô phi cấu trúc nhưng HTML gốc có bảng/danh sách. Ưu tiên giữ cấu trúc HTML.");
                                status = "Đã xử lý (HTML ưu tiên)";
                            }
                            else if (isPdfText)
                            {
                                Program.Log("Phát hiện văn bản PDF đứt gãy. Chạy AutoFormatter.FormatTextSmart...");
                                formattedText = AutoFormatter.FormatTextSmart(originalText);
                                status = "PDF Heuristic";
                            }
                            else
                            {
                                status = "Đã xử lý";
                            }

                            if ((formattedText != null && KrokiRenderer.ContainsAnyDiagram(formattedText)) || KrokiRenderer.ContainsAnyDiagramHtml(_lastRawHtml))
                            {
                                await PreCacheDiagramsAsync(formattedText, _lastRawHtml);
                            }

                            WordFormatInfo formatInfo = WordInterop.GetCurrentFormatWithTimeout(80);
                            formatInfo.IsWordTarget = formatInfo.IsFromActiveWord;
                            formatInfo.MermaidImageCache = _mermaidCache;

                            // Quyết định đường xử lý HTML:
                            // - Nếu HTML gốc có cấu trúc phong phú (bảng, danh sách), LUÔN dùng nhánh clean HTML
                            //   để bảo toàn cấu trúc từ web nguồn (Kaggle, Wikipedia, v.v.)
                            // - Nếu không có HTML hoặc là text thuần, dùng nhánh convert từ Markdown/plain text
                            bool hasMermaid = (formattedText != null && KrokiRenderer.ContainsAnyDiagram(formattedText)) || KrokiRenderer.ContainsAnyDiagramHtml(_lastRawHtml);
                            bool shouldUseHtmlCleanPath = hasHtml && (!isUnstructured || htmlHasRichStructure) && !isPdfText;

                            if (shouldUseHtmlCleanPath)
                            {
                                string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(originalHtml);
                                string cleanFragment = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, formatInfo);
                                formattedHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
                            }
                            else
                            {
                                formattedHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(formattedText ?? "", formatInfo);
                            }

                            formattedText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(formattedText ?? "");
                        }

                        // ==================================================
                        // BƯỚC 2: QUÉT BẢO MẬT & CHE CHẮN DLP (Chạy trên kết quả sau định dạng)
                        // ==================================================
                        PiiScanResult? piiResult = null;
                        if (piiScanEnabled && !isStartup && !string.IsNullOrEmpty(formattedText))
                        {
                            piiResult = PiiScanner.Scan(formattedText);
                        }

                        historyItem.ProcessedText = formattedText;
                        historyItem.PiiResult = piiResult;
                        historyItem.Status = status;

                        if (piiResult != null && piiResult.HasPii)
                        {
                            // Phát hiện nhạy cảm: Che chắn và ghi đè Clipboard bằng bản sạch
                            Program.Log($"[DLP] Phát hiện {piiResult.Matches.Count} thông tin nhạy cảm trên văn bản đã định dạng. Tiến hành che bảo vệ.");
                            
                            string sanitizedText = piiResult.GetSanitizedText(formattedText);
                            string sanitizedHtml = "";
                            if (shouldFormat)
                            {
                                WordFormatInfo formatInfo = WordInterop.GetCurrentFormatWithTimeout(80);
                                formatInfo.IsWordTarget = formatInfo.IsFromActiveWord;
                                formatInfo.MermaidImageCache = _mermaidCache;
                                sanitizedHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(sanitizedText, formatInfo);
                            }

                            // Ghi đè Clipboard bằng bản sạch bảo mật
                            SetClipboardOnStaThread(sanitizedText, sanitizedHtml);

                            // QUAN TRỌNG: ép mọi đường dán (Notepad/Word/Docs/LaTeX) dùng bản đã che,
                            // tránh OnPastePressed dựng lại từ bản gốc chưa che gây rò rỉ.
                            _lastRawText = sanitizedText;
                            _lastRawHtml = "";

                            historyItem.ProcessedText = sanitizedText;
                            historyItem.Status = $"🛡️ Đã che ({status})";

                            _syncContext.Post(_ => AddToHistory(historyItem), null);

                            // Hiển thị Toast Bảo mật (Cho phép khôi phục lại bản định dạng gốc formattedText & formattedHtml)
                            bool toastEnabled = (AppConfig.Get("PII_TOAST_ENABLED") ?? "true").ToLower() != "false";
                            if (toastEnabled)
                            {
                                ShowPiiToast(piiResult, formattedText, formattedHtml, historyItem);
                            }
                        }
                        else
                        {
                            // An toàn: Đẩy bản định dạng sạch vào Clipboard
                            if (shouldFormat)
                            {
                                SetClipboardOnStaThread(formattedText, formattedHtml);
                            }

                            _syncContext.Post(_ => AddToHistory(historyItem), null);

                            // Hiển thị thông báo định dạng tương ứng
                            if (shouldFormat && !isStartup && !_hasShownWelcome)
                            {
                                string tipText = isStartup
                                    ? "Đã tự động tối ưu hóa dữ liệu clipboard có sẵn!"
                                    : "Bắt đầu tự động tối ưu hóa clipboard!";
                                _notifyIcon.ShowBalloonTip(2000, "HapiCopi kích hoạt!", tipText, ToolTipIcon.None);
                                _hasShownWelcome = true;
                            }
                        }

                        // Luồng AI/PDF ngầm chậm đã được gỡ bỏ trong thiết kế Pivot để tránh lỗi Smart Replace đè phím.
                        // Văn bản đã được tối ưu hóa đồng bộ tức thời ở trên.
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"Lỗi xử lý clipboard ngầm: {ex.Message}\n{ex.StackTrace}");
                        historyItem.Status = "Lỗi";
                        _syncContext.Post(_ => AddToHistory(historyItem), null);
                    }
                });
            }
            catch (Exception ex)
            {
                Program.Log($"LỖI TRONG ProcessClipboard: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ============================
        // DLP: TOAST CẢNH BÁO PII
        // ============================

        private void ShowPiiToast(PiiScanResult piiResult, string originalText, string originalHtml, ClipboardHistoryItem historyItem)
        {
            _syncContext.Post(_ =>
            {
                try
                {
                    // Đóng toast cũ nếu còn
                    if (_currentToast != null && !_currentToast.IsDisposed)
                    {
                        _currentToast.Close();
                    }

                    string summary = piiResult.GetSummary();
                    Program.Log($"[DLP Toast] Tạo Toast dạng Security-First với summary: {summary}");
                    
                    // Khởi tạo Toast ở chế độ Security-First (màu xanh lá cây, tự động che trước)
                    _currentToast = new PiiToastForm(summary, autoCloseSeconds: 6, isSecurityFirst: true);

                    // Phát âm thanh cảnh báo dịu tai
                    PlayGentleAlert();

                    // Người dùng chọn ĐỒNG Ý giữ bản đã che (hoặc tự động đóng)
                    _currentToast.OnSanitizeClicked += () =>
                    {
                        Program.Log("[DLP] Người dùng chọn ĐỒNG Ý giữ bản đã che.");
                        // Không cần thay đổi gì thêm vì clipboard đã được che an toàn từ trước
                    };

                    // Người dùng chọn KHÔI PHỤC (Rollback) về bản gốc đã định dạng
                    _currentToast.OnIgnoreClicked += () =>
                    {
                        try
                        {
                            // Tự học ngoại lệ: Thêm các chuỗi bị che vào danh sách ngoại lệ exclusions.dat để lần sau không chặn
                            if (piiResult != null && piiResult.Matches != null)
                            {
                                foreach (var match in piiResult.Matches)
                                {
                                    ExclusionManager.AddExclusion(match.Value);
                                }
                            }

                            // 1. Khôi phục lại bản gốc đã định dạng (bao gồm cả HTML) vào Clipboard
                            _isSimulatingClipboard = true;
                            ClipboardHelper.SetOfficeClipboard(originalText, originalHtml);
                            _isSimulatingClipboard = false;
                            
                            historyItem.Status = "⚠ Đã khôi phục";
                            Program.Log("[DLP] Người dùng chọn KHÔI PHỤC bản gốc. Đã ghi nhận ngoại lệ và trả lại dữ liệu.");

                            // 2. Tự động thay thế bản che đã dán bằng bản gốc (nếu thỏa mãn các điều kiện an toàn)
                            bool isWithinTimeLimit = (DateTime.Now - _lastPasteTime).TotalSeconds <= 5.0;

                            if (_hasPastedSinceCopy && 
                                _lastPastedWindowHandle != IntPtr.Zero && 
                                isWithinTimeLimit && 
                                !_isLastPasteTerminal)
                            {
                                _syncContext.Post(async __ =>
                                {
                                    try
                                    {
                                        Program.Log("[DLP Rollback] Đang tự động khôi phục bản gốc tại ứng dụng đích...");
                                        
                                        // A. Khôi phục focus cho ứng dụng đích
                                        ActiveWindowHelper.SetForegroundWindow(_lastPastedWindowHandle);
                                        await System.Threading.Tasks.Task.Delay(150); // Chờ cửa sổ nhận focus ổn định

                                        // B. Gửi Ctrl+Z để hoàn tác việc dán bản che
                                        SendKeys.SendWait("^z");
                                        await System.Threading.Tasks.Task.Delay(100);

                                        // C. Gửi Ctrl+V để dán bản gốc
                                        SendKeys.SendWait("^v");
                                        Program.Log("[DLP Rollback] Đã tự động hoàn tất khôi phục bản gốc thành công!");
                                    }
                                    catch (Exception ex)
                                    {
                                        Program.Log($"[DLP Rollback LỖI] {ex.Message}");
                                    }
                                }, null);
                            }
                            else if (_hasPastedSinceCopy)
                            {
                                string reason = "";
                                if (!isWithinTimeLimit) reason = "quá giới hạn thời gian (5 giây)";
                                else if (_isLastPasteTerminal) reason = "ứng dụng đích là Terminal";
                                else reason = "cửa sổ không hợp lệ";

                                Program.Log($"[DLP Rollback] Bỏ qua khôi phục tự động tại ứng dụng do: {reason}. Chỉ khôi phục clipboard.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _isSimulatingClipboard = false;
                            Program.Log($"[DLP Rollback LỖI] {ex.Message}");
                        }
                    };

                    _currentToast.FormClosed += (s, args) => _currentToast = null;
                    _currentToast.Show();
                    Program.Log($"[DLP Toast] Toast đã Show() thành công.");
                }
                catch (Exception ex)
                {
                    Program.Log($"[DLP Toast LỖI] {ex.Message}\n{ex.StackTrace}");
                }
            }, null);
        }

        /// <summary>
        /// Chuyển đổi định dạng URL trình duyệt sang đường dẫn vật lý cục bộ nếu là file PDF.
        /// </summary>
        private string? GetLocalPdfPath(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                string path = url;
                if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(path);
                    path = uri.LocalPath;
                }
                else if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(path);
                    path = uri.LocalPath;
                }

                // Giải mã các ký tự escape URL (%20 -> khoảng trắng, v.v.)
                path = Uri.UnescapeDataString(path);
                
                // Chuẩn hóa dấu gạch chéo cho hệ điều hành Windows
                path = path.Replace('/', '\\');

                if (System.IO.File.Exists(path) && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[PDF Bridge] Lỗi phân tích đường dẫn URL PDF: {ex.Message}");
            }
            return null;
        }
        // Các phương thức CallPdfBridgeAndReplaceAsync và CallAiAndReplaceAsync
        // đã được gỡ bỏ hoàn toàn để tránh việc giả lập đè phím ngầm (Smart Replace) gây lỗi tài liệu của người dùng.



        private bool IsCorrectedVersion(string newText, string oldText)
        {
            if (string.IsNullOrEmpty(newText) || string.IsNullOrEmpty(oldText)) return false;

            string normNew = NormalizeForSimilarity(newText);
            string normOld = NormalizeForSimilarity(oldText);

            if (string.IsNullOrEmpty(normNew) || string.IsNullOrEmpty(normOld)) return false;

            string[] wordsNew = normNew.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] wordsOld = normOld.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int maxLen = Math.Max(wordsNew.Length, wordsOld.Length);
            if (maxLen < 15) return false;

            if (Math.Abs(wordsNew.Length - wordsOld.Length) > maxLen * 0.3) return false;

            int distance = GetWordEditDistance(wordsNew, wordsOld);
            double similarity = 1.0 - ((double)distance / maxLen);

            Program.Log($"[ML Similarity] So sánh độ tương đồng từ vựng: {similarity:P2} (Edit Distance = {distance}/{maxLen} từ)");

            return similarity >= 0.80;
        }

        private string NormalizeForSimilarity(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string lower = text.ToLowerInvariant();
            
            char[] chars = lower.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c))
                {
                    chars[i] = ' ';
                }
            }
            
            string clean = new string(chars);
            return Regex.Replace(clean, @"\s+", " ").Trim();
        }

        private int GetWordEditDistance(string[] s, string[] t)
        {
            int n = s.Length;
            int m = t.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            int[] prev = new int[m + 1];
            int[] curr = new int[m + 1];

            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                int[] temp = prev;
                prev = curr;
                curr = temp;
            }
            return prev[m];
        }

        // ============================
        // PASTE INTERCEPTION (Giữ nguyên logic định dạng)
        // ============================

        private async void OnPastePressed()
        {
            // Nếu có Toast PII đang hiển thị, ghi nhận cửa sổ vừa dán để có thể tự động thay thế sau đó
            if (_currentToast != null && !_currentToast.IsDisposed)
            {
                try
                {
                    ActiveAppInfo activeApp = ActiveWindowHelper.GetActiveAppInfo();
                    string procName = activeApp.ProcessName ?? "";
                    _isLastPasteTerminal = procName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
                                           procName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                                           procName.Equals("wt", StringComparison.OrdinalIgnoreCase) ||
                                           procName.Equals("conhost", StringComparison.OrdinalIgnoreCase) ||
                                           procName.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
                                           procName.Equals("wsl", StringComparison.OrdinalIgnoreCase);

                    _lastPastedWindowHandle = activeApp.WindowHandle;
                    _hasPastedSinceCopy = true;
                    _lastPasteTime = DateTime.Now;

                    Program.Log($"[DLP] Ghi nhận cửa sổ dán: Process={procName}, Hwnd={activeApp.WindowHandle}, IsTerminal={_isLastPasteTerminal}");
                }
                catch (Exception ex)
                {
                    Program.Log($"[DLP] Lỗi ghi nhận cửa sổ dán: {ex.Message}");
                }
            }

            if (_isPaused || IsSimulatingPaste || !_lastWasProcessed) return;

            try
            {
                // 1. Quét ứng dụng active tại tiền cảnh
                ActiveAppInfo appInfo = ActiveWindowHelper.GetActiveAppInfo();
                Program.Log($"Phát hiện dán vào ứng dụng: Process={appInfo.ProcessName}, Class={appInfo.ClassName}, Title={appInfo.WindowTitle}");

                if (appInfo.IsGoogleDocs)
                {
                    // Tối ưu hóa cho Google Docs: Xóa sạch font-family và font-size
                    Program.Log("Xào nấu Clipboard cho Google Docs (Tự thừa kế style con trỏ)...");
                    var docsFormat = new WordFormatInfo { IsFromActiveWord = false, MermaidImageCache = _mermaidCache };

                    string docsHtml = "";
                    bool hasMermaid = (_lastRawText != null && KrokiRenderer.ContainsAnyDiagram(_lastRawText)) || KrokiRenderer.ContainsAnyDiagramHtml(_lastRawHtml);
                    if (!string.IsNullOrEmpty(_lastRawHtml) && !hasMermaid)
                    {
                        string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(_lastRawHtml);
                        string cleanFragment = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, docsFormat);
                        docsHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
                    }
                    else
                    {
                        docsHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(_lastRawText, docsFormat);
                    }

                    string cleanPlainText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(_lastRawText);
                    ClipboardHelper.SetOfficeClipboard(cleanPlainText, docsHtml);
                }
                else if (appInfo.IsLaTeXEditor)
                {
                    // Tối ưu hóa cho LaTeX: Nếu chứa bảng biểu thì convert thành LaTeX code, ngược lại khôi phục LaTeX gốc
                    Program.Log("Xào nấu Clipboard cho LaTeX Editor...");

                    string tempHtml = "";
                    if (!string.IsNullOrEmpty(_lastRawHtml))
                    {
                        string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(_lastRawHtml);
                        tempHtml = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, new WordFormatInfo { IsFromActiveWord = false, MermaidImageCache = _mermaidCache });
                    }
                    else
                    {
                        if (WebViewManager.IsInitialized)
                        {
                            tempHtml = await WebViewManager.ConvertMarkdownToHtmlAsync(_lastRawText, false);
                        }
                        else
                        {
                            tempHtml = Markdig.Markdown.ToHtml(_lastRawText);
                        }
                    }

                    if (tempHtml.Contains("<table"))
                    {
                        string latexTable = MarkdownToWordConverter.ConvertHtmlTableToLatex(tempHtml);
                        ClipboardHelper.SetOfficeClipboard(latexTable, "");
                        Program.Log("Đã convert bảng sang mã LaTeX.");
                    }
                    else
                    {
                        ClipboardHelper.SetOfficeClipboard(_lastRawText, "");
                        Program.Log("Đã khôi phục LaTeX gốc.");
                    }
                }
                else if (appInfo.IsVSCode || appInfo.IsNotepadOrEditor)
                {
                    Program.Log("Khôi phục clipboard gốc cho VS Code / Notepad...");
                    ClipboardHelper.SetOfficeClipboard(_lastRawText, "");
                }
                else if (appInfo.IsAnyWord)
                {
                    WordFormatInfo formatInfo;
                    if (appInfo.IsMicrosoftWord)
                    {
                        Program.Log("Xào nấu Clipboard real-time cho MS Word...");
                        formatInfo = WordInterop.GetCurrentFormatWithTimeout(80);
                    }
                    else
                    {
                        Program.Log("Xào nấu Clipboard cho Word Online...");
                        formatInfo = new WordFormatInfo { IsFromActiveWord = false };
                    }
                    formatInfo.IsWordTarget = true;
                    formatInfo.MermaidImageCache = _mermaidCache;

                    string officeHtml = "";
                    bool hasMermaid = (_lastRawText != null && KrokiRenderer.ContainsAnyDiagram(_lastRawText)) || KrokiRenderer.ContainsAnyDiagramHtml(_lastRawHtml);
                    if (!string.IsNullOrEmpty(_lastRawHtml) && !hasMermaid)
                    {
                        string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(_lastRawHtml);
                        string cleanFragment = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, formatInfo);
                        officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
                    }
                    else
                    {
                        officeHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(_lastRawText, formatInfo);
                    }

                    string cleanPlainText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(_lastRawText);
                    ClipboardHelper.SetOfficeClipboard(cleanPlainText, officeHtml);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi trong OnPastePressed: {ex.Message}");
            }
        }

        // ============================
        // MARKDOWN/AI HTML DETECTION (Giữ nguyên)
        // ============================

        private static bool IsMarkdownOrAiHtml(string text, string html)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            // 1. Kiểm tra nếu dữ liệu copy là MÃ NGUỒN thực sự từ VS Code Editor
            if (ClipboardHelper.IsVscodeEditorCopy())
            {
                Program.Log("Bypass: Phát hiện copy từ VS Code Editor. Giữ nguyên định dạng gốc.");
                return false;
            }

            // 2. Nhận diện qua cấu trúc HTML trước
            if (!string.IsNullOrEmpty(html))
            {
                string htmlLower = html.ToLowerInvariant();

                bool isOfficeCopy = htmlLower.Contains("microsoft word") ||
                                    htmlLower.Contains("xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
                bool isGoogleDocsCopy = htmlLower.Contains("id=\"docs-internal-guid") || htmlLower.Contains("id=docs-internal-guid");

                if (!isOfficeCopy && !isGoogleDocsCopy)
                {
                    bool isBrowserCopy = htmlLower.Contains("sourceurl:");

                    if (htmlLower.Contains("class=\"markdown") ||
                        htmlLower.Contains("class=\"prose") ||
                        htmlLower.Contains("class=\"message") ||
                        htmlLower.Contains("class=\"chat") ||
                        htmlLower.Contains("class=\"response") ||
                        htmlLower.Contains("assistant-message") ||
                        htmlLower.Contains("chat-message") ||
                        htmlLower.Contains("<pre") ||
                        htmlLower.Contains("<code") ||
                        htmlLower.Contains("<table") ||
                        htmlLower.Contains("<blockquote") ||
                        htmlLower.Contains("<li") ||
                        htmlLower.Contains("<ul") ||
                        htmlLower.Contains("<ol") ||
                        htmlLower.Contains("<h1") || htmlLower.Contains("<h2") || htmlLower.Contains("<h3") ||
                        htmlLower.Contains("<h4") || htmlLower.Contains("<h5") || htmlLower.Contains("<h6") ||
                        htmlLower.Contains("<strong") || htmlLower.Contains("<b>") || htmlLower.Contains("<b ") ||
                        htmlLower.Contains("<em") || htmlLower.Contains("<i>") || htmlLower.Contains("<i ") ||
                        htmlLower.Contains("<a href") ||
                        htmlLower.Contains("katex") || htmlLower.Contains("mathjax") || htmlLower.Contains("mjx-") || htmlLower.Contains("<math") ||
                        (isBrowserCopy && (htmlLower.Contains("<p") || htmlLower.Contains("<br") || htmlLower.Contains("<div") || htmlLower.Contains("<span"))))
                    {
                        Program.Log("Nhận diện qua cấu trúc HTML: Tìm thấy dấu hiệu AI/Markdown/Table/Blockquote/Math/WebPage.");
                        return true;
                    }
                }
            }

            // 3. Kiểm tra ký tự Markdown thô
            if (Regex.IsMatch(text, @"^\s*#{1,6}\s+", RegexOptions.Multiline)) return true;
            if (Regex.IsMatch(text, @"(\*\*|\*|__|_).+?\1")) return true;
            if (Regex.IsMatch(text, @"^\s*([\-\*\+••]\s*|\d+\.\s*)", RegexOptions.Multiline)) return true;
            if (text.Contains("`")) return true;
            if (text.Contains(" | ") && Regex.IsMatch(text, @"\|.*\|", RegexOptions.Multiline)) return true;

            // 4. Kiểm tra công thức toán học LaTeX
            if (text.Contains("\\(") && text.Contains("\\)")) return true;
            if (text.Contains("\\[") && text.Contains("\\]")) return true;
            if (text.Contains("$$")) return true;

            // 5. Inline math $...$
            var inlineMathRegex = new Regex(@"\$([^$\n]+)\$");
            foreach (Match match in inlineMathRegex.Matches(text))
            {
                string inner = match.Groups[1].Value;
                if (inner.Contains("\\") || inner.Contains("_") || inner.Contains("^") || inner.Contains("{") || inner.Contains("}") || inner.Contains("+") || inner.Contains("=") || inner.Contains("-") || inner.Contains("*") || inner.Contains("/"))
                {
                    return true;
                }
            }

            // 6. Lệnh toán học LaTeX phổ biến
            if (text.Contains("\\frac") || text.Contains("\\sin") || text.Contains("\\cos") ||
                text.Contains("\\tan") || text.Contains("\\alpha") || text.Contains("\\beta") ||
                text.Contains("\\gamma") || text.Contains("\\theta") || text.Contains("\\epsilon") ||
                text.Contains("\\E") || text.Contains("\\begin"))
            {
                return true;
            }

            // 7. Sơ đồ Mermaid và các sơ đồ khác (PlantUML, DBML, D2, v.v.)
            if (KrokiRenderer.ContainsAnyDiagram(text))
            {
                return true;
            }

            return false;
        }

        // ============================
        // TRAY ICON
        // ============================

        private Icon CreateAppIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(16, 16))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        using (Brush brush = new SolidBrush(Color.FromArgb(46, 204, 113)))
                        {
                            g.FillEllipse(brush, 0, 0, 15, 15);
                        }
                        using (Font font = new Font("Arial", 7, FontStyle.Bold))
                        {
                            g.DrawString("HC", font, Brushes.White, 0, 2);
                        }
                    }
                    IntPtr hIcon = bmp.GetHicon();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void PlayGentleAlert()
        {
            try
            {
                string soundPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");
                if (!System.IO.File.Exists(soundPath))
                {
                    soundPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Notification.wav");
                }
                if (System.IO.File.Exists(soundPath))
                {
                    using (var player = new System.Media.SoundPlayer(soundPath))
                    {
                        player.Play();
                    }
                }
                else
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
            }
            catch
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch {}
            }
        }

        private void OnExitClicked(object? sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _listener.Dispose();
            _keyboardHook.Dispose();
            Application.Exit();
        }
    }
}
