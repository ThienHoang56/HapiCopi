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

        // DLP Toast tracking (tránh spam nhiều toast cùng lúc)
        private PiiToastForm? _currentToast;

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
            var settingsMenuItem = new ToolStripMenuItem("Cài đặt...", null, OnSettingsClicked);
            var exitMenuItem = new ToolStripMenuItem("Thoát", null, OnExitClicked);

            contextMenu.Items.Add(titleItem);
            contextMenu.Items.Add(new ToolStripSeparator());
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

            // 5. Thực hiện xử lý clipboard đang có sẵn ngay khi khởi động
            ProcessClipboard(isStartup: true);
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

                _settingsForm = new SettingsForm();
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
            _clipboardHistory.Add(item);
            while (_clipboardHistory.Count > MaxHistoryItems)
            {
                _clipboardHistory.RemoveAt(0);
            }

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

        private async System.Threading.Tasks.Task RenderAndCacheMermaidAsync(string innerCode)
        {
            if (!_mermaidCache.ContainsKey(innerCode))
            {
                string dataUri = "";
                if (WebViewManager.IsInitialized)
                {
                    Program.Log($"[Mermaid] WebView2 đã sẵn sàng. Tiến hành render OFFLINE...");
                    dataUri = await WebViewManager.RenderMermaidToPngAsync(innerCode, _mermaidCache.Count);
                }
                else
                {
                    Program.Log($"[Mermaid] WebView2 chưa sẵn sàng. Tiến hành render ONLINE qua API...");
                    dataUri = await MermaidRenderer.GetMermaidDataUriAsync(innerCode);
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

        private async System.Threading.Tasks.Task PreCacheMermaidDiagramsAsync(string text)
        {
            try
            {
                var mermaidRegex = new Regex(@"```mermaid\s*[\r\n]+([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
                var matches = mermaidRegex.Matches(text);

                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        string innerCode = match.Groups[1].Value.Trim();
                        await RenderAndCacheMermaidAsync(innerCode);
                    }
                }
                else
                {
                    string trimmed = text.Trim();
                    if (MermaidRenderer.IsRawMermaidCode(trimmed))
                    {
                        await RenderAndCacheMermaidAsync(trimmed);
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

            try
            {
                string text = ClipboardHelper.GetPlainText();
                string webHtml = "";
                bool hasHtml = false;
                if (Clipboard.ContainsText(TextDataFormat.Html))
                {
                    webHtml = Clipboard.GetText(TextDataFormat.Html);
                    hasHtml = !string.IsNullOrEmpty(webHtml);
                }

                if (string.IsNullOrEmpty(text) && !hasHtml)
                {
                    Program.Log("Bỏ qua: Clipboard trống.");
                    return;
                }

                // ============================
                // DLP: QUÉT PII TRƯỚC KHI XỬ LÝ
                // ============================
                ActiveAppInfo sourceApp = ActiveWindowHelper.GetActiveAppInfo();
                string appName = sourceApp.ProcessName ?? "Unknown";

                bool piiScanEnabled = (AppConfig.Get("PII_SCAN_ENABLED") ?? "true").ToLower() != "false";
                PiiScanResult? piiResult = null;

                if (piiScanEnabled && !isStartup && !string.IsNullOrEmpty(text))
                {
                    piiResult = PiiScanner.Scan(text);
                    if (piiResult.HasPii)
                    {
                        Program.Log($"[DLP] Phát hiện thông tin nhạy cảm: {piiResult.GetSummary()} ({piiResult.Matches.Count} mục)");

                        // Hiển thị Toast cảnh báo
                        bool toastEnabled = (AppConfig.Get("PII_TOAST_ENABLED") ?? "true").ToLower() != "false";
                        if (toastEnabled)
                        {
                            ShowPiiToast(piiResult, text);
                        }
                    }
                }

                // ============================
                // KIỂM TRA VÀ XỬ LÝ MARKDOWN
                // ============================
                bool shouldProcess = IsMarkdownOrAiHtml(text, webHtml);
                
                // Tạo history item
                var historyItem = new ClipboardHistoryItem
                {
                    AppName = appName,
                    OriginalText = text,
                    OriginalHtml = webHtml,
                    PiiResult = piiResult
                };

                if (!shouldProcess)
                {
                    // Heuristic: Dọn rác PDF siêu tốc nếu không phải Markdown/AI
                    if (!isStartup && PdfHeuristicCleaner.IsLikelyPdfText(text, webHtml))
                    {
                        Program.Log("Phát hiện văn bản PDF đứt gãy. Đang chạy Heuristic gỡ rối siêu tốc...");
                        string fixedText = PdfHeuristicCleaner.FixLineBreaks(text);
                        SetClipboardOnStaThread(fixedText, "");
                        Program.Log("Đã xử lý xong văn bản PDF bằng Heuristic.");
                        
                        historyItem.ProcessedText = fixedText;
                        historyItem.Status = "PDF Heuristic";
                    }
                    else if (!isStartup)
                    {
                        _lastWasProcessed = false;
                        historyItem.Status = "Bỏ qua";
                    }

                    if (!isStartup) AddToHistory(historyItem);
                    return;
                }

                _lastRawText = text;
                _lastRawHtml = webHtml;
                _lastWasProcessed = true;
                Program.Log("Đã cache dữ liệu Copy thô.");

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        if (text != null && (text.Contains("```mermaid") || MermaidRenderer.IsRawMermaidCode(text)))
                        {
                            await PreCacheMermaidDiagramsAsync(text);
                        }

                        WordFormatInfo formatInfo = WordInterop.GetCurrentFormatWithTimeout(80);
                        formatInfo.IsWordTarget = formatInfo.IsFromActiveWord;
                        formatInfo.MermaidImageCache = _mermaidCache;

                        string officeHtml = "";
                        bool hasMermaid = text != null && (text.Contains("```mermaid") || MermaidRenderer.IsRawMermaidCode(text));
                        if (hasHtml && !hasMermaid)
                        {
                            string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(webHtml);
                            string cleanFragment = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, formatInfo);
                            officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
                        }
                        else
                        {
                            officeHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(text ?? "", formatInfo);
                        }

                        string cleanText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(text ?? "");
                        SetClipboardOnStaThread(cleanText, officeHtml);

                        // Ghi nhận lịch sử
                        historyItem.ProcessedText = cleanText;
                        historyItem.Status = "Đã xử lý";
                        _syncContext.Post(_ => AddToHistory(historyItem), null);

                        if (!_hasShownWelcome)
                        {
                            string tipText = isStartup
                                ? "Đã tự động tối ưu hóa dữ liệu clipboard có sẵn!"
                                : "Bắt đầu tự động tối ưu hóa clipboard!";
                            _notifyIcon.ShowBalloonTip(2000, "HapiCopi kích hoạt!", tipText, ToolTipIcon.None);
                            _hasShownWelcome = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"Lỗi xử lý clipboard ngầm: {ex.Message}");
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

        private void ShowPiiToast(PiiScanResult piiResult, string originalText)
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
                    _currentToast = new PiiToastForm(summary);
                    _currentToast.OnSanitizeClicked += () =>
                    {
                        try
                        {
                            string sanitized = piiResult.GetSanitizedText(originalText);
                            _isSimulatingClipboard = true;
                            ClipboardHelper.SetOfficeClipboard(sanitized, "");
                            _isSimulatingClipboard = false;
                            Program.Log($"[DLP] Đã làm sạch clipboard (đã che {piiResult.Matches.Count} thông tin nhạy cảm).");
                        }
                        catch (Exception ex)
                        {
                            _isSimulatingClipboard = false;
                            Program.Log($"[DLP] Lỗi làm sạch clipboard: {ex.Message}");
                        }
                    };
                    _currentToast.FormClosed += (s, args) => _currentToast = null;
                    _currentToast.Show();
                }
                catch (Exception ex)
                {
                    Program.Log($"[DLP Toast LỖI] {ex.Message}");
                }
            }, null);
        }

        // ============================
        // PASTE INTERCEPTION (Giữ nguyên logic định dạng)
        // ============================

        private async void OnPastePressed()
        {
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
                    bool hasMermaid = _lastRawText != null && (_lastRawText.Contains("```mermaid") || MermaidRenderer.IsRawMermaidCode(_lastRawText));
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
                    bool hasMermaid = _lastRawText != null && (_lastRawText.Contains("```mermaid") || MermaidRenderer.IsRawMermaidCode(_lastRawText));
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

            // 7. Mermaid
            if (MermaidRenderer.IsRawMermaidCode(text))
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
