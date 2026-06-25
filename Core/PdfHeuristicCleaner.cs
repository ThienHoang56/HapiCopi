using System;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public static class PdfHeuristicCleaner
    {
        public static bool IsLikelyPdfText(string text, string html)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            
            // If it has complex HTML structure, it's likely not just raw PDF text
            if (!string.IsNullOrEmpty(html) && (html.Contains("<table") || html.Contains("<img") || html.Contains("class=")))
            {
                // But wait, Edge PDF viewer wraps lines in <div> or <p>. We can still clean it if it doesn't look like an AI table.
                // However, the safest is to check if text has suspicious line breaks.
            }

            // A typical signature of PDF text is having many \r\n that don't make sense.
            // Check if there's a lowercase letter followed by \r\n followed by a lowercase letter.
            return Regex.IsMatch(text, @"[a-z,]\r?\n[a-zđ]", RegexOptions.IgnoreCase);
        }

        public static string FixLineBreaks(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 1. Remove hyphens at the end of lines if they are word wraps (e.g. "inform-\r\nation" -> "information")
            text = Regex.Replace(text, @"([a-zđ]+)-\r?\n([a-zđ]+)", "$1$2", RegexOptions.IgnoreCase);

            // 2. Join lines if the first line ends with a lowercase letter, comma, or hyphen 
            // AND the next line starts with a lowercase letter or number.
            // Example: "Trong nhiều hệ thống thực tế,\r\ndữ liệu không phải..."
            text = Regex.Replace(text, @"([a-zđ,])\r?\n([a-zđ0-9])", "$1 $2", RegexOptions.IgnoreCase);

            // 3. Optional: Join if the line ends with a lowercase letter, and next line starts with Uppercase 
            // but the uppercase is just a normal word in Vietnamese or English? That's risky (could be a new bullet point).
            // So we stick to the safest rule: lowercase/comma -> lowercase/number.

            return text;
        }
    }
}
