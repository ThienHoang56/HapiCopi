using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace HapiCopiLite
{
    public static class AutoFormatter
    {
        private static readonly MarkdownPipeline MarkdownPipe = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        /// <summary>
        /// Nhận diện xem văn bản có phải Markdown không (chứa bảng biểu, tiêu đề, in đậm, danh sách, code block).
        /// </summary>
        public static bool IsMarkdown(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 4) return false;

            bool hasHeading = Regex.IsMatch(text, @"(?m)^#{1,6}\s+\S+");
            bool hasTable = Regex.IsMatch(text, @"(?m)\|[^\r\n]+\|[^\r\n]*\r?\n\|[-:\s|]+\|");
            bool hasCodeBlock = text.Contains("```");
            bool hasBold = Regex.IsMatch(text, @"\*\*[^*\r\n]+\*\*");
            bool hasList = Regex.IsMatch(text, @"(?m)^(\*|-|\d+\.)\s+\S+");

            return hasHeading || hasTable || hasCodeBlock || (hasBold && hasList);
        }

        /// <summary>
        /// Chuyển đổi Markdown sang định dạng HTML tương thích 100% với Microsoft Word và Google Docs.
        /// Tự động chèn CSS bảng kẻ ô sang trọng và đóng gói theo chuẩn Windows CF_HTML.
        /// </summary>
        public static string ConvertToOfficeHtml(string markdown)
        {
            string htmlBody = Markdown.ToHtml(markdown, MarkdownPipe);

            // CSS tinh chỉnh đặc biệt để khi dán vào Word/Docs sẽ hiển thị bảng kẻ ô đẹp, typography chuẩn
            string styledHtml =
                "<!-- CSS Styling for Office & Docs -->\r\n" +
                "<style>\r\n" +
                "  body { font-family: 'Segoe UI', Calibri, Arial, sans-serif; font-size: 11pt; color: #1e293b; line-height: 1.5; }\r\n" +
                "  table { border-collapse: collapse; width: 100%; margin: 10px 0; font-size: 10.5pt; }\r\n" +
                "  th { background-color: #f1f5f9; color: #0f172a; font-weight: bold; padding: 8px 12px; border: 1px solid #cbd5e1; text-align: left; }\r\n" +
                "  td { padding: 6px 12px; border: 1px solid #e2e8f0; }\r\n" +
                "  tr:nth-child(even) td { background-color: #f8fafc; }\r\n" +
                "  code { background-color: #f1f5f9; padding: 2px 6px; border-radius: 4px; font-family: Consolas, monospace; font-size: 9.5pt; color: #9333ea; }\r\n" +
                "  pre { background-color: #0f172a; color: #f8fafc; padding: 12px; border-radius: 6px; font-family: Consolas, monospace; }\r\n" +
                "  blockquote { border-left: 4px solid #3b82f6; margin: 8px 0; padding-left: 12px; color: #64748b; font-style: italic; }\r\n" +
                "  h1, h2, h3 { color: #0f172a; font-weight: 600; margin-top: 14px; margin-bottom: 6px; }\r\n" +
                "</style>\r\n" +
                htmlBody;

            return PackageAsClipboardHtml(styledHtml);
        }

        /// <summary>
        /// Đóng gói đoạn mã HTML thành cấu trúc Clipboard Format HTML (CF_HTML) chuẩn của Windows
        /// với các trường offset byte tính toán chính xác (StartHTML, EndHTML, StartFragment, EndFragment).
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

            byte[] dummyHeader = Encoding.UTF8.GetBytes(string.Format(HeaderTemplate, 0, 0, 0, 0));
            byte[] startHtmlBytes = Encoding.UTF8.GetBytes(StartHtml);
            byte[] fragmentBytes = Encoding.UTF8.GetBytes(htmlFragment);
            byte[] endHtmlBytes = Encoding.UTF8.GetBytes(EndHtml);

            int startHtmlOffset = dummyHeader.Length;
            int startFragmentOffset = startHtmlOffset + startHtmlBytes.Length;
            int endFragmentOffset = startFragmentOffset + fragmentBytes.Length;
            int endHtmlOffset = endFragmentOffset + endHtmlBytes.Length;

            string finalHeader = string.Format(HeaderTemplate, startHtmlOffset, endHtmlOffset, startFragmentOffset, endFragmentOffset);
            return finalHeader + StartHtml + htmlFragment + EndHtml;
        }

        /// <summary>
        /// Nhận diện xem văn bản có phải được copy từ file PDF (dòng ngắn bị chặt vụn do ngắt cứng).
        /// </summary>
        public static bool LooksLikePdfText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 100) return false;
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3) return false;

            // Nếu nhiều dòng ngắn kết thúc lơ lửng không có dấu chấm/phẩy thì đích thị là PDF
            int brokenLines = 0;
            foreach (var l in lines)
            {
                string t = l.Trim();
                if (t.Length > 20 && !Regex.IsMatch(t, @"[\.\?!:;]$"))
                {
                    brokenLines++;
                }
            }
            return (double)brokenLines / lines.Length > 0.6;
        }

        /// <summary>
        /// Nối lại các dòng bị ngắt gãy của PDF, sửa ký tự dính (ligatures) và lọc bỏ rác số trang.
        /// </summary>
        public static string HealPdfText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 1. Sửa ký tự dính phông phổ biến từ PDF
            string clean = text
                .Replace("ﬁ", "fi")
                .Replace("ﬂ", "fl")
                .Replace("ﬀ", "ff")
                .Replace("ﬃ", "ffi")
                .Replace("ﬄ", "ffl");

            var rawLines = clean.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new StringBuilder();
            bool isFirstInPara = true;

            foreach (var line in rawLines)
            {
                string trimmed = line.Trim();

                // Lọc số trang rác: "Page 1 of 5", "- 12 -", hoặc số đơn lẻ
                if (Regex.IsMatch(trimmed, @"^(?i)(page\s+\d+(\s+of\s+\d+)?|[-–—]?\s*\d+\s*[-–—]?)$"))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(trimmed))
                {
                    // Hàng trống: Kết thúc một đoạn văn
                    result.AppendLine();
                    result.AppendLine();
                    isFirstInPara = true;
                    continue;
                }

                if (isFirstInPara)
                {
                    result.Append(trimmed);
                    isFirstInPara = false;
                }
                else
                {
                    // Nối tiếp dòng vào đoạn văn hiện tại với 1 dấu cách
                    result.Append(" " + trimmed);
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Chuyển đổi mã bảng HTML sang cú pháp LaTeX chuẩn khi dán vào Overleaf / TeXmaker.
        /// </summary>
        public static string ConvertHtmlTableToLatex(string html)
        {
            var tableRegex = new Regex(@"<table[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return tableRegex.Replace(html, m =>
            {
                var content = m.Groups[1].Value;
                var sb = new StringBuilder();
                sb.AppendLine("\\begin{table}[h]");
                sb.AppendLine("\\centering");

                var trRegex = new Regex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var rows = trRegex.Matches(content);
                if (rows.Count == 0) return m.Value;

                var firstRowCells = Regex.Matches(rows[0].Value, @"<(?:td|th)[^>]*>(.*?)</(?:td|th)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                int cols = Math.Max(1, firstRowCells.Count);

                sb.AppendLine($"\\begin{{tabular}}{{|{string.Join("|", new string('c', cols).ToCharArray())}|}}");
                sb.AppendLine("\\hline");

                foreach (Match r in rows)
                {
                    var cells = Regex.Matches(r.Value, @"<(?:td|th)[^>]*>(.*?)</(?:td|th)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    var cellTexts = new List<string>();
                    foreach (Match c in cells)
                    {
                        string cellClean = Regex.Replace(c.Groups[1].Value, @"<[^>]+>", "").Trim();
                        cellTexts.Add(cellClean);
                    }
                    sb.AppendLine(string.Join(" & ", cellTexts) + " \\\\ \\hline");
                }

                sb.AppendLine("\\end{tabular}");
                sb.AppendLine("\\end{table}");
                return sb.ToString();
            });
        }
    }
}
