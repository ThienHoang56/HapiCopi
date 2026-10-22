using System;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    /// <summary>
    /// Nhận diện nguồn clipboard: Word, Google Docs, PDF… để quyết định đường dọn,
    /// không nhầm Word HTML với Markdown/AI.
    /// </summary>
    public static class ClipboardSource
    {
        public static bool IsOfficeHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return false;
            return html.IndexOf("microsoft word", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("xmlns:o=\"urn:schemas-microsoft-com:office:office\"", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("xmlns:w=\"urn:schemas-microsoft-com:office:word\"", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("class=Mso", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("class=\"Mso", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsGoogleDocsHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return false;
            return html.IndexOf("id=\"docs-internal-guid", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("id=docs-internal-guid", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("docs-internal-guid", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsOfficeOrDocsHtml(string html) =>
            IsOfficeHtml(html) || IsGoogleDocsHtml(html);

        /// <summary>
        /// Gỡ comment có điều kiện và thẻ o:/v:/w: trước khi đưa vào DOMParser.
        /// Word nhét VML + <o:p> làm hỏng cấu trúc nếu để nguyên.
        /// </summary>
        public static string PrepareOfficeFragment(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            string cleaned = Regex.Replace(html, @"<!--\[if[\s\S]*?<!\[endif\]-->", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<o:p[^>]*>[\s\S]*?</o:p>", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"</?o:[A-Za-z0-9]+[^>]*>", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"</?v:[A-Za-z0-9]+[^>]*>", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"</?w:[A-Za-z0-9]+[^>]*>", "", RegexOptions.IgnoreCase);
            return cleaned;
        }
    }
}
