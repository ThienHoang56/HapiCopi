using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Markdig;

namespace HapiCopi
{
    public static class MarkdownToWordConverter
    {
        private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        /// <summary>
        /// Chuyển đổi Markdown thô sang HTML có định dạng cấu trúc chuẩn cho MS Word/Google Docs (Hỗ trợ Async & WebView2)
        /// </summary>
        public static async Task<string> ConvertToOfficeHtmlAsync(string markdownText, WordFormatInfo formatInfo)
        {
            if (WebViewManager.IsInitialized)
            {
                try
                {
                    Program.Log("[Converter] Sử dụng WebView2 (Chromium ngầm) để chuyển đổi Markdown...");
                    
                    // 1. Gọi JS để chuyển đổi Markdown + LaTeX sang HTML hoàn chỉnh
                    string htmlFragment = await WebViewManager.ConvertMarkdownToHtmlAsync(markdownText, formatInfo.IsWordTarget);
                    
                    // 2. Gọi JS làm sạch HTML đồng thời đồng hóa Font/Size
                    string cleanFragment = await WebViewManager.CleanHtmlAsync(htmlFragment, formatInfo.FontName, formatInfo.FontSize);
                    
                    // 3. Đóng gói chuẩn CF_HTML của Windows
                    return PackageAsClipboardHtml(cleanFragment);
                }
                catch (Exception ex)
                {
                    Program.Log($"[Converter LỖI] Lỗi WebView2 (Sẽ lùi về Fallback C#): {ex.Message}");
                }
            }

            // --- BỘ PHẦN CỨNG FALLBACK C# THUẦN (OFFLINE DỰ PHÒNG) ---
            Program.Log("[Converter Fallback] Sử dụng bộ parser C# offline nội bộ...");

            // 1. Dịch chuyển các công thức LaTeX toán học sang HTML bằng C#
            string textWithMath = LatexParser.ProcessLatexInHtml(markdownText, formatInfo);

            // 2. Sử dụng Markdig để parse Markdown sang HTML chuẩn
            string fallbackHtml = Markdown.ToHtml(textWithMath, MarkdownPipeline);

            // 3. Đồng hóa Font và Size cho văn bản thường (Paragraphs)
            string pStyle = "";
            if (formatInfo.IsFromActiveWord)
            {
                pStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
            }
            string pReplacement = $"<p class=\"MsoNormal\"{pStyle}>";
            fallbackHtml = fallbackHtml.Replace("<p>", pReplacement);

            // 4. Tịnh tiến cấp độ Heading dựa trên Heading cha trong tài liệu Word
            fallbackHtml = AdjustHeadings(fallbackHtml, formatInfo);

            // 5. Đồng hóa kiểu chữ cho Lists (ul, ol, li)
            string listStyle = "";
            string liStyle = "";
            if (formatInfo.IsFromActiveWord)
            {
                listStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
                liStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
            }
            
            fallbackHtml = fallbackHtml.Replace("<ul>", $"<ul{listStyle}>");
            fallbackHtml = fallbackHtml.Replace("<ol>", $"<ol{listStyle}>");
            fallbackHtml = fallbackHtml.Replace("<li>", $"<li{liStyle}>");

            // 6. Xử lý Code Blocks và Inline Code
            var preRegex = new Regex(@"<pre><code(?:[^>]*)>(.*?)</code></pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            fallbackHtml = preRegex.Replace(fallbackHtml, match =>
            {
                string codeContent = match.Groups[1].Value;
                string decodedCode = System.Net.WebUtility.HtmlDecode(codeContent).Trim();
                
                if (decodedCode.StartsWith("graph ", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("gantt", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("pie", StringComparison.OrdinalIgnoreCase) ||
                    decodedCode.StartsWith("gitGraph", StringComparison.OrdinalIgnoreCase))
                {
                    if (formatInfo.MermaidImageCache != null)
                    {
                        foreach (var kv in formatInfo.MermaidImageCache)
                        {
                            if (kv.Key.Contains(decodedCode) || decodedCode.Contains(kv.Key))
                            {
                                if (!string.IsNullOrEmpty(kv.Value))
                                {
                                    return $"<p><img src=\"{kv.Value}\" alt=\"Mermaid Diagram\" style=\"max-width: 100%; height: auto;\" /></p>";
                                }
                            }
                        }
                    }
                }
                
                return $"<div class=\"MsoNormal\" style=\"font-family: 'Consolas', 'Courier New', monospace; font-size: 10pt; background-color: #f8f9fa; border: 1px solid #e9ecef; padding: 8pt; margin: 6pt 0; white-space: pre; line-height: 1.2; color: #212529;\">{codeContent}</div>";
            });

            fallbackHtml = fallbackHtml.Replace("<code>", "<code style=\"font-family: 'Consolas', 'Courier New', monospace; font-size: 10pt; background-color: #f8f9fa; border: 1px solid #e9ecef; padding: 2px 4px; border-radius: 3px; color: #e83e8c;\">");
            
            // 7. Định dạng Bảng biểu
            fallbackHtml = fallbackHtml.Replace("<table>", "<table class=\"MsoNormalTable\" style=\"border-collapse: collapse; width: 100%; border: 1px solid #cccccc; margin: 6pt 0;\">");
            
            var thRegex = new Regex(@"<th([^>]*)>", RegexOptions.IgnoreCase);
            fallbackHtml = thRegex.Replace(fallbackHtml, m => HtmlCleaner.FormatTableCell("th", m.Groups[1].Value, "border: 1px solid #cccccc; padding: 6px; background-color: #f2f2f2; font-weight: bold; text-align: left;"));
            
            var tdRegex = new Regex(@"<td([^>]*)>", RegexOptions.IgnoreCase);
            fallbackHtml = tdRegex.Replace(fallbackHtml, m => HtmlCleaner.FormatTableCell("td", m.Groups[1].Value, "border: 1px solid #cccccc; padding: 6px; text-align: left;"));

            fallbackHtml = fallbackHtml.Replace("<blockquote>", "<blockquote style=\"border-left: 3px solid #cccccc; padding-left: 10pt; margin: 6pt 0 6pt 12pt; color: #555555; font-style: italic;\">");
            fallbackHtml = Regex.Replace(fallbackHtml, @"<a\s+([^>]*href=""([^""]*)""[^>]*)>(.*?)</a>", @"<a $1 style=""color: #0563c1; text-decoration: underline;"">$3</a>");

            return PackageAsClipboardHtml(fallbackHtml);
        }

        /// <summary>
        /// Làm sạch HTML thô từ trình duyệt web AI (Hỗ trợ Async & WebView2)
        /// </summary>
        public static async Task<string> CleanWebHtmlToOfficeAsync(string htmlFragment, WordFormatInfo formatInfo)
        {
            if (WebViewManager.IsInitialized)
            {
                try
                {
                    Program.Log("[Converter] Sử dụng WebView2 (DOM Parser) để làm sạch HTML web...");
                    return await WebViewManager.CleanHtmlAsync(htmlFragment, formatInfo.FontName, formatInfo.FontSize);
                }
                catch (Exception ex)
                {
                    Program.Log($"[Converter LỖI] Lỗi dọn rác WebView2 (Sẽ lùi về Fallback C#): {ex.Message}");
                }
            }

            // Fallback C#
            return HtmlCleaner.CleanWebHtmlToOffice(htmlFragment, formatInfo);
        }

        private static string AdjustHeadings(string html, WordFormatInfo formatInfo)
        {
            int parentLevel = formatInfo.ParentHeadingLevel;
            var headingRegex = new Regex(@"<h([1-6])([^>]*)>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return headingRegex.Replace(html, match =>
            {
                int markdownLevel = int.Parse(match.Groups[1].Value);
                string content = match.Groups[3].Value;

                int targetLevel = Math.Min(9, parentLevel + markdownLevel);
                string msoClass = $"MsoHeading{targetLevel}";
                string headingStyle = "";
                if (formatInfo.IsFromActiveWord)
                {
                    headingStyle = $" style=\"font-family: '{formatInfo.FontName}';\"";
                }

                return $"<h{targetLevel} class=\"{msoClass}\"{headingStyle}>{content}</h{targetLevel}>";
            });
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

        // Delegated helpers to keep backwards compatibility if called elsewhere
        public static string CleanWebHtmlToOffice(string htmlFragment, WordFormatInfo formatInfo)
        {
            return HtmlCleaner.CleanWebHtmlToOffice(htmlFragment, formatInfo);
        }

        public static string ProcessLatexInPlainText(string text)
        {
            return LatexParser.ProcessLatexInPlainText(text);
        }
    }
}
