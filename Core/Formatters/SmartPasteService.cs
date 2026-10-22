using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Dịch vụ Dán thông minh Magic Wand (Smart Paste Service).
    /// Tự động đọc ngữ cảnh Word (lề, thụt dòng đầu, căn chỉnh justify/left, bullet/numbering list, font, size),
    /// biến đổi nội dung clipboard đồng bộ 100% với tài liệu hiện tại, đồng thời kích hoạt micro-widget
    /// phép thuật (sparkles indicator - 100% không dùng chữ) mang lại trải nghiệm mượt mà, không giật lag.
    /// </summary>
    public static class SmartPasteService
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        private const byte VK_SHIFT = 0x10;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public static void ReleaseShiftKey()
        {
            try
            {
                keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);
            }
            catch { }
        }

        /// <summary>
        /// Thực thi quy trình Dán thông minh khi người dùng nhấn Ctrl + Shift + V.
        /// </summary>
        public static async Task ExecuteSmartPasteAsync(
            string? rawText = null,
            string? rawHtml = null,
            Dictionary<string, string>? diagramCache = null)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // 1. Hiển thị ngay micro-widget phép thuật tại vị trí con trỏ chuột
                Point cursorPos = Cursor.Position;
                MagicWandIndicatorForm.ShowNearCursor(cursorPos);
                Program.Log($"[MagicWand] Kích hoạt tại ({cursorPos.X}, {cursorPos.Y})");

                // 2. Nhận diện ứng dụng đang kích hoạt ở tiền cảnh
                ActiveAppInfo appInfo = ActiveWindowHelper.GetActiveAppInfo();
                Program.Log($"[MagicWand] Đích dán: Process={appInfo.ProcessName}, Class={appInfo.ClassName}, Title={appInfo.WindowTitle}");

                // 3. Chuẩn bị dữ liệu đầu vào (fallback đọc trực tiếp clipboard nếu bộ nhớ đệm trống)
                string textToProcess = rawText ?? "";
                string htmlToProcess = rawHtml ?? "";

                if (string.IsNullOrWhiteSpace(textToProcess))
                {
                    textToProcess = ClipboardHelper.GetPlainText();
                }

                if (string.IsNullOrWhiteSpace(textToProcess) && string.IsNullOrWhiteSpace(htmlToProcess))
                {
                    Program.Log("[MagicWand] Clipboard hoàn toàn trống, bỏ qua định dạng.");
                    MagicWandIndicatorForm.NotifySuccess();
                    ReleaseShiftKey();
                    await Task.Delay(40);
                    ClipboardSimulator.SimulateCtrlV();
                    return;
                }

                // 4. Phân luồng xử lý theo ứng dụng đích
                if (appInfo.IsAnyWord)
                {
                    await ProcessForWordAsync(appInfo, textToProcess, htmlToProcess, diagramCache);
                }
                else if (appInfo.IsGoogleDocs)
                {
                    await ProcessForGoogleDocsAsync(textToProcess, htmlToProcess, diagramCache);
                }
                else if (appInfo.IsLaTeXEditor)
                {
                    ProcessForLatex(textToProcess, htmlToProcess);
                }
                else
                {
                    // Ứng dụng văn bản thông thường (VS Code, Notepad, Browser, ...)
                    ClipboardHelper.SetOfficeClipboard(textToProcess, "");
                }

                // 5. Báo hiệu thành công cho widget phép thuật (hiển thị tick xanh & bung nở nhẹ)
                MagicWandIndicatorForm.NotifySuccess();

                // 6. Nhả phím Shift để Word không bị nuốt phím tắt
                ReleaseShiftKey();

                // 7. Nghỉ 60ms cho hoạt ảnh và clipboard ổn định trước khi giả lập Ctrl+V
                await Task.Delay(60);
                ClipboardSimulator.SimulateCtrlV();

                sw.Stop();
                Program.Log($"[MagicWand] Hoàn tất toàn bộ quy trình Smart Paste trong {sw.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                Program.Log($"[MagicWand LỖI] {ex.Message}");
                MagicWandIndicatorForm.NotifySuccess();
                ReleaseShiftKey();
                ClipboardSimulator.SimulateCtrlV();
            }
        }

        private static async Task ProcessForWordAsync(
            ActiveAppInfo appInfo,
            string textToProcess,
            string htmlToProcess,
            Dictionary<string, string>? diagramCache)
        {
            WordFormatInfo formatInfo;
            if (appInfo.IsMicrosoftWord)
            {
                // Đọc ngữ cảnh Word hiện tại trong tối đa 80ms
                formatInfo = WordInterop.GetCurrentFormatWithTimeout(80);
            }
            else
            {
                formatInfo = new WordFormatInfo { IsFromActiveWord = false };
            }

            formatInfo.IsWordTarget = true;
            formatInfo.MermaidImageCache = diagramCache ?? new Dictionary<string, string>();

            string officeHtml;
            bool hasMermaid = (!string.IsNullOrEmpty(textToProcess) && KrokiRenderer.ContainsAnyDiagram(textToProcess)) ||
                              KrokiRenderer.ContainsAnyDiagramHtml(htmlToProcess);

            if (!string.IsNullOrEmpty(htmlToProcess) && !hasMermaid)
            {
                string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(htmlToProcess);
                string cleanFragment = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, formatInfo);
                string smartFragment = ApplySmartWordStyles(cleanFragment, formatInfo);
                officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(smartFragment);
            }
            else
            {
                // Chuyển Markdown sang HTML
                officeHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(textToProcess, formatInfo);
                string fragment = MarkdownToWordConverter.ExtractHtmlFragment(officeHtml);
                string smartFragment = ApplySmartWordStyles(fragment, formatInfo);
                officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(smartFragment);
            }

            string cleanPlainText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(textToProcess);
            FormulaTracker.ScanAndRegisterHtml(officeHtml);
            ClipboardHelper.SetOfficeClipboard(cleanPlainText, officeHtml);
        }

        private static async Task ProcessForGoogleDocsAsync(
            string textToProcess,
            string htmlToProcess,
            Dictionary<string, string>? diagramCache)
        {
            var docsFormat = new WordFormatInfo { IsFromActiveWord = false, MermaidImageCache = diagramCache ?? new Dictionary<string, string>() };
            string docsHtml;
            bool hasMermaid = (!string.IsNullOrEmpty(textToProcess) && KrokiRenderer.ContainsAnyDiagram(textToProcess)) ||
                              KrokiRenderer.ContainsAnyDiagramHtml(htmlToProcess);

            if (!string.IsNullOrEmpty(htmlToProcess) && !hasMermaid)
            {
                string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(htmlToProcess);
                string cleanFragment = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, docsFormat);
                docsHtml = MarkdownToWordConverter.PackageAsClipboardHtml(cleanFragment);
            }
            else
            {
                docsHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(textToProcess, docsFormat);
            }

            string cleanPlainText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(textToProcess);
            FormulaTracker.ScanAndRegisterHtml(docsHtml);
            ClipboardHelper.SetOfficeClipboard(cleanPlainText, docsHtml);
        }

        private static void ProcessForLatex(string textToProcess, string htmlToProcess)
        {
            if (!string.IsNullOrEmpty(htmlToProcess) && htmlToProcess.Contains("<table", StringComparison.OrdinalIgnoreCase))
            {
                string latexTable = MarkdownToWordConverter.ConvertHtmlTableToLatex(htmlToProcess);
                ClipboardHelper.SetOfficeClipboard(latexTable, "");
            }
            else
            {
                ClipboardHelper.SetOfficeClipboard(textToProcess, "");
            }
        }

        /// <summary>
        /// Áp dụng các quy tắc căn chỉnh tài liệu chuẩn xác đồng bộ với văn bản Word hiện tại:
        /// 1. Căn lề đều 2 bên (Justify) hoặc Left/Center/Right theo định dạng Word hiện tại.
        /// 2. Thụt đầu dòng (First-Line Indent, ví dụ 1cm = 28.35pt, 1.27cm = 36pt).
        /// 3. Thụt lề trái (Left Indent) của đoạn văn.
        /// 4. Đồng bộ Bullet hoặc gạch ngang (List markers & Hanging Indents) khi con trỏ đang nằm trong danh sách.
        /// 5. Đảm bảo Tiêu đề (Headings) không bị dính thụt lề đầu dòng sai lệch.
        /// </summary>
        public static string ApplySmartWordStyles(string htmlFragment, WordFormatInfo formatInfo)
        {
            if (string.IsNullOrWhiteSpace(htmlFragment)) return htmlFragment;

            var sb = new StringBuilder(htmlFragment);

            // Xác định chuỗi căn lề CSS
            string alignCss = formatInfo.Alignment switch
            {
                1 => "text-align: center;",
                2 => "text-align: right;",
                3 => "text-align: justify;",
                _ => "text-align: left;"
            };

            // 1. Xử lý đoạn văn thường <p class="MsoNormal"...> hoặc <p...>
            // Biểu thức chính quy bắt từng thẻ mở <p ...>
            var pRegex = new Regex(@"<p(\s+[^>]*)?>", RegexOptions.IgnoreCase);
            string processed = pRegex.Replace(sb.ToString(), match =>
            {
                string attrs = match.Groups[1].Value;

                // Bỏ qua thẻ p nếu là container chứa ảnh công thức block hoặc bảng
                if (attrs.Contains("data-xpm-math-type=\"block\"") || attrs.Contains("latex-math-block"))
                {
                    return match.Value;
                }

                // Không áp dụng first-line indent nếu đoạn văn đã là MsoListParagraph
                bool isListPara = attrs.Contains("MsoListParagraph", StringComparison.OrdinalIgnoreCase);

                var stylesToAdd = new List<string>();

                // Căn lề văn bản (Justify hoặc Left/Center/Right)
                if (!attrs.Contains("text-align", StringComparison.OrdinalIgnoreCase))
                {
                    stylesToAdd.Add(alignCss);
                }

                // Thụt lề đầu dòng cho đoạn văn bản thường (nếu Word có cấu hình FirstLineIndent > 0)
                if (!isListPara && formatInfo.FirstLineIndent > 0 && !attrs.Contains("text-indent", StringComparison.OrdinalIgnoreCase))
                {
                    stylesToAdd.Add($"text-indent: {formatInfo.FirstLineIndent:0.##}pt;");
                }

                // Thụt lề trái (Left Indent)
                if (!isListPara && formatInfo.LeftIndent > 0 && !attrs.Contains("margin-left", StringComparison.OrdinalIgnoreCase))
                {
                    stylesToAdd.Add($"margin-left: {formatInfo.LeftIndent:0.##}pt;");
                }

                if (stylesToAdd.Count == 0) return match.Value;

                string injectedStyles = string.Join(" ", stylesToAdd);

                if (attrs.Contains("style=\"", StringComparison.OrdinalIgnoreCase))
                {
                    return Regex.Replace(match.Value, @"style=""([^""]*)""", m =>
                    {
                        string currentStyle = m.Groups[1].Value.Trim().TrimEnd(';');
                        return $"style=\"{currentStyle}; {injectedStyles}\"";
                    }, RegexOptions.IgnoreCase);
                }
                else
                {
                    return $"<p style=\"{injectedStyles}\"{attrs}>";
                }
            });

            // 2. Xử lý danh sách (Lists: <ul>, <ol>, <li>)
            // Nếu con trỏ Word đang nằm trong danh sách (IsInList == true):
            // Chuyển đổi các thẻ <li> thành các đoạn MsoListParagraph đồng bộ ký tự bullet/dash của Word
            if (formatInfo.IsInList)
            {
                string bulletChar = !string.IsNullOrEmpty(formatInfo.ListBulletChar) ? formatInfo.ListBulletChar : "·";
                float listLeftIndent = formatInfo.LeftIndent > 0 ? formatInfo.LeftIndent : 36.0f;
                string fontStyle = !string.IsNullOrEmpty(formatInfo.FontName) ? $"font-family: '{formatInfo.FontName}';" : "";
                string sizeStyle = formatInfo.FontSize > 0 ? $"font-size: {formatInfo.FontSize:0.#}pt;" : "";
                string lineSpacingStyle = $"line-height: {formatInfo.LineSpacing:0.##};";

                // Thay thế <ul><li>...</li></ul> bằng chuỗi MsoListParagraph mượt mà
                var liRegex = new Regex(@"<li>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                processed = liRegex.Replace(processed, m =>
                {
                    string content = m.Groups[1].Value.Trim();
                    // Loại bỏ thẻ <p> bọc lồng bên trong nếu có
                    content = Regex.Replace(content, @"^<p[^>]*>|</p>$", "", RegexOptions.IgnoreCase);

                    return $"<p class=\"MsoListParagraph\" style=\"margin-left: {listLeftIndent:0.#}pt; text-indent: -18.0pt; {fontStyle} {sizeStyle} {lineSpacingStyle} {alignCss}\">" +
                           $"<span style=\"font-family: Symbol;\">{bulletChar}</span><span style=\"font-size: 7.0pt;\">&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</span>{content}</p>";
                });

                // Gỡ bỏ thẻ <ul>, </ul>, <ol>, </ol> bao ngoài đã được chuyển thành MsoListParagraph
                processed = Regex.Replace(processed, @"<\/?(ul|ol)[^>]*>", "", RegexOptions.IgnoreCase);
            }
            else
            {
                // Nếu không trong danh sách Word, đảm bảo các thẻ <li> được căn lề đều (justify) nếu tài liệu đang dùng justify
                if (formatInfo.Alignment == 3)
                {
                    processed = Regex.Replace(processed, @"<li(\s+[^>]*)?>", m =>
                    {
                        string attrs = m.Groups[1].Value;
                        if (!attrs.Contains("text-align", StringComparison.OrdinalIgnoreCase))
                        {
                            if (attrs.Contains("style=\"", StringComparison.OrdinalIgnoreCase))
                            {
                                return Regex.Replace(m.Value, @"style=""([^""]*)""", sm =>
                                {
                                    string s = sm.Groups[1].Value.Trim().TrimEnd(';');
                                    return $"style=\"{s}; text-align: justify;\"";
                                }, RegexOptions.IgnoreCase);
                            }
                            else
                            {
                                return $"<li style=\"text-align: justify;\"{attrs}>";
                            }
                        }
                        return m.Value;
                    }, RegexOptions.IgnoreCase);
                }
            }

            // 3. Đảm bảo các thẻ Tiêu đề <h1-6> KHÔNG bị dính text-indent
            processed = Regex.Replace(processed, @"<h([1-6])(\s+[^>]*)?>", m =>
            {
                string tagNum = m.Groups[1].Value;
                string attrs = m.Groups[2].Value;
                if (attrs.Contains("style=\"", StringComparison.OrdinalIgnoreCase))
                {
                    // Đảm bảo có text-indent: 0pt nếu chưa có
                    if (!attrs.Contains("text-indent", StringComparison.OrdinalIgnoreCase))
                    {
                        return Regex.Replace(m.Value, @"style=""([^""]*)""", sm =>
                        {
                            string s = sm.Groups[1].Value.Trim().TrimEnd(';');
                            return $"style=\"{s}; text-indent: 0pt;\"";
                        }, RegexOptions.IgnoreCase);
                    }
                }
                return m.Value;
            }, RegexOptions.IgnoreCase);

            return processed;
        }
    }
}
