using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public static class AutoFormatter
    {
        private static readonly string[] Abbreviations = new[]
        {
            "tp", "tphcm", "hn", "ths", "ts", "gs", "pgs", "nxb", "stt", "v.v", "mr", "mrs", "dr", "vs", "a.m", "p.m", "đ"
        };

        private static readonly string[] TransitionKeywords = new[]
        {
            "tuy nhiên", "đồng thời", "mặt khác", "hơn nữa", "do đó", "tóm lại", 
            "bên cạnh đó", "ví dụ", "trái lại", "nhờ vậy", "hậu quả là", "thay vào đó",
            "tuy thế", "ngược lại", "với lại", "ngoài ra", "nhưng", "đặc biệt"
        };

        /// <summary>
        /// Sửa lỗi ký tự dính phông chữ (ligatures) phổ biến từ PDF.
        /// </summary>
        public static string FixPdfLigatures(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text
                .Replace("ﬁ", "fi")
                .Replace("ﬂ", "fl")
                .Replace("ﬀ", "ff")
                .Replace("ﬃ", "ffi")
                .Replace("ﬄ", "ffl");
        }

        /// <summary>
        /// Kiểm tra xem dòng hiện tại có phải là số trang, header, footer hoặc rác PDF không.
        /// </summary>
        public static bool IsPdfHeaderFooterOrPageNumber(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string trimmed = line.Trim();

            // 1. Chỉ số trang đơn lẻ (chỉ gồm số, ví dụ "14" hoặc "- 14 -")
            if (Regex.IsMatch(trimmed, @"^\s*[-–—]?\s*\d+\s*[-–—]?\s*$")) return true;

            // 2. Định dạng số trang kiểu "Trang X" hoặc "Page X", "Page X of Y"
            if (Regex.IsMatch(trimmed, @"^\s*(trang|page|p\.)\s*\d+(\s*(of|trên|/)\s*\d+)?\s*$", RegexOptions.IgnoreCase)) return true;

            // 3. Định dạng số trang kiểu "X / Y" hoặc "X/Y"
            if (Regex.IsMatch(trimmed, @"^\s*\d+\s*[\s/]\s*\d+\s*$")) return true;

            // 4. Các dòng bản quyền phổ biến
            if (Regex.IsMatch(trimmed, @"^\s*(copyright|©|all rights reserved)\s*.*$", RegexOptions.IgnoreCase)) return true;

            // 5. Các dòng chứa URL website đơn thuần
            if (Regex.IsMatch(trimmed, @"^(https?://)?(www\.)?[a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,}(/\S*)?$", RegexOptions.IgnoreCase)) return true;

            return false;
        }

        /// <summary>
        /// Sắp xếp và định dạng thông minh toàn bộ văn bản được chọn, tối ưu hóa đặc biệt cho PDF.
        /// </summary>
        public static string FormatTextSmart(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 1. Sửa lỗi ký tự dính (ligatures) từ PDF
            string fixedText = FixPdfLigatures(text);

            // 2. Dọn dẹp dấu câu và khoảng trắng cơ bản
            string cleanText = CleanPunctuation(fixedText);

            // 3. Chia thành các dòng thô và lọc bỏ rác PDF (số trang, headers, footers)
            var rawLines = cleanText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var filteredLines = new List<string>();
            foreach (var line in rawLines)
            {
                string trimmed = line.Trim();
                if (IsPdfHeaderFooterOrPageNumber(trimmed))
                {
                    Program.Log($"[Heuristics PDF] Đã lọc bỏ dòng rác: \"{trimmed}\"");
                    continue; // Bỏ qua dòng rác
                }
                filteredLines.Add(trimmed);
            }

            var finalParagraphs = new List<string>();
            var currentParagraph = new List<string>();

            for (int i = 0; i < filteredLines.Count; i++)
            {
                string currentLine = filteredLines[i];

                if (string.IsNullOrEmpty(currentLine))
                {
                    // Nếu gặp dòng trống, đẩy đoạn văn hiện tại ra và bắt đầu đoạn mới
                    if (currentParagraph.Count > 0)
                    {
                        finalParagraphs.Add(RebuildParagraph(currentParagraph));
                        currentParagraph.Clear();
                    }
                    finalParagraphs.Add(""); // Giữ dòng trống để ngăn cách đoạn
                    continue;
                }

                // Nếu là tiêu đề hoặc danh sách, đẩy đoạn cũ ra, giữ nguyên dòng này thành đoạn riêng
                if (IsHeading(currentLine) || IsListItem(currentLine))
                {
                    if (currentParagraph.Count > 0)
                    {
                        finalParagraphs.Add(RebuildParagraph(currentParagraph));
                        currentParagraph.Clear();
                    }
                    finalParagraphs.Add(currentLine);
                    continue;
                }

                currentParagraph.Add(currentLine);
            }

            if (currentParagraph.Count > 0)
            {
                finalParagraphs.Add(RebuildParagraph(currentParagraph));
            }

            // Loại bỏ các dòng trống liền nhau không cần thiết
            var result = new List<string>();
            bool lastWasEmpty = false;
            foreach (var para in finalParagraphs)
            {
                if (string.IsNullOrEmpty(para))
                {
                    if (!lastWasEmpty)
                    {
                        result.Add("");
                        lastWasEmpty = true;
                    }
                }
                else
                {
                    result.Add(para);
                    lastWasEmpty = false;
                }
            }

            return string.Join("\r\n", result);
        }

        /// <summary>
        /// Gộp các dòng trong một đoạn văn bản, tự động xử lý dấu gạch nối (hyphenation) ở cuối dòng.
        /// </summary>
        private static string RebuildParagraph(List<string> lines)
        {
            if (lines == null || lines.Count == 0) return "";

            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (sb.Length == 0)
                {
                    sb.Append(line);
                }
                else
                {
                    string prevText = sb.ToString();
                    // Nếu dòng trước kết thúc bằng dấu gạch nối và là chữ cái, tiến hành ghép từ không khoảng trắng
                    if (prevText.EndsWith("-") && prevText.Length > 1 && char.IsLetter(prevText[prevText.Length - 2]))
                    {
                        // Xóa dấu gạch nối ở cuối
                        sb.Remove(sb.Length - 1, 1);
                        sb.Append(line);
                    }
                    else
                    {
                        // Thêm dấu cách trước khi nối dòng tiếp theo
                        sb.Append(" ");
                        sb.Append(line);
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Dọn dẹp khoảng trắng thừa và sửa lỗi khoảng cách xung quanh các dấu câu.
        /// </summary>
        public static string CleanPunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Loại bỏ khoảng trắng ngang thừa (tabs, multiple spaces) giữ nguyên ngắt dòng
            text = Regex.Replace(text, @"[ \t]+", " ");

            // Loại bỏ khoảng trắng trước các dấu câu phổ biến
            text = Regex.Replace(text, @"\s+([,\.\:;\?!])", "$1");

            // Tự động thêm dấu cách sau các dấu câu nếu thiếu, loại trừ số thập phân (1.5) hoặc tỷ số (2:3)
            // Thêm space sau dấu phẩy: nội dung,như thế -> nội dung, như thế
            text = Regex.Replace(text, @"(,)(?=[^\s\d])", "$1 ");

            // Thêm space sau dấu chấm: câu kết thúc.Tiếp theo -> câu kết thúc. Tiếp theo
            text = Regex.Replace(text, @"(\.)(?=[^\s\d])", "$1 ");

            // Thêm space sau dấu hai chấm: kết quả:ABC -> kết quả: ABC
            text = Regex.Replace(text, @"(:)(?=[^\s\d])", "$1 ");

            // Thêm space sau dấu chấm phẩy
            text = Regex.Replace(text, @";(?=[^\s])", "; ");

            // Thêm space sau dấu chấm hỏi và chấm than
            text = Regex.Replace(text, @"([\?!])(?=[^\s])", "$1 ");

            return text;
        }

        /// <summary>
        /// Tách đoạn văn bản thành các câu độc lập dựa trên dấu kết thúc.
        /// </summary>
        public static List<string> SplitIntoSentences(string paragraph)
        {
            var sentences = new List<string>();
            if (string.IsNullOrEmpty(paragraph)) return sentences;

            int start = 0;
            int len = paragraph.Length;

            for (int i = 0; i < len; i++)
            {
                char c = paragraph[i];
                if (c == '.' || c == '?' || c == '!')
                {
                    bool isSentenceEnd = false;

                    if (i == len - 1)
                    {
                        isSentenceEnd = true;
                    }
                    else if (char.IsWhiteSpace(paragraph[i + 1]))
                    {
                        // Quét ngược tìm từ kết thúc để tránh tách sai các từ viết tắt
                        int wordStart = i;
                        while (wordStart > 0 && char.IsLetterOrDigit(paragraph[wordStart - 1]))
                        {
                            wordStart--;
                        }
                        string word = paragraph.Substring(wordStart, i - wordStart).ToLowerInvariant();

                        if (Abbreviations.Contains(word))
                        {
                            isSentenceEnd = false; // Tránh ngắt ở "TP.", "TS."
                        }
                        else
                        {
                            // Kiểm tra xem ký tự đầu tiên của từ tiếp theo có viết hoa hoặc là số không
                            int nextCharIdx = i + 1;
                            while (nextCharIdx < len && char.IsWhiteSpace(paragraph[nextCharIdx]))
                            {
                                nextCharIdx++;
                            }
                            if (nextCharIdx == len || char.IsUpper(paragraph[nextCharIdx]) || char.IsDigit(paragraph[nextCharIdx]))
                            {
                                isSentenceEnd = true;
                            }
                        }
                    }

                    if (isSentenceEnd)
                    {
                        string sentence = paragraph.Substring(start, i - start + 1).Trim();
                        if (!string.IsNullOrEmpty(sentence))
                        {
                            sentences.Add(sentence);
                        }
                        start = i + 1;
                    }
                }
            }

            if (start < len)
            {
                string lastSentence = paragraph.Substring(start).Trim();
                if (!string.IsNullOrEmpty(lastSentence))
                {
                    sentences.Add(lastSentence);
                }
            }

            return sentences;
        }

        private static bool StartsWithTransitionWord(string sentence)
        {
            string clean = sentence.Trim().ToLowerInvariant();
            return TransitionKeywords.Any(kw => clean.StartsWith(kw));
        }

        private static bool IsHeading(string text)
        {
            if (text.StartsWith("#")) return true;

            // Thường là tiêu đề dòng ngắn (< 100 ký tự) không chứa dấu chấm cuối câu
            if (text.Length < 100 && !text.EndsWith(".") && !text.EndsWith("?") && !text.EndsWith("!"))
            {
                string lower = text.ToLowerInvariant();
                if (lower.StartsWith("chương") || lower.StartsWith("phần") || lower.StartsWith("mục") || 
                    lower.StartsWith("điều") || lower.StartsWith("bài") || lower.StartsWith("tiêu đề"))
                {
                    return true;
                }

                // Kiểm tra xem dòng đó có viết hoa toàn bộ (All Caps) không
                int upperCount = 0;
                int letterCount = 0;
                foreach (char c in text)
                {
                    if (char.IsLetter(c))
                    {
                        letterCount++;
                        if (char.IsUpper(c)) upperCount++;
                    }
                }
                if (letterCount > 0 && (double)upperCount / letterCount > 0.8)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsListItem(string text)
        {
            // Các ký tự đầu dòng của danh sách
            if (text.StartsWith("-") || text.StartsWith("*") || text.StartsWith("+") || 
                text.StartsWith("•") || text.StartsWith("o "))
            {
                return true;
            }

            // Dạng 1., 1), a., a), I.
            var listRegex = new Regex(@"^\s*([0-9a-zA-Z]|[ivxIVX]+)[\.\)]\s+");
            return listRegex.IsMatch(text);
        }

        private static bool IsShortLine(string text)
        {
            // Các dòng chữ ký, kết luận ngắn không chấm câu
            return text.Length < 60 && !text.EndsWith(".") && !text.EndsWith("?") && !text.EndsWith("!");
        }

        /// <summary>
        /// Nhận diện xem đoạn văn bản có phải là văn bản thô, chưa được cấu trúc hóa (như OCR, ghi âm giọng nói).
        /// </summary>
        public static bool IsUnstructuredRawText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (trimmed.Length < 150) return false; // Ngưỡng tối thiểu để kích hoạt AI/ML tránh lãng phí hiệu năng

            // Nếu đã chứa các định dạng Markdown phổ biến thì coi như đã được cấu trúc hóa
            if (trimmed.Contains("```") || trimmed.Contains("|---|") || trimmed.Contains("### ") || 
                trimmed.Contains("- ") || trimmed.Contains("* ") || trimmed.Contains("• "))
            {
                return false;
            }

            // Nếu là copy mã nguồn từ VS Code thì giữ nguyên định dạng thô của code
            if (ClipboardHelper.IsVscodeEditorCopy())
            {
                return false;
            }

            // Tính số lượng ngắt đoạn đôi (\n\n) để đo độ dày đặc của khối chữ
            int doubleNewlineCount = Regex.Matches(trimmed, @"(\r?\n){2,}").Count;

            // Nếu khối chữ dài nhưng có rất ít hoặc không có ngắt đoạn đôi (<= 1), đây chính là một "bức tường chữ" phi cấu trúc
            if (doubleNewlineCount <= 1)
            {
                return true;
            }

            // Hoặc trường hợp OCR bị ngắt dòng cứng liên tục mà không chia đoạn lớn
            int singleNewlineCount = Regex.Matches(trimmed, @"\r?\n").Count;
            if (doubleNewlineCount == 0 && singleNewlineCount > 2)
            {
                return true;
            }

            return false;
        }
    }
}
