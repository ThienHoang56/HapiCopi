using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Markdig;
using Markdig.Syntax;

namespace HapiCopi
{
    /// <summary>
    /// Bộ chuyển đổi Ngữ nghĩa Sạch dành riêng cho Google Docs (Google Docs Semantic Adapter).
    /// Nguyên lý cốt lõi:
    /// 1. HTML Ngữ nghĩa Sạch (Clean Semantic HTML):
    ///    - Thẻ h1..h6, p, ul, ol, li TUYỆT ĐỐI KHÔNG ép inline font-family hay font-size.
    ///    - Kích hoạt cơ chế "Named Styles" tự động của Google Docs để tài liệu tự áp dụng
    ///      Font, Cỡ chữ, Màu sắc chuẩn theo đúng thiết kế của người dùng.
    /// 2. Tịnh tiến cấp độ Đề mục (Heading Level Shifting):
    ///    - heading.Level mới = Math.Clamp(heading.Level + headingOffset, 1, 6).
    ///    - Giúp nội dung dán vào tự động trở thành mục con/cháu của phần người dùng đang viết.
    /// 3. Bảo toàn 100% Rich Text (Bold, Italic, Code, Links, Tables) và Công thức Toán (ảnh Vector/PNG).
    /// </summary>
    public static class GoogleDocsSemanticAdapter
    {
        private static readonly MarkdownPipeline DefaultPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        /// <summary>
        /// Chuyển đổi Markdown sang HTML ngữ nghĩa sạch cho Google Docs.
        /// Sử dụng Markdig AST engine kết hợp bộ lọc làm sạch để đảm bảo tương thích 100% cú pháp.
        /// </summary>
        public static string TransformMarkdownToGoogleDocsHtml(string markdownText, int headingOffset = 0)
        {
            if (string.IsNullOrWhiteSpace(markdownText)) return "";

            string rawHtml = Markdown.ToHtml(markdownText, DefaultPipeline);
            return CleanHtmlForGoogleDocs(rawHtml, headingOffset);
        }

