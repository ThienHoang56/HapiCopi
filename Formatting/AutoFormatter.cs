using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Sắp xếp và định dạng thông minh toàn bộ văn bản được chọn.
        /// </summary>
        public static string FormatTextSmart(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 1. Dọn dẹp dấu câu và khoảng trắng cơ bản trước khi xử lý cấu trúc
            string cleanText = CleanPunctuation(text);

            // 2. Chia thành các dòng/đoạn thô dựa trên ký tự xuống dòng
            var rawLines = cleanText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var finalParagraphs = new List<string>();
            var currentParagraph = new List<string>();

            foreach (var line in rawLines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                {
                    // Nếu gặp dòng trống, đẩy đoạn văn hiện tại ra và bắt đầu đoạn mới
                    if (currentParagraph.Count > 0)
                    {
                        finalParagraphs.Add(string.Join(" ", currentParagraph));
                        currentParagraph.Clear();
                    }
                    finalParagraphs.Add(""); // Giữ dòng trống để ngăn cách đoạn
                    continue;
                }

                // Nếu là tiêu đề hoặc danh sách, đẩy đoạn cũ ra, giữ nguyên dòng này thành đoạn riêng
                if (IsHeading(trimmedLine) || IsListItem(trimmedLine))
                {
                    if (currentParagraph.Count > 0)
                    {
                        finalParagraphs.Add(string.Join(" ", currentParagraph));
                        currentParagraph.Clear();
                    }
                    finalParagraphs.Add(trimmedLine);
                    continue;
                }

                // Kiểm tra xem dòng hiện tại có nên nối tiếp với dòng trước đó không
                // Thường nối tiếp nếu dòng trước không kết thúc bằng dấu chấm câu (. ! ?)
                if (currentParagraph.Count > 0)
                {
                    string lastLine = currentParagraph[currentParagraph.Count - 1];
                    bool endsWithPunctuation = lastLine.EndsWith(".") || lastLine.EndsWith("!") || lastLine.EndsWith("?") || lastLine.EndsWith(":") || lastLine.EndsWith("`") || lastLine.EndsWith("\"");
                    
                    if (endsWithPunctuation)
                    {
                        // Đoạn trước đã kết thúc trọn vẹn bằng dấu câu, nhưng không có dòng trống ngăn cách,
                        // Ta vẫn giữ nguyên như một câu độc lập trong cùng đoạn
                        currentParagraph.Add(trimmedLine);
                    }
                    else
                    {
                        // Dòng trước bị ngắt nửa chừng (ví dụ khi copy từ PDF hoặc AI trả về ngắt dòng lung tung)
                        // Ta tiến hành nối liền hai dòng lại với nhau
                        currentParagraph[currentParagraph.Count - 1] = lastLine + " " + trimmedLine;
                    }
                }
                else
                {
                    currentParagraph.Add(trimmedLine);
                }
            }

            if (currentParagraph.Count > 0)
            {
                finalParagraphs.Add(string.Join(" ", currentParagraph));
            }

            // Loại bỏ các dòng trống liền nhau không cần thiết để văn bản cân đối
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

            // MS Word sử dụng \r\n hoặc \r làm ký tự kết thúc đoạn paragraph
            return string.Join("\r\n", result);
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
    }
}
