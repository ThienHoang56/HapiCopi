using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    /// <summary>
    /// Bộ máy đồng bộ CẤU TRÚC đề mục &amp; danh sách (không chỉ typography).
    ///
    /// Nhiệm vụ:
    /// 1. Nhận diện đề mục trong nội dung nguồn (Markdown, plain text, HTML từ Gemini/ChatGPT)
    ///    kể cả khi nguồn không dùng thẻ heading mà chỉ ghi "4.5. Research Objectives".
    /// 2. Ánh xạ độ sâu số đề mục (4.5 => cấp 2) sang đúng cấp Heading của tài liệu đích,
    ///    thay vì tin tuyệt đối vào cấp markdown hoặc cộng dồn cấp của con trỏ.
    /// 3. Đánh số lại theo sườn tài liệu thật (4.3 -> nội dung mới 4.5 => 4.4).
    /// 4. Rót danh sách vào đúng khuôn bullet/numbering đã đo từ tài liệu.
    /// </summary>
    public static class OutlineStructureService
    {
        // Trạng thái đánh số phiên (ProjectedOutline/SiblingCounters) nằm trên DocumentStructureTree.
        // Clipboard có thể xử lý 2 lần copy chồng nhau; khóa ngắn này bảo đảm số cấp ra tuần tự.
        private static readonly object ProjectionGate = new();

        private static readonly Regex NumberPrefixRegex = new(
            @"^(?<num>\d+(?:\.\d+){0,5})(?<dot>\.?)\s+(?<title>\S.*)$",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownHeadingRegex = new(
            @"^(?<hashes>#{1,6})\s+(?<text>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex BulletLineRegex = new(
            @"^(?<indent>[ \t]*)[-*•–]\s+(?<text>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex LeadingNumberInHtmlRegex = new(
            @"^(?<lead>(?:\s|&nbsp;|&#160;|<[^>]+>)*)(?<num>\d+(?:\.\d+){0,5})(?<dot>\.?)(?<gap>\s*)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // ----------------------------------------------------------------------
        // 1. PHÂN TÍCH TIỀN TỐ SỐ ĐỀ MỤC
        // ----------------------------------------------------------------------

        public sealed class HeadingPrefix
        {
            public string Number { get; init; } = "";
            public string Dot { get; init; } = "";
            public string Title { get; init; } = "";
            public List<int> Parts { get; init; } = new();
            public int Depth => Parts.Count;
            public string NumberWithDot => Number + Dot;
        }

        /// <summary>Phân tích "4.5. Research Objectives" thành số [4,5] + tiêu đề.</summary>
        public static bool TryParseHeadingPrefix(string plainText, out HeadingPrefix prefix)
        {
            prefix = new HeadingPrefix();
            if (string.IsNullOrWhiteSpace(plainText)) return false;

            string text = WebUtility.HtmlDecode(plainText).Trim();

            // Gemini/ChatGPT copy dạng plain text thường bọc heading bằng **...**.
            // Bóc marker ở hai đầu để vẫn nhận diện được "**4.5. Research Objectives**".
            text = Regex.Replace(text, @"^(?:\*{1,3}|_{1,3})\s*", "");
            text = Regex.Replace(text, @"\s*(?:\*{1,3}|_{1,3})$", "");

            Match m = NumberPrefixRegex.Match(text);
            if (!m.Success) return false;

            string number = m.Groups["num"].Value;
            string dot = m.Groups["dot"].Value;
            string title = m.Groups["title"].Value.Trim();
            if (title.Length == 0) return false;

            var parts = number.Split('.')
                .Select(p => int.TryParse(p, out int v) ? v : 0)
                .ToList();
            if (parts.Count == 0 || parts.Any(p => p <= 0)) return false;

            prefix = new HeadingPrefix
            {
                Number = number,
                Dot = dot,
                Title = title,
                Parts = parts
            };
            return true;
        }

        /// <summary>Bỏ toàn bộ thẻ HTML để lấy text thuần (dùng cho việc nhận diện).</summary>
        public static string StripHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            return Regex.Replace(text, @"[ \t\r\n]+", " ").Trim();
        }

        private static bool IsAllCapsTitle(string title)
        {
            bool hasLetter = false;
            foreach (char c in title)
            {
                if (char.IsLetter(c))
                {
                    hasLetter = true;
                    if (char.IsLower(c)) return false;
                }
            }
            return hasLetter && title.Length >= 3;
        }

        private static bool EndsLikeSentence(string title)
        {
            char c = title.TrimEnd().LastOrDefault();
            return c == '.' || c == ':' || c == ';';
        }

        /// <summary>
        /// Tiêu đề dạng Title Case ("Research Objectives", "Brand Positioning")
        /// khác với câu liệt kê ("To evaluate current brand perception").
        /// </summary>
        private static bool LooksLikeTitleCase(string title)
        {
            if (title.Length == 0 || title.Length > 80) return false;

            var words = Regex.Split(title, @"\s+")
                .Select(w => w.Trim('"', '\'', '“', '”', '(', ')', '[', ']'))
                .Where(w => w.Any(char.IsLetter))
                .ToList();

            if (words.Count == 0) return false;

            char FirstLetter(string word)
            {
                foreach (char c in word)
                {
                    if (char.IsLetter(c)) return c;
                }
                return '\0';
            }

            if (!char.IsUpper(FirstLetter(words[0]))) return false;

            int capitalized = words.Count(w => char.IsUpper(FirstLetter(w)));
            double ratio = (double)capitalized / words.Count;
            return ratio >= 0.6;
        }

        /// <summary>
        /// Một dòng plain text có phải đề mục không?
        /// - Cấp >= 2 (4.5, 4.5.1) là tín hiệu rất mạnh.
        /// - Cấp 1 chỉ nhận khi viết hoa toàn bộ hoặc đứng tách biệt, ngắn, không phải câu liệt kê.
        /// </summary>
        private static bool IsHeadingCandidate(IReadOnlyList<string> lines, int index, HeadingPrefix prefix)
        {
            string title = prefix.Title.Trim();
            if (title.Length == 0 || title.Length > 160) return false;
            if (title.Contains(':')) return false;          // "Age: 18-25" là item, không phải heading
            if (EndsLikeSentence(title)) return false;

            if (prefix.Depth >= 2)
            {
                return char.IsUpper(title[0]);
            }

            if (IsAllCapsTitle(title)) return true;

            bool prevBlank = index <= 0 || string.IsNullOrWhiteSpace(lines[index - 1]);
            bool nextBlank = index >= lines.Count - 1 || string.IsNullOrWhiteSpace(lines[index + 1]);
            return prevBlank && nextBlank && LooksLikeTitleCase(title);
        }

        // ----------------------------------------------------------------------
        // 2. ĐÁNH SỐ LẠI THEO SƯỜN TÀI LIỆU
        // ----------------------------------------------------------------------

        /// <summary>
        /// Bộ đánh số có trạng thái: đi theo đúng thứ tự khối nội dung được dán,
        /// giữ cho các đề mục con nhất quán với đề mục cha vừa được đánh lại.
        /// </summary>
        public sealed class OutlineRenumberer
        {
            private readonly DocumentStructureTree _tree;
            private readonly Dictionary<string, int> _lastSibling = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _prefixMap = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _existingTitle = new(StringComparer.Ordinal);
            private readonly HashSet<string> _existingNumbers = new(StringComparer.Ordinal);

            public OutlineRenumberer(DocumentStructureTree tree)
            {
                _tree = tree;

                foreach (var entry in tree.Outline.Concat(tree.ProjectedOutline))
                {
                    if (entry.NumberParts.Count == 0) continue;

                    string full = string.Join(".", entry.NumberParts);
                    _existingNumbers.Add(full);

                    string titleKey = NormalizeTitle(entry.Title);
                    if (!string.IsNullOrEmpty(titleKey))
                    {
                        _existingTitle[full] = titleKey;
                    }

                    string parent = string.Join(".", entry.NumberParts.Take(entry.NumberParts.Count - 1));
                    int last = entry.NumberParts[^1];
                    if (!_lastSibling.TryGetValue(parent, out int current) || last > current)
                    {
                        _lastSibling[parent] = last;
                    }
                }

                foreach (var pair in tree.ProjectedSiblingCounters)
                {
                    if (!_lastSibling.TryGetValue(pair.Key, out int current) || pair.Value > current)
                    {
                        _lastSibling[pair.Key] = pair.Value;
                    }
                }
            }

            public bool CanRenumber => _tree.RenumberOutline && _existingNumbers.Count > 0;

            /// <summary>
            /// Trả về cấp Heading đích và số đề mục mới (kèm dấu chấm nếu tài liệu dùng).
            /// numberText = null nghĩa là giữ nguyên số gốc.
            /// </summary>
            public (int Level, string? NumberText) Process(int requestedLevel, HeadingPrefix? prefix)
            {
                requestedLevel = Math.Clamp(requestedLevel, 1, 6);
                if (prefix == null || prefix.Parts.Count == 0)
                {
                    return (requestedLevel, null);
                }

                // Độ sâu số là bằng chứng mạnh hơn cấp markdown — nhưng chỉ khi tài liệu
                // có sườn đề mục đo được. Với MagicWand (cây quét từ Word, không có sườn),
                // phải giữ nguyên cấp đã được tịnh tiến theo con trỏ.
                int level = requestedLevel;
                if (_tree.Outline.Count > 0 && prefix.Depth >= 1 && prefix.Depth <= 6)
                {
                    level = _tree.MapLevelByNumberDepth(prefix.Depth);
                }

                if (!CanRenumber || prefix.Parts.Count < level)
                {
                    return (level, null);
                }

                string originalParent = string.Join(".", prefix.Parts.Take(level - 1));
                string parent = _prefixMap.TryGetValue(originalParent, out var mappedParent)
                    ? mappedParent
                    : originalParent;

                string originalFull = string.Join(".", prefix.Parts.Take(level));
                int requested = prefix.Parts[level - 1];

                int last = _lastSibling.GetValueOrDefault(parent, 0);

                // Nếu đề mục y hệt đã tồn tại (cùng số + cùng tiêu đề) => đang cập nhật, giữ nguyên số.
                if (_existingNumbers.Contains(originalFull)
                    && _existingTitle.TryGetValue(originalFull, out string? existingTitle)
                    && existingTitle == NormalizeTitle(prefix.Title))
                {
                    _lastSibling[parent] = Math.Max(last, requested);
                    _prefixMap[originalFull] = originalFull;
                    return (level, BuildNumberText(level, parent, requested));
                }

                int chosen;
                if (_existingNumbers.Contains(originalFull))
                {
                    // Trùng số nhưng khác nội dung => cấp số tiếp theo.
                    chosen = Math.Max(last + 1, requested + 1);
                }
                else if (requested > last + 1)
                {
                    // Nhảy cóc (4.3 -> 4.5 khi chưa có 4.4): lấp vào số kế tiếp.
                    chosen = last + 1;
                }
                else
                {
                    chosen = Math.Max(1, requested);
                }

                if (chosen <= 0) chosen = 1;

                string newFull = string.IsNullOrEmpty(parent)
                    ? chosen.ToString()
                    : parent + "." + chosen;

                _lastSibling[parent] = Math.Max(last, chosen);
                _tree.ProjectedSiblingCounters[parent] = Math.Max(last, chosen);
                _prefixMap[originalFull] = newFull;
                _existingNumbers.Add(newFull);

                // Ghi nhớ số vừa cấp trong RAM để lần Copy kế tiếp đi tiếp 4.5, 4.6...
                // (không ghi vào file hồ sơ).
                var projectedParts = new List<int>();
                if (!string.IsNullOrEmpty(parent))
                {
                    projectedParts.AddRange(parent.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0));
                }
                projectedParts.Add(chosen);

                _tree.ProjectedOutline.Add(new OutlineEntry
                {
                    Level = level,
                    Number = newFull,
                    NumberParts = projectedParts,
                    Title = prefix.Title,
                    Text = newFull + " " + prefix.Title,
                    NumberIsAutomatic = false
                });

                // Giới hạn bộ nhớ phiên: chỉ cần các đề mục gần đây để chống trùng số.
                if (_tree.ProjectedOutline.Count > 500)
                {
                    _tree.ProjectedOutline.RemoveRange(0, _tree.ProjectedOutline.Count - 500);
                }

                return (level, BuildNumberText(level, parent, chosen));
            }

            private string BuildNumberText(int level, string parent, int lastPart)
            {
                var parts = new List<int>();
                if (!string.IsNullOrEmpty(parent))
                {
                    parts.AddRange(parent.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0));
                }
                parts.Add(lastPart);

                string format = _tree.GetHeadingNumberFormat(level);
                string formatted = format;
                for (int i = 1; i <= 6; i++)
                {
                    string token = "{" + i + "}";
                    if (!formatted.Contains(token)) continue;
                    int value = i <= parts.Count ? parts[i - 1] : 1;
                    formatted = formatted.Replace(token, value.ToString());
                }

                if (formatted.Contains('{'))
                {
                    formatted = string.Join(".", parts) + ".";
                }

                return formatted;
            }

            private static string NormalizeTitle(string title)
            {
                string t = WebUtility.HtmlDecode(title ?? "").ToLowerInvariant();
                t = Regex.Replace(t, @"\s+", " ").Trim();
                t = t.TrimEnd('.', ':', ';', ',');
                return t;
            }
        }

        // ----------------------------------------------------------------------
        // 3. CHUẨN HÓA PLAIN TEXT / MARKDOWN
        // ----------------------------------------------------------------------

        /// <summary>
        /// Tiền xử lý văn bản nguồn trước khi đưa vào Markdig:
        /// - Chuyển "4.5. Research Objectives" thành heading cấp đúng (thay vì bị Markdig hiểu là ordered list).
        /// - Ánh xạ cấp heading theo độ sâu số đo được từ tài liệu đích.
        /// - Đánh số lại nếu bật RenumberOutline.
        /// - Đổi bullet lạ (•, –) về "-" để Markdig hiểu.
        /// </summary>
        public static string NormalizePlainTextOutline(string text, DocumentStructureTree tree)
        {
            lock (ProjectionGate)
            {
                return NormalizePlainTextOutlineCore(text, tree);
            }
        }

        private static string NormalizePlainTextOutlineCore(string text, DocumentStructureTree tree)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var renumberer = new OutlineRenumberer(tree);
            var output = new List<string>(lines.Length);

            bool inFence = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    output.Add(line);
                    continue;
                }

                if (inFence)
                {
                    output.Add(line);
                    continue;
                }

                // Bullet lạ từ web/AI
                Match bullet = BulletLineRegex.Match(line);
                if (bullet.Success && !line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    string indent = bullet.Groups["indent"].Value;
                    output.Add($"{indent}- {bullet.Groups["text"].Value}");
                    continue;
                }

                int hashCount = 0;
                string content = line;
                Match md = MarkdownHeadingRegex.Match(trimmed);
                if (md.Success)
                {
                    hashCount = md.Groups["hashes"].Value.Length;
                    content = md.Groups["text"].Value.Trim();
                }

                if (TryParseHeadingPrefix(content, out var prefix))
                {
                    bool isExistingMdHeading = hashCount > 0;
                    if (isExistingMdHeading || IsHeadingCandidate(lines, i, prefix))
                    {
                        int requestedLevel = hashCount > 0 ? hashCount : Math.Clamp(prefix.Depth, 1, 6);
                        if (tree.Outline.Count > 0 && prefix.Depth >= 1)
                        {
                            requestedLevel = tree.MapLevelByNumberDepth(prefix.Depth);
                        }

                        var (level, renumbered) = renumberer.Process(requestedLevel, prefix);
                        string numberText = renumbered ?? prefix.NumberWithDot;
                        output.Add(new string('#', Math.Clamp(level, 1, 6)) + " " + numberText + " " + prefix.Title);
                        continue;
                    }
                }

                output.Add(line);
            }

            return string.Join(newline, output);
        }

        // ----------------------------------------------------------------------
        // 4. RÓT NỘI DUNG VÀO KHUÔN HTML CỦA TÀI LIỆU
        // ----------------------------------------------------------------------

        public static string BuildHeadingHtml(int level, string innerHtml, DocumentStructureTree tree)
        {
            level = Math.Clamp(level, 1, 6);
            if (!tree.Headings.TryGetValue(level, out var heading))
            {
                heading = new HeadingStyleNode
                {
                    Level = level,
                    FontName = tree.BodyStyle.FontName,
                    FontSize = tree.BodyStyle.FontSize,
                    Bold = true
                };
            }

            return $"<h{level} class=\"MsoHeading{level}\" style=\"{heading.ToCssStyle()}\">{innerHtml}</h{level}>";
        }

        public static string BuildListParagraphHtml(
            string innerHtml,
            int level,
            bool ordered,
            int itemIndex,
            DocumentStructureTree tree)
        {
            var style = tree.GetListStyle(ordered, level);
            string marker;
            if (ordered)
            {
                marker = FormatOrderedMarker(style, Math.Max(1, itemIndex));
            }
            else
            {
                marker = string.IsNullOrWhiteSpace(style.BulletOrPrefix) || style.BulletOrPrefix.StartsWith("%")
                    ? "-"
                    : style.BulletOrPrefix;
            }

            string css = style.ToCssStyle(tree.BodyStyle);
            string markerHtml = WebUtility.HtmlEncode(marker) + "&nbsp;&nbsp;";
            return $"<p class=\"MsoListParagraph\" style=\"{css}\">" +
                   $"<span style=\"mso-list:Ignore;\">{markerHtml}</span>{innerHtml}</p>";
        }

        /// <summary>
        /// Định dạng số thứ tự theo numbering format đo được (decimal / alpha / roman).
        /// Chỉ hỗ trợ một cấp placeholder (%1) — đủ cho tuyệt đại đa số danh sách AI trả về;
        /// nếu template dùng nhiều placeholder, fallback về "n." an toàn.
        /// </summary>
        private static string FormatOrderedMarker(ListLevelStyleNode style, int index)
        {
            string template = style.BulletOrPrefix ?? "";
            int placeholders = Regex.Matches(template, @"%\d").Count;
            if (placeholders == 0)
            {
                return index + ".";
            }
            if (placeholders > 1)
            {
                return index + ".";
            }

            string value = style.NumberFormat.ToLowerInvariant() switch
            {
                "lowerletter" => ToAlpha(index, false),
                "upperletter" => ToAlpha(index, true),
                "lowerroman" => ToRoman(index, false),
                "upperroman" => ToRoman(index, true),
                "decimalzero" => index.ToString("00"),
                _ => index.ToString()
            };

            string marker = Regex.Replace(template, @"%\d", value);
            return string.IsNullOrWhiteSpace(marker) ? index + "." : marker;
        }

        private static string ToAlpha(int number, bool upper)
        {
            if (number <= 0) number = 1;
            var sb = new StringBuilder();
            while (number > 0)
            {
                number--;
                sb.Insert(0, (char)('a' + (number % 26)));
                number /= 26;
            }
            string s = sb.ToString();
            return upper ? s.ToUpperInvariant() : s;
        }

        private static string ToRoman(int number, bool upper)
        {
            if (number <= 0) number = 1;
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] romans = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new StringBuilder();
            int n = Math.Min(number, 3999);
            for (int i = 0; i < values.Length; i++)
            {
                while (n >= values[i])
                {
                    sb.Append(romans[i]);
                    n -= values[i];
                }
            }
            string result = sb.ToString();
            return upper ? result : result.ToLowerInvariant();
        }

        // ----------------------------------------------------------------------
        // 5. CHUẨN HÓA HTML: NÂNG CẤP ĐỀ MỤC + ĐÁNH SỐ LẠI
        // ----------------------------------------------------------------------

        /// <summary>
        /// Quét các thẻ &lt;h1..h6&gt; và cả &lt;p&gt; có nội dung dạng "4.5. Tên mục".
        /// Trả về heading đã áp đúng khuôn + số đã đồng bộ.
        /// </summary>
        public static string PromoteAndStyleHeadings(
            string html,
            DocumentStructureTree tree,
            int parentHeadingLevel = 0)
        {
            lock (ProjectionGate)
            {
                return PromoteAndStyleHeadingsCore(html, tree, parentHeadingLevel);
            }
        }

        private static string PromoteAndStyleHeadingsCore(
            string html,
            DocumentStructureTree tree,
            int parentHeadingLevel)
        {
            if (string.IsNullOrWhiteSpace(html)) return html;

            var renumberer = new OutlineRenumberer(tree);
            int lastIndex = 0;
            int tableDepth = 0;
            int listDepth = 0;

            return Regex.Replace(
                html,
                @"<h(?<hlevel>[1-6])(?:\s+[^>]*)?>(?<hinner>.*?)</h\k<hlevel>>" +
                @"|<p(?<pattrs>(?:\s+[^>]*)?)>(?<pinner>.*?)</p>",
                m =>
                {
                    // Theo dõi xem match hiện tại có nằm trong bảng không (tránh phá cấu trúc ô).
                    string gap = html.Substring(lastIndex, Math.Max(0, m.Index - lastIndex));
                    tableDepth += Regex.Matches(gap, @"<table\b", RegexOptions.IgnoreCase).Count;
                    tableDepth -= Regex.Matches(gap, @"</table\s*>", RegexOptions.IgnoreCase).Count;
                    if (tableDepth < 0) tableDepth = 0;

                    listDepth += Regex.Matches(gap, @"<(?:ul|ol)\b", RegexOptions.IgnoreCase).Count;
                    listDepth -= Regex.Matches(gap, @"</(?:ul|ol)\s*>", RegexOptions.IgnoreCase).Count;
                    if (listDepth < 0) listDepth = 0;
                    lastIndex = m.Index + m.Length;

                    bool isHeadingTag = m.Groups["hlevel"].Success;
                    string inner = isHeadingTag ? m.Groups["hinner"].Value : m.Groups["pinner"].Value;
                    string attrs = isHeadingTag ? "" : m.Groups["pattrs"].Value;

                    if (!isHeadingTag)
                    {
                        if (tableDepth > 0) return m.Value;
                        if (listDepth > 0) return m.Value;
                        if (attrs.Contains("MsoListParagraph", StringComparison.OrdinalIgnoreCase)) return m.Value;
                        if (attrs.Contains("mso-element:comment", StringComparison.OrdinalIgnoreCase)) return m.Value;
                        if (attrs.Contains("data-xpm-math-type", StringComparison.OrdinalIgnoreCase)) return m.Value;
                        if (attrs.Contains("latex-math", StringComparison.OrdinalIgnoreCase)) return m.Value;
                    }

                    string plain = StripHtmlTags(inner);
                    int hashLevel = isHeadingTag ? int.Parse(m.Groups["hlevel"].Value) : 0;

                    if (!TryParseHeadingPrefix(plain, out var prefix))
                    {
                        if (!isHeadingTag) return m.Value;

                        int level = hashLevel;
                        if (parentHeadingLevel > 0)
                        {
                            level = Math.Clamp(parentHeadingLevel + hashLevel, 1, 6);
                        }
                        return BuildHeadingHtml(level, inner, tree);
                    }

                    if (!isHeadingTag)
                    {
                        // &lt;p&gt; không có ngữ cảnh dòng: chỉ nhận khi độ sâu >= 2,
                        // hoặc tiêu đề viết hoa toàn bộ / Title Case (4. RESEARCH QUESTIONS).
                        if (prefix.Depth < 2
                            && !IsAllCapsTitle(prefix.Title)
                            && !LooksLikeTitleCase(prefix.Title))
                        {
                            return m.Value;
                        }
                    }

                    int requestedLevel = tree.Outline.Count > 0
                        ? tree.MapLevelByNumberDepth(prefix.Depth)
                        : Math.Clamp(prefix.Depth, 1, 6);
                    var (mappedLevel, renumbered) = renumberer.Process(requestedLevel, prefix);

                    string newInner = inner;
                    if (!string.IsNullOrEmpty(renumbered))
                    {
                        newInner = ReplaceLeadingNumber(inner, renumbered);
                    }

                    return BuildHeadingHtml(mappedLevel, newInner, tree);
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string ReplaceLeadingNumber(string innerHtml, string newNumberText)
        {
            Match m = LeadingNumberInHtmlRegex.Match(innerHtml);
            if (m.Success)
            {
                string lead = m.Groups["lead"].Value;
                string gap = m.Groups["gap"].Value;
                if (string.IsNullOrEmpty(gap)) gap = " ";
                return innerHtml.Substring(0, m.Index) + lead + newNumberText + gap + innerHtml.Substring(m.Index + m.Length);
            }

            // Không tìm thấy tiền tố ở đầu chuỗi HTML: ghép số mới lên trước.
            return newNumberText + " " + innerHtml;
        }

        // ----------------------------------------------------------------------
        // 6. CHUYỂN DANH SÁCH HTML THÀNH ĐOẠN VĂN CÓ KHUÔN THẬT
        // ----------------------------------------------------------------------

        /// <summary>
        /// Word không tôn trọng CSS list-style khi dán từ clipboard. Để khớp đúng
        /// marker/lề của tài liệu, chuyển &lt;ul&gt;/&lt;ol&gt; thành MsoListParagraph
        /// với marker tường minh (cùng cách Word xuất clipboard của chính nó).
        /// Hỗ trợ danh sách lồng nhau.
        /// </summary>
        public static string ConvertHtmlListsToParagraphs(string html, DocumentStructureTree tree)
        {
            if (string.IsNullOrWhiteSpace(html) || !Regex.IsMatch(html, @"<(ul|ol)\b", RegexOptions.IgnoreCase))
            {
                return html;
            }

            // Gắn độ sâu lồng nhau vào từng thẻ mở trước khi chuyển đổi.
            string annotated = AnnotateListDepth(html);

            // Chuyển từ trong ra ngoài: regex chỉ khớp list không còn list con.
            var pattern = new Regex(
                @"<(?<tag>ul|ol)(?<attrs>[^>]*)>(?<body>(?:(?!</?(?:ul|ol)\b).)*?)</\k<tag>>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            for (int guard = 0; guard < 32; guard++)
            {
                var match = pattern.Match(annotated);
                if (!match.Success) break;

                int level = 1;
                var depthMatch = Regex.Match(match.Groups["attrs"].Value, @"data-hapi-depth\s*=\s*""?(\d+)""?", RegexOptions.IgnoreCase);
                if (depthMatch.Success && int.TryParse(depthMatch.Groups[1].Value, out int d) && d > 0)
                {
                    level = d;
                }

                bool ordered = match.Groups["tag"].Value.Equals("ol", StringComparison.OrdinalIgnoreCase);
                int start = 1;
                var startMatch = Regex.Match(match.Groups["attrs"].Value, @"\bstart\s*=\s*""?(\d+)""?", RegexOptions.IgnoreCase);
                if (startMatch.Success && int.TryParse(startMatch.Groups[1].Value, out int s) && s > 0)
                {
                    start = s;
                }

                var replacement = new StringBuilder();
                var itemPattern = new Regex(@"<li(?<liattrs>[^>]*)>(?<li>.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                int index = start;
                foreach (Match item in itemPattern.Matches(match.Groups["body"].Value))
                {
                    string content = item.Groups["li"].Value.Trim();

                    // Tách phần nội dung của chính item khỏi các list con đã được chuyển thành <p>.
                    int nestedIndex = content.IndexOf("<p class=\"MsoListParagraph\"", StringComparison.OrdinalIgnoreCase);
                    string ownContent = nestedIndex >= 0 ? content.Substring(0, nestedIndex) : content;
                    string nestedContent = nestedIndex >= 0 ? content.Substring(nestedIndex) : "";

                    // Chỉ dỡ lớp <p> bao ngoài; tuyệt đối không dỡ các <p> MsoListParagraph của list con.
                    ownContent = Regex.Replace(ownContent, @"^\s*<p[^>]*>", "", RegexOptions.IgnoreCase);
                    ownContent = Regex.Replace(ownContent, @"</p>\s*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    ownContent = Regex.Replace(ownContent, @"^<p[^>]*>(.*?)</p>$", "$1", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    ownContent = ownContent.Trim();

                    if (!string.IsNullOrEmpty(ownContent))
                    {
                        replacement.Append(BuildListParagraphHtml(ownContent, level, ordered, index, tree));
                    }
                    replacement.Append(nestedContent);
                    index++;
                }

                annotated = annotated.Substring(0, match.Index) + replacement + annotated.Substring(match.Index + match.Length);
            }

            return annotated;
        }

        private static string AnnotateListDepth(string html)
        {
            int depth = 0;
            return Regex.Replace(
                html,
                @"</?(?:ul|ol)\b[^>]*>",
                m =>
                {
                    if (m.Value.StartsWith("</", StringComparison.Ordinal))
                    {
                        depth = Math.Max(0, depth - 1);
                        return m.Value;
                    }

                    depth++;
                    string tag = m.Value;
                    if (tag.EndsWith("/>", StringComparison.Ordinal))
                    {
                        depth = Math.Max(0, depth - 1);
                        return tag;
                    }

                    int insertAt = tag.IndexOf('>');
                    return insertAt >= 0
                        ? tag.Insert(insertAt, $" data-hapi-depth=\"{depth}\"")
                        : tag;
                },
                RegexOptions.IgnoreCase);
        }
    }
}