        /// <summary>
        /// Làm sạch HTML đã render sẵn (từ WebView2 hoặc Web nguồn), loại bỏ inline font-family/size
        /// trên các thẻ cấu trúc và tịnh tiến cấp độ Heading cho Google Docs.
        /// </summary>
        public static string CleanHtmlForGoogleDocs(string htmlFragment, int headingOffset = 0)
        {
            if (string.IsNullOrWhiteSpace(htmlFragment)) return "";

            string processed = htmlFragment;

            // 1. Tịnh tiến cấp độ Heading và loại bỏ inline font-family/font-size trên Headings
            processed = Regex.Replace(processed, @"<h([1-6])(\s+[^>]*)?>(.*?)</h\1>", m =>
            {
                int originalLevel = int.Parse(m.Groups[1].Value);
                int targetLevel = Math.Clamp(originalLevel + headingOffset, 1, 6);
                string inner = m.Groups[3].Value;

                // Xóa bỏ các style ép font trong inner text của heading nếu có
                string cleanInner = StripFontOverrides(inner);

                return $"<h{targetLevel}>{cleanInner}</h{targetLevel}>";
            }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 2. Làm sạch thẻ <p>: Loại bỏ font-family và font-size ép buộc
            processed = Regex.Replace(processed, @"<p(\s+[^>]*)?>(.*?)</p>", m =>
            {
                string attrs = m.Groups[1].Value;
                string inner = m.Groups[2].Value;

                // Nếu là khối căn giữa (ảnh hoặc caption)
                if (attrs.Contains("text-align: center") || attrs.Contains("text-align:center"))
                {
                    return $"<p style=\"text-align: center;\">{inner}</p>";
                }

                // Nếu chứa ảnh công thức toán học thì giữ nguyên
                if (inner.Contains("<img") && (inner.Contains("latex-math") || inner.Contains("HapiCopi:Math")))
                {
                    return $"<p>{inner}</p>";
                }

                string cleanInner = StripFontOverrides(inner);
                return $"<p>{cleanInner}</p>";
            }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 2.1. Dọn dẹp thẻ <p> lồng bên trong <li> để tránh thụt lề kép trong Google Docs
            processed = Regex.Replace(processed, @"<li(\s+[^>]*)?>(.*?)</li>", liMatch =>
            {
                string content = liMatch.Groups[2].Value.Trim();
                content = Regex.Replace(content, @"^<p[^>]*>|</p>$", "", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"<p[^>]*>(.*?)</p>", "$1<br/>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                string cleanContent = StripFontOverrides(content);
                return $"<li>{cleanContent}</li>";
            }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 3. Tối ưu bảng biểu cho Google Docs: border nét mảnh xám nhạt, padding gọn gàng
            processed = Regex.Replace(processed, @"<table(\s+[^>]*)?>", "<table style=\"border-collapse: collapse; width: 100%; margin: 8pt 0; border: 1px solid #dadce0;\">", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"<th(\s+[^>]*)?>", "<th style=\"border: 1px solid #dadce0; padding: 6px 12px; background-color: #f8f9fa; font-weight: bold; text-align: left;\">", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"<td(\s+[^>]*)?>", "<td style=\"border: 1px solid #dadce0; padding: 6px 12px;\">", RegexOptions.IgnoreCase);

            // 4. Tối ưu khối mã nguồn (Fenced Code Block)
            processed = Regex.Replace(processed, @"<pre><code(\s+[^>]*)?>(.*?)</code></pre>", m =>
            {
                string code = m.Groups[2].Value;
                return "<div style=\"background-color: #f8f9fa; border: 1px solid #dadce0; border-radius: 4px; padding: 10px 14px; margin: 8pt 0;\">" +
                       $"<pre style=\"margin: 0; font-family: Consolas, 'Courier New', monospace; font-size: 10pt; line-height: 1.35; color: #202124;\"><code>{code}</code></pre>" +
                       "</div>";
            }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 5. Tối ưu mã nội dòng (Inline Code) nếu chưa được style
            processed = Regex.Replace(processed, @"(?<!<pre[^>]*>)<code>([^<]+)</code>", "<code style=\"font-family: Consolas, monospace; background-color: #f1f3f4; padding: 2px 4px; border-radius: 3px; font-size: 90%; color: #c5221f;\">$1</code>", RegexOptions.IgnoreCase);

            return processed;
        }

        private static string StripFontOverrides(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            // Loại bỏ thuộc tính style chứa font-family hoặc font-size
            string cleaned = Regex.Replace(html, @"style=""[^""]*(font-family|font-size)[^""]*""", "", RegexOptions.IgnoreCase);
            // Xóa thẻ span rỗng không còn thuộc tính: <span>text</span> -> text
            cleaned = Regex.Replace(cleaned, @"<span\s*>(.*?)</span>", "$1", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return cleaned;
        }

        /// <summary>
        /// Quy trình trọn gói xử lý Markdown sang Clipboard HTML hoàn chỉnh cho Google Docs.
        /// </summary>
        public static async Task<string> ConvertMarkdownToGoogleDocsClipboardAsync(string markdownText, int headingOffset = 0)
        {
            if (string.IsNullOrWhiteSpace(markdownText)) return "";

            bool hasMath = markdownText.Contains('$');
            string rawHtml;

            if (hasMath && WebViewManager.IsInitialized)
            {
                // Chuyển đổi qua WebView2 với isWordTarget = false để sinh ảnh công thức toán sắc nét cho Google Docs
                var formatInfo = new WordFormatInfo { IsWordTarget = false, IsFromActiveWord = false };
                string webHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(markdownText, formatInfo);
                string fragment = MarkdownToWordConverter.ExtractHtmlFragment(webHtml);
                rawHtml = CleanHtmlForGoogleDocs(fragment, headingOffset);
            }
            else
            {
                rawHtml = TransformMarkdownToGoogleDocsHtml(markdownText, headingOffset);
            }

            return MarkdownToWordConverter.PackageAsClipboardHtml(rawHtml);
        }
    }
}
