using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HapiCopi
{
    public static class MarkdownToWordConverter
    {
        /// <summary>
        /// Chuyển đổi Markdown thô sang HTML có định dạng cấu trúc chuẩn cho MS Word/Google Docs (Sử dụng WebView2)
        /// </summary>
        public static async Task<string> ConvertToOfficeHtmlAsync(string markdownText, WordFormatInfo formatInfo)
        {
            try
            {
                Program.Log("[Converter] Đợi WebView2 khởi tạo...");
                await WebViewManager.InitializationTask;

                Program.Log("[Converter] Sử dụng WebView2 (Chromium ngầm) để chuyển đổi Markdown...");
                
                string processedMarkdown = markdownText;
                // Chỉ tự bọc fence khi văn bản CHƯA có khối ``` (tránh bọc lồng gây hỏng cú pháp và trượt cache).
                if (!markdownText.Contains("```"))
                {
                    if (MermaidRenderer.IsRawMermaidCode(markdownText))
                    {
                        processedMarkdown = $"```mermaid\r\n{markdownText}\r\n```";
                        Program.Log("[Converter] Bọc mã Mermaid thô vào khối code block.");
                    }
                    else
                    {
                        string detectedLang = KrokiRenderer.DetectRawDiagramLanguage(markdownText);
                        if (detectedLang != null)
                        {
                            processedMarkdown = $"```{detectedLang}\r\n{markdownText}\r\n```";
                            Program.Log($"[Converter] Bọc mã {detectedLang} thô vào khối code block.");
                        }
                    }
                }

                // 1. Gọi JS để chuyển đổi Markdown + LaTeX sang HTML hoàn chỉnh
                string htmlFragment = await WebViewManager.ConvertMarkdownToHtmlAsync(processedMarkdown, formatInfo.IsWordTarget);
                
                // 2. Gọi JS làm sạch HTML đồng thời đồng hóa Font/Size và tịnh tiến Heading
                string cacheJson = System.Text.Json.JsonSerializer.Serialize(formatInfo.MermaidImageCache);

                string cleanFragment = await WebViewManager.CleanHtmlAsync(htmlFragment, formatInfo.FontName, formatInfo.FontSize, formatInfo.LineSpacing, formatInfo.ParentHeadingLevel, formatInfo.IsWordTarget, cacheJson);
                
                // 3. Đóng gói chuẩn CF_HTML của Windows
                return PackageAsClipboardHtml(cleanFragment);
            }
            catch (Exception ex)
            {
                Program.Log($"[Converter LỖI] Lỗi WebView2: {ex.Message}");
                return PackageAsClipboardHtml($"<p>Lỗi chuyển đổi Markdown: {ex.Message}</p>");
            }
        }

        /// <summary>
        /// Làm sạch HTML thô từ trình duyệt web AI (Sử dụng WebView2)
        /// </summary>
        public static async Task<string> CleanWebHtmlToOfficeAsync(string htmlFragment, WordFormatInfo formatInfo)
        {
            try
            {
                Program.Log("[Converter] Đợi WebView2 khởi tạo để làm sạch HTML...");
                await WebViewManager.InitializationTask;

                Program.Log("[Converter] Sử dụng WebView2 (DOM Parser) để làm sạch HTML web...");
                Program.Log($"[DEBUG] CleanWebHtmlToOfficeAsync HTML length: {htmlFragment.Length}");
                Program.Log($"[DEBUG] HTML contains 'katex': {htmlFragment.Contains("katex")}");
                Program.Log($"[DEBUG] HTML contains 'mathjax': {htmlFragment.Contains("mathjax", StringComparison.OrdinalIgnoreCase)}");
                Program.Log($"[DEBUG] HTML contains 'mjx': {htmlFragment.Contains("mjx", StringComparison.OrdinalIgnoreCase)}");
                Program.Log($"[DEBUG] HTML contains 'annotation': {htmlFragment.Contains("annotation")}");
                
                try
                {
                    string diagPath = Path.Combine(ResourcePaths.LogDirectory, "diagnostic_clipboard.html");
                    Directory.CreateDirectory(ResourcePaths.LogDirectory);
                    File.WriteAllText(diagPath, htmlFragment, Encoding.UTF8);
                    Program.Log($"[DEBUG] Da ghi HTML fragment ra: {diagPath}");
                }
                catch (Exception ex)
                {
                    Program.Log($"[DEBUG LỖI] Khong the ghi file chan doan: {ex.Message}");
                }
                
                htmlFragment = ClipboardSource.PrepareOfficeFragment(htmlFragment);

                string cacheJson = System.Text.Json.JsonSerializer.Serialize(formatInfo.MermaidImageCache);
                return await WebViewManager.CleanHtmlAsync(htmlFragment, formatInfo.FontName, formatInfo.FontSize, formatInfo.LineSpacing, formatInfo.ParentHeadingLevel, formatInfo.IsWordTarget, cacheJson);
            }
            catch (Exception ex)
            {
                Program.Log($"[Converter LỖI] Lỗi dọn rác WebView2: {ex.Message}");
                return htmlFragment;
            }
        }

        /// <summary>
        /// Trích xuất đoạn HTML fragment thực tế nằm giữa thẻ comment StartFragment và EndFragment
        /// </summary>
        public static string ExtractHtmlFragment(string cfHtml)
        {
            const string startToken = "<!--StartFragment-->";
            const string endToken = "<!--EndFragment-->";
            
            int startIndex = cfHtml.IndexOf(startToken);
            int endIndex = cfHtml.IndexOf(endToken);
            
            if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
            {
                startIndex += startToken.Length;
                return cfHtml.Substring(startIndex, endIndex - startIndex);
            }
            return cfHtml;
        }

        /// <summary>
        /// Bọc HTML fragment theo đúng chuẩn cấu trúc CF_HTML (Clipboard Format HTML) của Windows
        /// </summary>
        public static string PackageAsClipboardHtml(string htmlFragment)
        {
            const string HeaderTemplate =
                "Version:0.9\r\n" +
                "StartHTML:{0:D10}\r\n" +
                "EndHTML:{1:D10}\r\n" +
                "StartFragment:{2:D10}\r\n" +
                "EndFragment:{3:D10}\r\n";

            const string StartHtml = "<html>\r\n<head>\r\n<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">\r\n</head>\r\n<body>\r\n<!--StartFragment-->";
            const string EndHtml = "<!--EndFragment-->\r\n</body>\r\n</html>";

            byte[] headerBytes = Encoding.UTF8.GetBytes(string.Format(HeaderTemplate, 0, 0, 0, 0));
            byte[] startHtmlBytes = Encoding.UTF8.GetBytes(StartHtml);
            byte[] fragmentBytes = Encoding.UTF8.GetBytes(htmlFragment);
            byte[] endHtmlBytes = Encoding.UTF8.GetBytes(EndHtml);

            int startHtmlOffset = headerBytes.Length;
            int startFragmentOffset = startHtmlOffset + startHtmlBytes.Length;
            int endFragmentOffset = startFragmentOffset + fragmentBytes.Length;
            int endHtmlOffset = endFragmentOffset + endHtmlBytes.Length;

            string finalHeader = string.Format(HeaderTemplate, startHtmlOffset, endHtmlOffset, startFragmentOffset, endFragmentOffset);

            return finalHeader + StartHtml + htmlFragment + EndHtml;
        }

        /// <summary>
        /// Chuyển đổi bảng biểu HTML sang cấu trúc mã nguồn LaTeX (\begin{table}...)
        /// </summary>
        public static string ConvertHtmlTableToLatex(string html)
        {
            var tableRegex = new Regex(@"<table[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return tableRegex.Replace(html, match =>
            {
                var content = match.Groups[1].Value;
                var sb = new StringBuilder();
                sb.AppendLine("\\begin{table}[h]");
                sb.AppendLine("\\centering");

                var trRegex = new Regex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var rows = trRegex.Matches(content);
                if (rows.Count == 0) return match.Value;

                var cellRegex = new Regex(@"<(?:td|th)[^>]*>(.*?)</(?:td|th)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var firstRowCells = cellRegex.Matches(rows[0].Value);
                int colCount = firstRowCells.Count;

                string colFormat = "|" + string.Join("|", new string('c', colCount).ToCharArray()) + "|";
                sb.AppendLine($"\\begin{{tabular}}{{{colFormat}}}");
                sb.AppendLine("\\hline");

                foreach (Match row in rows)
                {
                    var cells = cellRegex.Matches(row.Value);
                    var cellList = new System.Collections.Generic.List<string>();
                    foreach (Match cell in cells)
                    {
                        string text = Regex.Replace(cell.Groups[1].Value, @"<[^>]*>", "").Trim();
                        text = text.Replace("&", "\\&").Replace("%", "\\%").Replace("$", "\\$").Replace("_", "\\_");
                        cellList.Add(text);
                    }
                    if (cellList.Count > 0)
                    {
                        sb.AppendLine(string.Join(" & ", cellList) + " \\\\");
                        sb.AppendLine("\\hline");
                    }
                }

                sb.AppendLine("\\end{tabular}");
                sb.AppendLine("\\caption{Bảng số liệu}");
                sb.AppendLine("\\end{table}");
                return sb.ToString();
            });
        }

        /// <summary>
        /// Dịch LaTeX thô trong Plain Text (Sử dụng WebView2)
        /// </summary>
        public static async Task<string> ProcessLatexInPlainTextAsync(string text)
        {
            try
            {
                await WebViewManager.InitializationTask;
                return await WebViewManager.ProcessLatexInPlainTextAsync(text);
            }
            catch (Exception ex)
            {
                Program.Log($"[Converter LỖI] Lỗi dịch LaTeX trong PlainText: {ex.Message}");
                return text;
            }
        }
    }
}
