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
    /// Dịch vụ Đũa phép Magic Wand (Rule Profile Mode).
    /// Quét toàn bộ "bộ gen định dạng" của tài liệu Word trên luồng ngầm bất đồng bộ (chuyển tab vẫn chạy tốt),
    /// nạp quy tắc vào bộ nhớ đệm (Rule Profile). Khi chế độ BẬT, mọi lần dán Ctrl+V bình thường đều tự động
    /// tuân theo quy tắc lề, thụt dòng, justify, bullet của tài liệu đó với độ trễ < 0.01 ms.
    /// Khi TẮT, HapiCopi trở về hoạt động bình thường.
    /// </summary>
    public static class SmartPasteService
    {
        public static bool IsMagicWandActive { get; private set; } = false;
        public static WordFormatInfo? ActiveDocumentRules { get; private set; } = null;
        public static string? ActiveDocumentTitle { get; private set; } = null;
        public static event Action<bool>? StateChanged;

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
        /// Bật hoặc Tắt chế độ Đũa phép (Ctrl + F9).
        /// Chạy ngầm bất đồng bộ: Dù người dùng chuyển tab ngay sau khi nhấn phím,
        /// tác vụ trích xuất quy tắc Word vẫn tiếp tục hoàn thành và báo hiệu thị giác êm ái.
        /// </summary>
        public static async Task ToggleMagicWandAsync(Action<string, string>? notifyAction = null)
        {
            if (IsMagicWandActive)
            {
                // 1. TẮT CHẾ ĐỘ
                IsMagicWandActive = false;
                ActiveDocumentRules = null;
                ActiveDocumentTitle = null;
                StateChanged?.Invoke(false);

                MagicWandIndicatorForm.NotifyDeactivated();
                notifyAction?.Invoke("Đã tắt Đũa phép Word (Ctrl+F9)", "Trở về chế độ dán HapiCopi thông thường.");
                Program.Log("[MagicWand] Người dùng đã tắt chế độ Magic Wand.");
                return;
            }

            // 2. BẬT CHẾ ĐỘ: Hiển thị ngay micro-indicator tại chuột ở trạng thái hấp thụ/quét quy tắc
            Point cursorPos = Cursor.Position;
            MagicWandIndicatorForm.ShowNearCursor(cursorPos);
            Program.Log($"[MagicWand] Bắt đầu quét tài liệu bất đồng bộ tại ({cursorPos.X}, {cursorPos.Y})...");

            var appInfo = ActiveWindowHelper.GetActiveAppInfo();
            ActiveDocumentTitle = appInfo.WindowTitle;

            // Luồng ngầm STA trích xuất quy tắc từ Word (Chuyển tab người dùng sang Chrome/ChatGPT vẫn chạy tốt)
            var rules = await WordInterop.ScanDocumentRulesAsync(3000);

            ActiveDocumentRules = rules;
            IsMagicWandActive = true;
            StateChanged?.Invoke(true);

            // Báo hiệu thành công cho widget chuột (nở thành tick xanh mềm mại rồi ẩn)
            MagicWandIndicatorForm.NotifySuccess();

            string alignName = rules.Alignment == 3 ? "Justify (Đều 2 bên)" : (rules.Alignment == 1 ? "Center" : "Left");
            string indentText = rules.FirstLineIndent > 0 ? $"{rules.FirstLineIndent:0.#}pt ({rules.FirstLineIndent / 28.35f:0.##}cm)" : "0pt";

            notifyAction?.Invoke("Đã nạp quy tắc Word!",
                $"• Font: {rules.FontName ?? "Times New Roman"} ({rules.FontSize:0.#}pt)\n" +
                $"• Thụt đầu dòng: {indentText}\n" +
                $"• Căn lề: {alignName}\n" +
                "Mọi lần nhấn Ctrl+V sẽ tự động đồng bộ theo tài liệu.");

            Program.Log($"[MagicWand] Đã nạp thành công quy tắc Word: Font={rules.FontName}, Size={rules.FontSize}, Indent={rules.FirstLineIndent}, Align={rules.Alignment}");
        }

        /// <summary>
        /// Xử lý định dạng nội dung Clipboard theo quy tắc tài liệu đã nạp sẵn.
        /// Chạy siêu tốc (< 0.01ms), không gọi COM Interop khi dán.
        /// </summary>
        public static async Task<bool> FormatClipboardWithActiveRulesAsync(
            string textToProcess,
            string htmlToProcess,
            Dictionary<string, string>? diagramCache)
        {
            if (!IsMagicWandActive || ActiveDocumentRules == null) return false;

            var formatInfo = ActiveDocumentRules;
            formatInfo.IsWordTarget = true;
            if (diagramCache != null) formatInfo.MermaidImageCache = diagramCache;

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
            else if (!string.IsNullOrWhiteSpace(textToProcess))
            {
                // Kiểm tra nếu là văn bản thường không có thẻ Markdown
                if (!textToProcess.Contains("#") && !textToProcess.Contains("```") && !textToProcess.Contains("$") && !textToProcess.Contains("*") && !textToProcess.Contains("- "))
                {
                    string smartFragment = ConvertPlainTextToSmartParagraphs(textToProcess, formatInfo);
                    officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(smartFragment);
                }
                else
                {
                    officeHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(textToProcess, formatInfo);
                    string fragment = MarkdownToWordConverter.ExtractHtmlFragment(officeHtml);
                    string smartFragment = ApplySmartWordStyles(fragment, formatInfo);
                    officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(smartFragment);
                }
            }
            else
            {
                return false;
            }

            string cleanPlainText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(textToProcess);
            FormulaTracker.ScanAndRegisterHtml(officeHtml);
            ClipboardHelper.SetOfficeClipboard(cleanPlainText, officeHtml);
            return true;
        }

        /// <summary>
        /// Chuyển đổi văn bản thô nhiều dòng thành các đoạn văn <p class="MsoNormal"> có thụt lề và justify chuẩn.
        /// </summary>
        public static string ConvertPlainTextToSmartParagraphs(string plainText, WordFormatInfo formatInfo)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return plainText;

            string[] lines = plainText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();

            string fontStyle = !string.IsNullOrEmpty(formatInfo.FontName) ? $"font-family: '{formatInfo.FontName}';" : "";
            string sizeStyle = formatInfo.FontSize > 0 ? $"font-size: {formatInfo.FontSize:0.#}pt;" : "";
            string lineSpacingStyle = $"line-height: {formatInfo.LineSpacing:0.##};";
            string alignStyle = formatInfo.Alignment switch
            {
                1 => "text-align: center;",
                2 => "text-align: right;",
                3 => "text-align: justify;",
                _ => "text-align: left;"
            };
            string indentStyle = formatInfo.FirstLineIndent > 0 ? $"text-indent: {formatInfo.FirstLineIndent:0.##}pt;" : "";

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Kiểm tra nếu dòng bắt đầu bằng ký tự bullet hoặc gạch đầu dòng
                if (line.StartsWith("- ") || line.StartsWith("– ") || line.StartsWith("* ") || line.StartsWith("+ ") || line.StartsWith("• "))
                {
                    string bulletChar = formatInfo.ListBulletChar.Length > 0 ? formatInfo.ListBulletChar : line.Substring(0, 1);
                    string content = line.Substring(2).Trim();
                    sb.Append($"<p class=\"MsoListParagraph\" style=\"margin-left: 36.0pt; text-indent: -18.0pt; {fontStyle} {sizeStyle} {lineSpacingStyle} {alignStyle}\">" +
                              $"<span style=\"font-family: Symbol;\">{bulletChar}</span><span style=\"font-size: 7.0pt;\">&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</span>{System.Net.WebUtility.HtmlEncode(content)}</p>");
                }
                else
                {
                    sb.Append($"<p class=\"MsoNormal\" style=\"{fontStyle} {sizeStyle} {lineSpacingStyle} {alignStyle} {indentStyle}\">{System.Net.WebUtility.HtmlEncode(line)}</p>");
                }
            }

            return sb.ToString();
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
            var pRegex = new Regex(@"<p(\s+[^>]*)?>", RegexOptions.IgnoreCase);
            string processed = pRegex.Replace(sb.ToString(), match =>
            {
                string attrs = match.Groups[1].Value;

                // Bỏ qua thẻ p nếu là container chứa ảnh công thức block hoặc bảng
                if (attrs.Contains("data-xpm-math-type=\"block\"") || attrs.Contains("latex-math-block"))
                {
                    return match.Value;
                }

                bool isListPara = attrs.Contains("MsoListParagraph", StringComparison.OrdinalIgnoreCase);
                var stylesToAdd = new List<string>();

                // Căn lề văn bản (Justify hoặc Left/Center/Right)
                if (!attrs.Contains("text-align", StringComparison.OrdinalIgnoreCase))
                {
                    stylesToAdd.Add(alignCss);
                }

                // Thụt lề đầu dòng cho đoạn văn bản thường
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
            if (formatInfo.IsInList)
            {
                string bulletChar = !string.IsNullOrEmpty(formatInfo.ListBulletChar) ? formatInfo.ListBulletChar : "–";
                float listLeftIndent = formatInfo.LeftIndent > 0 ? formatInfo.LeftIndent : 36.0f;
                string fontStyle = !string.IsNullOrEmpty(formatInfo.FontName) ? $"font-family: '{formatInfo.FontName}';" : "";
                string sizeStyle = formatInfo.FontSize > 0 ? $"font-size: {formatInfo.FontSize:0.#}pt;" : "";
                string lineSpacingStyle = $"line-height: {formatInfo.LineSpacing:0.##};";

                var liRegex = new Regex(@"<li>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                processed = liRegex.Replace(processed, m =>
                {
                    string content = m.Groups[1].Value.Trim();
                    content = Regex.Replace(content, @"^<p[^>]*>|</p>$", "", RegexOptions.IgnoreCase);

                    return $"<p class=\"MsoListParagraph\" style=\"margin-left: {listLeftIndent:0.#}pt; text-indent: -18.0pt; {fontStyle} {sizeStyle} {lineSpacingStyle} {alignCss}\">" +
                           $"<span style=\"font-family: Symbol;\">{bulletChar}</span><span style=\"font-size: 7.0pt;\">&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</span>{content}</p>";
                });

                processed = Regex.Replace(processed, @"<\/?(ul|ol)[^>]*>", "", RegexOptions.IgnoreCase);
            }
            else
            {
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
