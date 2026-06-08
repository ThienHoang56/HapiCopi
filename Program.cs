using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HapiCopi
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Log("==================================================");
            Log("HapiCopi bắt đầu khởi chạy...");
            
            // Chạy ứng dụng thông qua ApplicationContext tùy biến để chạy ngầm không cần Form chính hiển thị
            Application.Run(new HapiCopiApplicationContext());
        }

        public static void Log(string message)
        {
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
            }
            catch {}
        }
    }

    public class HapiCopiApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ClipboardListener _listener;
        private readonly KeyboardHook _keyboardHook;
        private readonly ToolStripMenuItem _pauseMenuItem;
        private bool _isPaused = false;
        private bool _hasShownWelcome = false;

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
            Program.Log("Sự kiện thay đổi Clipboard kích hoạt.");
            ProcessClipboard(isStartup: false);
        }

        private void ProcessClipboard(bool isStartup)
        {
            if (_isPaused)
            {
                Program.Log("Bỏ qua: Ứng dụng đang tạm dừng.");
                return;
            }

            // Bỏ qua nếu clipboard do chính HapiCopi xử lý và ghi đè
            if (ClipboardHelper.IsAlreadyProcessed())
            {
                Program.Log("Bỏ qua: Dữ liệu này đã được HapiCopi xử lý.");
                return;
            }

            try
            {
                string text = ClipboardHelper.GetPlainText();
                Program.Log($"{(isStartup ? "[Startup] " : "")}Đọc văn bản thô từ Clipboard (độ dài: {text.Length} ký tự).");

                // Đọc HTML từ Clipboard nếu có để phục vụ phân tích cấu trúc
                string webHtml = "";
                bool hasHtml = false;
                try
                {
                    if (Clipboard.ContainsText(TextDataFormat.Html))
                    {
                        webHtml = Clipboard.GetText(TextDataFormat.Html);
                        hasHtml = !string.IsNullOrEmpty(webHtml);
                        Program.Log($"{(isStartup ? "[Startup] " : "")}Đọc HTML từ Clipboard (độ dài: {webHtml.Length} ký tự).");
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("Lỗi đọc HTML từ Clipboard: " + ex.Message);
                }
                
                if (string.IsNullOrEmpty(text) && !hasHtml)
                {
                    Program.Log("Bỏ qua: Clipboard trống.");
                    return;
                }

                bool shouldProcess = IsMarkdownOrAiHtml(text, webHtml);
                Program.Log($"Kết quả kiểm tra điều kiện xử lý (IsMarkdownOrAiHtml): {shouldProcess}");

                if (shouldProcess)
                {
                    // 1. Lưu lại cache gốc để xào nấu lúc paste
                    _lastRawText = text;
                    _lastRawHtml = webHtml;
                    _lastWasProcessed = true;
                    Program.Log("Đã cache dữ liệu Copy thô để chuẩn bị dán động.");

                    // 2. Chuyển đổi định dạng Office mặc định (Fallback lúc trượt Hook phím)
                    WordFormatInfo formatInfo = WordInterop.GetCurrentFormatWithTimeout(80);
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
                    ClipboardHelper.SetOfficeClipboard(cleanText, officeHtml);

                    if (!_hasShownWelcome)
                    {
                        string tipText = isStartup 
                            ? "Đã tự động tối ưu hóa dữ liệu clipboard có sẵn!" 
                            : "Bắt đầu tự động tối ưu hóa clipboard!";
                        _notifyIcon.ShowBalloonTip(2000, "HapiCopi kích hoạt!", 
                            tipText, 
                            ToolTipIcon.None);
                        _hasShownWelcome = true;
                    }
                }
                else
                {
                    if (!isStartup)
                    {
                        _lastWasProcessed = false;
                    }
                }
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
                    var docsFormat = new WordFormatInfo { IsFromActiveWord = false };
                    
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
                        tempHtml = MarkdownToWordConverter.CleanWebHtmlToOffice(htmlFragment, new WordFormatInfo { IsFromActiveWord = false });
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
 
        private static bool IsMarkdownOrAiHtml(string text, string html)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
 
            // 1. Kiểm tra nếu dữ liệu copy là MÃ NGUỒN thực sự từ VS Code Editor (chứa format vscode-editor-data)
            if (ClipboardHelper.IsVscodeEditorCopy())
            {
                Program.Log("Bypass: Phát hiện copy từ VS Code Editor. Giữ nguyên định dạng gốc.");
                return false;
            }
 
            // 2. Nhận diện qua cấu trúc HTML trước (để bắt được các thẻ math/katex/mathjax/prose/markdown)
            if (!string.IsNullOrEmpty(html))
            {
                string htmlLower = html.ToLowerInvariant();
                if (htmlLower.Contains("class=\"markdown") || 
                    htmlLower.Contains("class=\"prose") || 
                    htmlLower.Contains("<pre") || 
                    htmlLower.Contains("<code") ||
                    htmlLower.Contains("<table") ||
                    htmlLower.Contains("<blockquote") ||
                    (htmlLower.Contains("<li") && htmlLower.Contains("<ul")) ||
                    htmlLower.Contains("<h1") || htmlLower.Contains("<h2") || htmlLower.Contains("<h3") ||
                    htmlLower.Contains("katex") || htmlLower.Contains("mathjax") || htmlLower.Contains("mjx-") || htmlLower.Contains("<math"))
                {
                    Program.Log("Nhận diện qua cấu trúc HTML: Tìm thấy dấu hiệu AI/Markdown/Table/Blockquote/Math.");
                    return true;
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