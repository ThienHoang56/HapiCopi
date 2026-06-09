using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace HapiCopi
{
    public static class MarkdownToWordConverter
    {
        private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        /// <summary>
        /// Chuyển đổi Markdown thô sang HTML có định dạng cấu trúc chuẩn cho MS Word/Google Docs
        /// </summary>
        public static string ConvertToOfficeHtml(string markdownText, WordFormatInfo formatInfo)
        {
            // 1. Dịch chuyển các công thức LaTeX toán học sang HTML trước khi truyền vào Markdig
            string textWithMath = LatexParser.ProcessLatexInHtml(markdownText, formatInfo);

            // 2. Sử dụng Markdig để parse Markdown sang HTML chuẩn
            string htmlFragment = Markdown.ToHtml(textWithMath, MarkdownPipeline);

            // 2. Đồng hóa Font và Size cho văn bản thường (Paragraphs)
            string pStyle = "";
            if (formatInfo.IsFromActiveWord)
            {
                pStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
            }
            string pReplacement = $"<p class=\"MsoNormal\"{pStyle}>";
            htmlFragment = htmlFragment.Replace("<p>", pReplacement);

            // 3. Tịnh tiến cấp độ Heading dựa trên Heading cha trong tài liệu Word
            htmlFragment = AdjustHeadings(htmlFragment, formatInfo);

            // 4. Đồng hóa kiểu chữ cho Lists (ul, ol, li)
            string listStyle = "";
            string liStyle = "";
            if (formatInfo.IsFromActiveWord)
            {
                listStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
                liStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
            }
            
            htmlFragment = htmlFragment.Replace("<ul>", $"<ul{listStyle}>");
            htmlFragment = htmlFragment.Replace("<ol>", $"<ol{listStyle}>");
            htmlFragment = htmlFragment.Replace("<li>", $"<li{liStyle}>");

            // 5. Xử lý Code Blocks và Inline Code
            var preRegex = new Regex(@"<pre><code(?:[^>]*)>(.*?)</code></pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            htmlFragment = preRegex.Replace(htmlFragment, match =>
            {
                string codeContent = match.Groups[1].Value;
                string decodedCode = System.Net.WebUtility.HtmlDecode(codeContent).Trim();
                
                // Kiểm tra xem đây có phải là biểu đồ Mermaid đã được cache ảnh không
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

            // 5.2. Định dạng mã nguồn nội dòng (Inline Code)
            htmlFragment = htmlFragment.Replace("<code>", "<code style=\"font-family: 'Consolas', 'Courier New', monospace; font-size: 10pt; background-color: #f8f9fa; border: 1px solid #e9ecef; padding: 2px 4px; border-radius: 3px; color: #e83e8c;\">");
            
            // 6. Định dạng Bảng biểu (Table Formatting)
            htmlFragment = htmlFragment.Replace("<table>", "<table class=\"MsoNormalTable\" style=\"border-collapse: collapse; width: 100%; border: 1px solid #cccccc; margin: 6pt 0;\">");
            
            var thRegex = new Regex(@"<th([^>]*)>", RegexOptions.IgnoreCase);
            htmlFragment = thRegex.Replace(htmlFragment, m => HtmlCleaner.FormatTableCell("th", m.Groups[1].Value, "border: 1px solid #cccccc; padding: 6px; background-color: #f2f2f2; font-weight: bold; text-align: left;"));
            
            var tdRegex = new Regex(@"<td([^>]*)>", RegexOptions.IgnoreCase);
            htmlFragment = tdRegex.Replace(htmlFragment, m => HtmlCleaner.FormatTableCell("td", m.Groups[1].Value, "border: 1px solid #cccccc; padding: 6px; text-align: left;"));

            // 7. Định dạng Blockquotes (Trích dẫn)
            htmlFragment = htmlFragment.Replace("<blockquote>", "<blockquote style=\"border-left: 3px solid #cccccc; padding-left: 10pt; margin: 6pt 0 6pt 12pt; color: #555555; font-style: italic;\">");

            // 8. Định dạng Hyperlinks
            htmlFragment = Regex.Replace(htmlFragment, @"<a\s+([^>]*href=""([^""]*)""[^>]*)>(.*?)</a>", @"<a $1 style=""color: #0563c1; text-decoration: underline;"">$3</a>");

            // 9. Bọc toàn bộ HTML vào định dạng CF_HTML của Windows
            return PackageAsClipboardHtml(htmlFragment);
        }

        private static string AdjustHeadings(string html, WordFormatInfo formatInfo)
        {
            int parentLevel = formatInfo.ParentHeadingLevel;

            // Tìm và thay thế các thẻ Heading từ <h1> đến <h6>
            var headingRegex = new Regex(@"<h([1-6])([^>]*)>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return headingRegex.Replace(html, match =>
            {
                int markdownLevel = int.Parse(match.Groups[1].Value);
                string content = match.Groups[3].Value;

                // Cấp độ Heading mới sau khi tịnh tiến
                int targetLevel = Math.Min(9, parentLevel + markdownLevel);

                // Áp dụng class MsoHeadingX đặc trưng của Word
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
