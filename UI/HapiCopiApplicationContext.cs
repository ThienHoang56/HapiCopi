using System;
using System.Drawing;
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
        private readonly System.Collections.Generic.Dictionary<string, string> _mermaidCache = new System.Collections.Generic.Dictionary<string, string>();

        // Cache lưu trữ nội dung copy thô để xử lý dán động
        private string _lastRawText = "";
        private string _lastRawHtml = "";
        private bool _lastWasProcessed = false;

        public HapiCopiApplicationContext()
        {
            // 1. Thiết lập Menu chuột phải cho System Tray Icon
            var contextMenu = new ContextMenuStrip();
            
            var titleItem = new ToolStripMenuItem("HapiCopi v1.0 - Auto Word Format") { Enabled = false };
            titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
            
            _pauseMenuItem = new ToolStripMenuItem("Tạm dừng", null, OnPauseClicked);
            var exitMenuItem = new ToolStripMenuItem("Thoát", null, OnExitClicked);

            contextMenu.Items.Add(titleItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(_pauseMenuItem);
            contextMenu.Items.Add(exitMenuItem);

            // 2. Tạo System Tray Icon
            _notifyIcon = new NotifyIcon()
            {
                Icon = CreateAppIcon(),
                ContextMenuStrip = contextMenu,
                Text = "HapiCopi - Tự động định dạng Markdown sang Word",
                Visible = true
            };

            // Hiển thị thông báo chào mừng lần đầu khởi động
            _notifyIcon.ShowBalloonTip(4000, "HapiCopi đã kích hoạt!", 
                "Ứng dụng đã chạy ngầm thành công.\nHãy Ctrl+C text Markdown từ AI và Ctrl+V vào Word/Docs.", 
                ToolTipIcon.Info);

            // 3. Đăng ký Listener bắt sự kiện Clipboard
            _listener = new ClipboardListener();
            _listener.ClipboardChanged += OnClipboardChanged;

            // 4. Đăng ký Hook bàn phím bắt sự kiện dán chủ động
            _keyboardHook = new KeyboardHook();
            _keyboardHook.PastePressed += OnPastePressed;
            _keyboardHook.AutoFormatPressed += OnAutoFormatPressed;

            // 5. Thực hiện xử lý clipboard đang có sẵn ngay khi khởi động
            ProcessClipboard(isStartup: true);
        }

        private void OnPauseClicked(object? sender, EventArgs e)
        {
            _isPaused = !_isPaused;
            _pauseMenuItem.Checked = _isPaused;
            
            if (_isPaused)
            {
                _notifyIcon.Text = "HapiCopi (Đang tạm dừng)";
                _notifyIcon.ShowBalloonTip(1500, "HapiCopi", "Đã tạm dừng tự động định dạng.", ToolTipIcon.Warning);
            }
            else
            {
                _notifyIcon.Text = "HapiCopi - Tự động định dạng Markdown sang Word";
                _notifyIcon.ShowBalloonTip(1500, "HapiCopi", "Đã hoạt động trở lại.", ToolTipIcon.Info);
            }
        }

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

        private async System.Threading.Tasks.Task PreCacheMermaidDiagramsAsync(string text)
        {
            try
            {
                var mermaidRegex = new Regex(@"```mermaid\s*\r?\n(.*?)\r?\n```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var matches = mermaidRegex.Matches(text);
                Program.Log($"[DEBUG] PreCacheMermaidDiagramsAsync found {matches.Count} matches");
                
                foreach (Match match in matches)
                {
                    string innerCode = match.Groups[1].Value.Trim();
                    Program.Log($"[DEBUG] Match innerCode (first 50 chars): '{innerCode.Substring(0, Math.Min(50, innerCode.Length)).Replace("\r", "\\r").Replace("\n", "\\n")}'");
                    if (!_mermaidCache.ContainsKey(innerCode))
                    {
                        Program.Log($"[Mermaid] Tìm thấy biểu đồ, đang kết xuất ngầm...");
                        string dataUri = await MermaidRenderer.GetMermaidDataUriAsync(innerCode);
                        if (!string.IsNullOrEmpty(dataUri))
                        {
                            _mermaidCache[innerCode] = dataUri;
                            Program.Log($"[Mermaid] Kết xuất ngầm thành công. Kích thước base64: {dataUri.Length} ký tự.");
                        }
                        else
                        {
                            Program.Log($"[Mermaid] Kết xuất ngầm THẤT BẠI.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[Mermaid Cache LỖI] {ex.Message}");
            }
        }

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

                bool shouldProcess = IsMarkdownOrAiHtml(text, webHtml);
                if (!shouldProcess)
                {
                    if (!isStartup)
                    {
                        _lastWasProcessed = false;
                    }
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
                        Program.Log($"[DEBUG] text length: {text?.Length ?? 0}, hasHtml: {hasHtml}");
                        if (text != null && text.Contains("```mermaid"))
                        {
                            Program.Log("[DEBUG] text contains ```mermaid");
                            await PreCacheMermaidDiagramsAsync(text);
                        }
                        else
                        {
                            Program.Log("[DEBUG] text does NOT contain ```mermaid");
                        }

                        WordFormatInfo formatInfo = WordInterop.GetCurrentFormatWithTimeout(80);
                        formatInfo.MermaidImageCache = _mermaidCache;

                        string officeHtml = "";
                        if (hasHtml)
                        {
                            string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(webHtml);
                            string cleanFragment = MarkdownToWordConverter.CleanWebHtmlToOffice(htmlFragment, formatInfo);
                            officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
                        }
                        else
                        {
                            officeHtml = MarkdownToWordConverter.ConvertToOfficeHtml(text, formatInfo);
                        }

                        string cleanText = MarkdownToWordConverter.ProcessLatexInPlainText(text);
                        SetClipboardOnStaThread(cleanText, officeHtml);

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
                    }
                });
            }
            catch (Exception ex)
            {
                Program.Log($"LỖI TRONG ProcessClipboard: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnPastePressed()
        {
            if (_isPaused || !_lastWasProcessed) return;

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
                    if (!string.IsNullOrEmpty(_lastRawHtml))
                    {
                        string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(_lastRawHtml);
                        string cleanFragment = MarkdownToWordConverter.CleanWebHtmlToOffice(htmlFragment, docsFormat);
                        docsHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
                    }
                    else
                    {
                        docsHtml = MarkdownToWordConverter.ConvertToOfficeHtml(_lastRawText, docsFormat);
                    }
                    
                    string cleanPlainText = MarkdownToWordConverter.ProcessLatexInPlainText(_lastRawText);
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
                        tempHtml = MarkdownToWordConverter.CleanWebHtmlToOffice(htmlFragment, new WordFormatInfo { IsFromActiveWord = false, MermaidImageCache = _mermaidCache });
                    }
                    else
                    {
                        tempHtml = Markdig.Markdown.ToHtml(_lastRawText);
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
                    if (!string.IsNullOrEmpty(_lastRawHtml))
                    {
                        string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(_lastRawHtml);
                        string cleanFragment = MarkdownToWordConverter.CleanWebHtmlToOffice(htmlFragment, formatInfo);
                        officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
                    }
                    else
                    {
                        officeHtml = MarkdownToWordConverter.ConvertToOfficeHtml(_lastRawText, formatInfo);
                    }
                    
                    string cleanPlainText = MarkdownToWordConverter.ProcessLatexInPlainText(_lastRawText);
                    ClipboardHelper.SetOfficeClipboard(cleanPlainText, officeHtml);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi trong OnPastePressed: {ex.Message}");
            }
        }

        private async void OnAutoFormatPressed()
        {
            if (_isPaused) return;

            try
            {
                // Kiểm tra ứng dụng active tại tiền cảnh
                ActiveAppInfo appInfo = ActiveWindowHelper.GetActiveAppInfo();
                Program.Log($"Phát hiện phím tắt Ctrl+M tại ứng dụng: Process={appInfo.ProcessName}, Class={appInfo.ClassName}, Title={appInfo.WindowTitle}");

                bool isWord = appInfo.IsMicrosoftWord;
                bool isOtherEditor = appInfo.IsGoogleDocs || appInfo.IsWordOnline || appInfo.IsVSCode || appInfo.IsNotepadOrEditor || appInfo.IsLaTeXEditor;

                if (isWord || isOtherEditor)
                {
                    string appName = isWord ? "Microsoft Word" : (appInfo.IsGoogleDocs ? "Google Docs" : (appInfo.IsWordOnline ? "Word Online" : appInfo.ProcessName));
                    Program.Log($"Kích hoạt tự động định dạng thông minh trong {appName}...");

                    // Đánh dấu bắt đầu giả lập clipboard để ngăn sự kiện OnClipboardChanged can thiệp
                    if (!isWord)
                    {
                        _isSimulatingClipboard = true;
                    }

                    string rawText = "";
                    if (isWord)
                    {
                        rawText = WordInterop.GetSelectedText();
                    }
                    else
                    {
                        rawText = ClipboardSimulator.GetSelectedTextViaClipboard();
                    }

                    bool fromClipboard = false;
                    if (string.IsNullOrEmpty(rawText) || rawText.Trim().Length == 0)
                    {
                        if (isWord)
                        {
                            rawText = ClipboardHelper.GetPlainText();
                        }
                        else
                        {
                            rawText = ClipboardSimulator.OriginalText;
                        }

                        if (string.IsNullOrEmpty(rawText) || rawText.Trim().Length == 0)
                        {
                            Program.Log("Không có văn bản nào được bôi đen và Clipboard trống.");
                            _notifyIcon.ShowBalloonTip(1500, "HapiCopi", "Vui lòng bôi đen văn bản hoặc sao chép văn bản vào Clipboard trước khi định dạng.", ToolTipIcon.Warning);
                            if (!isWord)
                            {
                                ClipboardSimulator.RestoreClipboard();
                                _isSimulatingClipboard = false;
                            }
                            return;
                        }

                        fromClipboard = true;
                        Program.Log("Không phát hiện văn bản bôi đen. Lấy văn bản từ Clipboard để định dạng.");
                    }

                    // Không hiển thị balloon tip trước khi dán để tránh cướp focus của cửa sổ đang hoạt động
                    string formattedText = "";
                    bool isAiSuccess = false;
                    string warningMessage = "";

                    try
                    {
                        formattedText = await GeminiClient.ReformatWithGeminiAsync(rawText);
                        isAiSuccess = true;
                        Program.Log("Định dạng bằng Gemini AI thành công.");
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"Lỗi khi gọi Gemini API (Sẽ chuyển sang bộ định dạng cục bộ): {ex.Message}");
                        warningMessage = "Lỗi kết nối AI hoặc chưa cấu hình API Key. Đang dùng định dạng cục bộ offline...";
                        
                        // Fallback sang định dạng cục bộ
                        formattedText = AutoFormatter.FormatTextSmart(rawText);
                    }

                    if (!fromClipboard && formattedText == rawText)
                    {
                        Program.Log("Văn bản chọn đã chuẩn sạch, không cần định dạng lại.");
                        if (!isWord)
                        {
                            ClipboardSimulator.RestoreClipboard();
                            _isSimulatingClipboard = false;
                        }
                        
                        // Thông báo kết quả ngay cả khi không đổi để người dùng biết app đã phản hồi
                        _notifyIcon.ShowBalloonTip(1500, "HapiCopi", "Văn bản chọn đã chuẩn sạch, không có thay đổi.", ToolTipIcon.Info);
                        return;
                    }

                    // Thực hiện ghi đè ngược lại ứng dụng
                    bool success = false;
                    if (isWord)
                    {
                        success = WordInterop.WriteTextStreaming(formattedText);
                    }
                    else
                    {
                        success = ClipboardSimulator.WriteTextViaClipboard(formattedText);
                    }
                    
                    // Giải phóng cờ giả lập sau khi thao tác clipboard kết thúc hoàn toàn
                    if (!isWord)
                    {
                        _isSimulatingClipboard = false;
                    }

                    // Hiển thị thông báo kết quả SAU KHI đã dán xong (tránh cướp focus khi đang dán)
                    if (success)
                    {
                        if (!string.IsNullOrEmpty(warningMessage))
                        {
                            _notifyIcon.ShowBalloonTip(2000, "HapiCopi", "Đã định dạng xong (Offline Fallback do lỗi AI/Thiếu Key).", ToolTipIcon.Warning);
                        }
                        else
                        {
                            string source = isAiSuccess ? "Gemini AI" : "Offline";
                            _notifyIcon.ShowBalloonTip(1500, "HapiCopi", $"Đã định dạng xong văn bản! ({source})", ToolTipIcon.Info);
                        }
                    }
                }
                else
                {
                    Program.Log("Bỏ qua: Không hỗ trợ phím tắt Ctrl+M cho ứng dụng này.");
                    _notifyIcon.ShowBalloonTip(2000, "HapiCopi", "Tính năng này hỗ trợ định dạng trực tiếp trong Microsoft Word và Google Docs.", ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                // Bảo đảm cờ giả lập được giải phóng nếu có lỗi nghiêm trọng xảy ra
                _isSimulatingClipboard = false;
                Program.Log($"Lỗi trong OnAutoFormatPressed: {ex.Message}");
            }
        }

        private static bool IsMarkdownOrAiHtml(string text, string html)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            // 1. Kiểm tra nếu dữ liệu copy là MÃ NGUỒN thực sự từ VS Code Editor (chứa format vscode-editor-data)
            if (ClipboardHelper.IsVscodeEditorCopy())
            {
                Program.Log("Bypass: Phát hiện copy từ VS Code Editor. Giữ nguyên định dạng gốc.");
                return false;
            }

            // 2. Nhận diện qua cấu trúc HTML trước (để bắt được các thẻ math/katex/mathjax/prose/markdown/webpage)
            if (!string.IsNullOrEmpty(html))
            {
                string htmlLower = html.ToLowerInvariant();

                // Bỏ qua nếu là HTML gốc sinh ra bởi chính Microsoft Word/Excel/Office Desktop để giữ nguyên định dạng của Office
                bool isOfficeCopy = htmlLower.Contains("microsoft word") || 
                                    htmlLower.Contains("xmlns:o=\"urn:schemas-microsoft-com:office:office\"");

                if (!isOfficeCopy)
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

            // 3. Kiểm tra ký tự Markdown thô trong văn bản thô
            if (Regex.IsMatch(text, @"^\s*#{1,6}\s+", RegexOptions.Multiline)) return true;
            if (Regex.IsMatch(text, @"(\*\*|\*|__|_).+?\1")) return true;
            if (Regex.IsMatch(text, @"^\s*([\-\*\+••]\s*|\d+\.\s*)", RegexOptions.Multiline)) return true;
            if (text.Contains("`")) return true;
            if (text.Contains(" | ") && Regex.IsMatch(text, @"\|.*\|", RegexOptions.Multiline)) return true;

            // 4. Kiểm tra ký tự công thức toán học LaTeX chứa trong văn bản thô (ví dụ: \(...\) hoặc \[...\] hoặc $$)
            if (text.Contains("\\(") && text.Contains("\\)")) return true;
            if (text.Contains("\\[") && text.Contains("\\]")) return true;
            if (text.Contains("$$")) return true;

            // 5. Kiểm tra công thức toán học inline $...$ có chứa ký tự toán học đặc trưng để tránh nhầm với mệnh giá tiền
            var inlineMathRegex = new Regex(@"\$([^$\n]+)\$");
            foreach (Match match in inlineMathRegex.Matches(text))
            {
                string inner = match.Groups[1].Value;
                if (inner.Contains("\\") || inner.Contains("_") || inner.Contains("^") || inner.Contains("{") || inner.Contains("}") || inner.Contains("+") || inner.Contains("=") || inner.Contains("-") || inner.Contains("*") || inner.Contains("/"))
                {
                    return true;
                }
            }

            // 6. Kiểm tra các lệnh toán học LaTeX phổ biến trong văn bản thô
            if (text.Contains("\\frac") || text.Contains("\\sin") || text.Contains("\\cos") || 
                text.Contains("\\tan") || text.Contains("\\alpha") || text.Contains("\\beta") || 
                text.Contains("\\gamma") || text.Contains("\\theta") || text.Contains("\\epsilon") ||
                text.Contains("\\E"))
            {
                return true;
            }

            return false;
        }

        private Icon CreateAppIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(16, 16))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        // Tạo nền tròn màu xanh lục nổi bật
                        using (Brush brush = new SolidBrush(Color.FromArgb(46, 204, 113)))
                        {
                            g.FillEllipse(brush, 0, 0, 15, 15);
                        }
                        // Ghi chữ tắt "HC" của HapiCopi màu trắng
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
