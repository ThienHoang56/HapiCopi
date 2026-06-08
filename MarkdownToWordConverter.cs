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
            string textWithMath = ProcessLatexInHtml(markdownText, formatInfo);

            // 2. Sử dụng Markdig để parse Markdown sang HTML chuẩn
            string htmlFragment = Markdown.ToHtml(textWithMath, MarkdownPipeline);

            // 2. Đồng hóa Font và Size cho văn bản thường (Paragraphs)
            // Áp dụng class MsoNormal và chỉ đồng hóa Font/Size nếu lấy được từ Word, hoàn toàn không can thiệp vào Spacing/Line spacing/Alignment/Color
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
            // 5.1. Định dạng khối code (Code Blocks) giữ nguyên thụt lề và xuống dòng
            var preRegex = new Regex(@"<pre><code(?:[^>]*)>(.*?)</code></pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            htmlFragment = preRegex.Replace(htmlFragment, match =>
            {
                string codeContent = match.Groups[1].Value;
                return $"<div class=\"MsoNormal\" style=\"font-family: 'Consolas', 'Courier New', monospace; font-size: 10pt; background-color: #f8f9fa; border: 1px solid #e9ecef; padding: 8pt; margin: 6pt 0; white-space: pre; line-height: 1.2; color: #212529;\">{codeContent}</div>";
            });

            // 5.2. Định dạng mã nguồn nội dòng (Inline Code)
            htmlFragment = htmlFragment.Replace("<code>", "<code style=\"font-family: 'Consolas', 'Courier New', monospace; font-size: 10pt; background-color: #f8f9fa; border: 1px solid #e9ecef; padding: 2px 4px; border-radius: 3px; color: #e83e8c;\">");
            
            // 6. Định dạng Bảng biểu (Table Formatting) - Phase 1
            // Cấu hình border-collapse và đường viền xám mỏng, đồng thời sử dụng regex để giữ vững căn lề (align)
            htmlFragment = htmlFragment.Replace("<table>", "<table class=\"MsoNormalTable\" style=\"border-collapse: collapse; width: 100%; border: 1px solid #cccccc; margin: 6pt 0;\">");
            
            var thRegex = new Regex(@"<th([^>]*)>", RegexOptions.IgnoreCase);
            htmlFragment = thRegex.Replace(htmlFragment, m => FormatTableCell("th", m.Groups[1].Value, "border: 1px solid #cccccc; padding: 6px; background-color: #f2f2f2; font-weight: bold; text-align: left;"));
            
            var tdRegex = new Regex(@"<td([^>]*)>", RegexOptions.IgnoreCase);
            htmlFragment = tdRegex.Replace(htmlFragment, m => FormatTableCell("td", m.Groups[1].Value, "border: 1px solid #cccccc; padding: 6px; text-align: left;"));

            // 7. Định dạng Blockquotes (Trích dẫn) - Phase 1
            htmlFragment = htmlFragment.Replace("<blockquote>", "<blockquote style=\"border-left: 3px solid #cccccc; padding-left: 10pt; margin: 6pt 0 6pt 12pt; color: #555555; font-style: italic;\">");

            // 8. Định dạng Hyperlinks - Phase 1
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
        /// Dọn dẹp HTML rác từ trình duyệt khi bôi đen thủ công trên web AI, đồng hóa kiểu chữ theo Word
        /// </summary>
        private enum TokenType
        {
            Text,
            StartTag,
            EndTag,
            Comment
        }

        private class HtmlToken
        {
            public TokenType Type { get; set; }
            public string Name { get; set; } = "";
            public string RawAttributes { get; set; } = "";
            public string Content { get; set; } = "";
        }

        private static System.Collections.Generic.List<HtmlToken> Tokenize(string html)
        {
            var tokens = new System.Collections.Generic.List<HtmlToken>();
            int i = 0;
            int len = html.Length;

            while (i < len)
            {
                if (html[i] == '<')
                {
                    // Check comment
                    if (i + 4 <= len && html.Substring(i, 4) == "<!--")
                    {
                        int endComment = html.IndexOf("-->", i + 4);
                        if (endComment != -1)
                        {
                            tokens.Add(new HtmlToken 
                            { 
                                Type = TokenType.Comment, 
                                Content = html.Substring(i, endComment + 3 - i) 
                            });
                            i = endComment + 3;
                            continue;
                        }
                    }

                    // Parse tag
                    int tagEnd = html.IndexOf('>', i);
                    if (tagEnd != -1)
                    {
                        string tagText = html.Substring(i + 1, tagEnd - i - 1).Trim();
                        
                        // Kiểm tra xem thẻ tag có bắt đầu bằng một ký tự hợp lệ hay không (chữ cái, '/', hoặc '!')
                        // nhằm tránh coi các biểu thức toán học hoặc code dạng "a < b" hoặc "< 5" là thẻ HTML.
                        bool isValidTag = tagText.Length > 0 && (char.IsLetter(tagText[0]) || tagText[0] == '/' || tagText[0] == '!');
                        
                        if (isValidTag)
                        {
                            i = tagEnd + 1;

                            if (tagText.StartsWith("/"))
                            {
                                string tagName = tagText.Substring(1).Trim().ToLower();
                                tokens.Add(new HtmlToken 
                                { 
                                    Type = TokenType.EndTag, 
                                    Name = tagName 
                                });
                            }
                            else
                            {
                                bool isSelfClosing = tagText.EndsWith("/");
                                if (isSelfClosing)
                                {
                                    tagText = tagText.Substring(0, tagText.Length - 1).Trim();
                                }

                                int spaceIdx = tagText.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' });
                                string name;
                                string attrs = "";
                                if (spaceIdx != -1)
                                {
                                    name = tagText.Substring(0, spaceIdx).ToLower();
                                    attrs = tagText.Substring(spaceIdx + 1);
                                }
                                else
                                {
                                    name = tagText.ToLower();
                                }

                                tokens.Add(new HtmlToken 
                                { 
                                    Type = TokenType.StartTag, 
                                    Name = name, 
                                    RawAttributes = attrs 
                                });

                                if (isSelfClosing)
                                {
                                    tokens.Add(new HtmlToken 
                                    { 
                                        Type = TokenType.EndTag, 
                                        Name = name 
                                    });
                                }
                            }
                            continue;
                        }
                    }
                }

                // Nếu không phải là thẻ Tag hoặc Comment hợp lệ, xử lý nó như Text thuần
                // Tìm ký tự '<' tiếp theo mà cấu thành một thẻ HTML hoặc Comment hợp lệ để cắt chuỗi text
                int searchStart = i + 1;
                int nextLt = -1;
                while (searchStart < len)
                {
                    int possibleLt = html.IndexOf('<', searchStart);
                    if (possibleLt == -1)
                    {
                        break;
                    }
                    
                    bool isNextValid = false;
                    if (possibleLt + 4 <= len && html.Substring(possibleLt, 4) == "<!--")
                    {
                        isNextValid = true;
                    }
                    else
                    {
                        int nextTagEnd = html.IndexOf('>', possibleLt);
                        if (nextTagEnd != -1)
                        {
                            string nextTagText = html.Substring(possibleLt + 1, nextTagEnd - possibleLt - 1).Trim();
                            isNextValid = nextTagText.Length > 0 && (char.IsLetter(nextTagText[0]) || nextTagText[0] == '/' || nextTagText[0] == '!');
                        }
                    }

                    if (isNextValid)
                    {
                        nextLt = possibleLt;
                        break;
                    }
                    else
                    {
                        searchStart = possibleLt + 1;
                    }
                }

                if (nextLt == -1)
                {
                    tokens.Add(new HtmlToken 
                    { 
                        Type = TokenType.Text, 
                        Content = html.Substring(i) 
                    });
                    break;
                }
                else
                {
                    tokens.Add(new HtmlToken 
                    { 
                        Type = TokenType.Text, 
                        Content = html.Substring(i, nextLt - i) 
                    });
                    i = nextLt;
                }
            }

            return tokens;
        }

        private static bool IsStyleBold(string style)
        {
            if (string.IsNullOrEmpty(style)) return false;
            var match = Regex.Match(style, @"\bfont-weight\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string val = match.Groups[1].Value.Trim().ToLower();
                if (val == "bold" || val == "bolder") return true;
                if (int.TryParse(val, out int weight))
                {
                    return weight >= 600;
                }
            }
            return false;
        }

        private static bool IsStyleHasNormalWeight(string style)
        {
            if (string.IsNullOrEmpty(style)) return false;
            var match = Regex.Match(style, @"\bfont-weight\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string val = match.Groups[1].Value.Trim().ToLower();
                if (val == "normal") return true;
                if (int.TryParse(val, out int weight))
                {
                    return weight < 600;
                }
            }
            return false;
        }

        private static bool IsStyleItalic(string style)
        {
            if (string.IsNullOrEmpty(style)) return false;
            var match = Regex.Match(style, @"\bfont-style\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string val = match.Groups[1].Value.Trim().ToLower();
                return val == "italic" || val == "oblique";
            }
            return false;
        }

        private static bool IsStyleHasNormalStyle(string style)
        {
            if (string.IsNullOrEmpty(style)) return false;
            var match = Regex.Match(style, @"\bfont-style\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string val = match.Groups[1].Value.Trim().ToLower();
                return val == "normal";
            }
            return false;
        }

        private static bool IsMathTag(string name, string attrs)
        {
            if (name == "math" || name == "annotation" || name == "semantics") return true;

            string @class = ExtractAttribute(attrs, "class").ToLowerInvariant();
            if (@class.Contains("katex") || @class.Contains("mathjax") || @class.StartsWith("mjx-") || @class.Contains("mjx-") || @class.Contains("latex"))
            {
                return true;
            }

            return false;
        }

        private class TagState
        {
            public string OriginalName { get; set; } = "";
            public bool IntroducedBold { get; set; }
            public bool IntroducedItalic { get; set; }
            public bool IsPreserved { get; set; }
            public string PreservedTagOutput { get; set; } = "";
        }

        /// <summary>
        /// Dọn dẹp HTML rác từ trình duyệt khi bôi đen thủ công trên web AI, đồng hóa kiểu chữ theo Word
        /// </summary>
        public static string CleanWebHtmlToOffice(string htmlFragment, WordFormatInfo formatInfo)
        {
            if (string.IsNullOrEmpty(htmlFragment)) return "";

            var tokens = Tokenize(htmlFragment);
            var sb = new StringBuilder();
            var stack = new System.Collections.Generic.Stack<TagState>();

            int activeBoldLayers = 0;
            int activeItalicLayers = 0;
            bool isInPre = false;
            bool isInInlineCode = false;
            bool isInMath = false;
            int mathDepth = 0;

            int parentLevel = formatInfo.ParentHeadingLevel;

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                if (isInMath)
                {
                    if (token.Type == TokenType.StartTag)
                    {
                        string selfClosingSuffix = token.RawAttributes.EndsWith("/") ? " /" : "";
                        string cleanAttrs = token.RawAttributes;
                        if (cleanAttrs.EndsWith("/")) cleanAttrs = cleanAttrs.Substring(0, cleanAttrs.Length - 1).Trim();
                        
                        sb.Append($"<{token.Name}");
                        if (!string.IsNullOrEmpty(cleanAttrs)) sb.Append($" {cleanAttrs}");
                        sb.Append($"{selfClosingSuffix}>");

                        // Do not increment depth for known self-closing tags or tags ending with "/"
                        bool isSelfClosingTag = token.Name == "br" || token.Name == "hr" || token.Name == "img" || 
                                                token.Name == "meta" || token.Name == "link" || token.Name == "col" || 
                                                token.Name == "wbr" || token.RawAttributes.EndsWith("/");
                        if (!isSelfClosingTag)
                        {
                            mathDepth++;
                        }
                    }
                    else if (token.Type == TokenType.EndTag)
                    {
                        sb.Append($"</{token.Name}>");
                        mathDepth--;
                        if (mathDepth == 0)
                        {
                            isInMath = false;
                        }
                    }
                    else if (token.Type == TokenType.Text)
                    {
                        sb.Append(token.Content);
                    }
                    continue;
                }

                if (token.Type == TokenType.Comment)
                {
                    sb.Append(token.Content);
                    continue;
                }

                if (token.Type == TokenType.Text)
                {
                    // Clean up LaTeX formulas in plain text blocks
                    sb.Append(ProcessLatexInHtml(token.Content, formatInfo));
                    continue;
                }

                if (token.Type == TokenType.StartTag)
                {
                    string name = token.Name;
                    string attrs = token.RawAttributes;

                    if (name == "col" || name == "colgroup" || name == "meta" || name == "title" || name == "link" || name == "style" || name == "script")
                    {
                        continue;
                    }

                    // Nhận dạng và chuyển dịch toàn bộ cây thẻ toán học MathJax/KaTeX sang HTML sạch hoặc MathML
                    if (IsMathTag(name, attrs))
                    {
                        // 1. Nếu đây là thẻ gốc math và dán vào Word, ta bảo tồn toàn bộ cây MathML gốc
                        if (name == "math" && formatInfo.IsWordTarget)
                        {
                            var mathContent = new StringBuilder();
                            mathContent.Append($"<{name}");
                            string cleanA = attrs;
                            if (cleanA.EndsWith("/")) cleanA = cleanA.Substring(0, cleanA.Length - 1).Trim();
                            if (!string.IsNullOrEmpty(cleanA)) mathContent.Append($" {cleanA}");
                            mathContent.Append(">");

                            int innerMathDepth = 1;
                            int k = i + 1;
                            while (k < tokens.Count && innerMathDepth > 0)
                            {
                                var t = tokens[k];
                                if (t.Type == TokenType.StartTag && t.Name == "math")
                                {
                                    if (!t.RawAttributes.EndsWith("/")) innerMathDepth++;
                                }
                                else if (t.Type == TokenType.EndTag && t.Name == "math")
                                {
                                    innerMathDepth--;
                                }

                                if (t.Type == TokenType.StartTag)
                                {
                                    string selfClosing = t.RawAttributes.EndsWith("/") ? " /" : "";
                                    string tAttrs = t.RawAttributes;
                                    if (tAttrs.EndsWith("/")) tAttrs = tAttrs.Substring(0, tAttrs.Length - 1).Trim();
                                    mathContent.Append($"<{t.Name}");
                                    if (!string.IsNullOrEmpty(tAttrs)) mathContent.Append($" {tAttrs}");
                                    mathContent.Append($"{selfClosing}>");
                                }
                                else if (t.Type == TokenType.EndTag)
                                {
                                    mathContent.Append($"</{t.Name}>");
                                }
                                else if (t.Type == TokenType.Text)
                                {
                                    mathContent.Append(t.Content);
                                }
                                k++;
                            }
                            sb.Append(mathContent.ToString());
                            i = k - 1;
                            continue;
                        }

                        // 2. Ngược lại, quét các token con để tìm nội dung LaTeX thô
                        string rawLatex = "";
                        int depth = 1;
                        int j = i + 1;

                        while (j < tokens.Count && depth > 0)
                        {
                            var t = tokens[j];
                            if (t.Type == TokenType.StartTag && t.Name == name)
                            {
                                if (!t.RawAttributes.EndsWith("/"))
                                {
                                    depth++;
                                }
                            }
                            else if (t.Type == TokenType.EndTag && t.Name == name)
                            {
                                depth--;
                            }

                            // Trích xuất từ annotation tag (KaTeX)
                            if (t.Type == TokenType.StartTag && t.Name == "annotation")
                            {
                                int k = j + 1;
                                var annotationContent = new StringBuilder();
                                while (k < tokens.Count && !(tokens[k].Type == TokenType.EndTag && tokens[k].Name == "annotation"))
                                {
                                    if (tokens[k].Type == TokenType.Text)
                                    {
                                        annotationContent.Append(tokens[k].Content);
                                    }
                                    k++;
                                }
                                rawLatex = annotationContent.ToString();
                            }
                            // Trích xuất từ MathJax script tag
                            else if (t.Type == TokenType.StartTag && t.Name == "script" && ExtractAttribute(t.RawAttributes, "type").Contains("math/tex"))
                            {
                                int k = j + 1;
                                var scriptContent = new StringBuilder();
                                while (k < tokens.Count && !(tokens[k].Type == TokenType.EndTag && tokens[k].Name == "script"))
                                {
                                    if (tokens[k].Type == TokenType.Text)
                                    {
                                        scriptContent.Append(tokens[k].Content);
                                    }
                                    k++;
                                }
                                rawLatex = scriptContent.ToString();
                            }

                            j++;
                        }

                        if (!string.IsNullOrEmpty(rawLatex))
                        {
                            string decoded = System.Net.WebUtility.HtmlDecode(rawLatex);
                            if (formatInfo.IsWordTarget)
                            {
                                string mathml = ConvertLatexToMathML(decoded);
                                if (!string.IsNullOrEmpty(mathml))
                                {
                                    sb.Append(mathml);
                                }
                                else
                                {
                                    sb.Append(TranslateLatexToHtml(decoded));
                                }
                            }
                            else
                            {
                                string translatedHtml = TranslateLatexToHtml(decoded);
                                sb.Append(translatedHtml);
                            }
                        }
                        else
                        {
                            // Nếu không trích xuất được LaTeX, chuyển sang chế độ bảo tồn (dự phòng)
                            isInMath = true;
                            mathDepth = 1;
                            
                            string selfClosingSuffix = attrs.EndsWith("/") ? " /" : "";
                            string cleanAttrs = attrs;
                            if (cleanAttrs.EndsWith("/")) cleanAttrs = cleanAttrs.Substring(0, cleanAttrs.Length - 1).Trim();
                            
                            sb.Append($"<{name}");
                            if (!string.IsNullOrEmpty(cleanAttrs)) sb.Append($" {cleanAttrs}");
                            sb.Append($"{selfClosingSuffix}>");
                        }

                        if (rawLatex != "")
                        {
                            // Nhảy qua toàn bộ các token con của thẻ math này
                            i = j - 1;
                        }
                        continue;
                    }

                    if (isInPre)
                    {
                        if (name == "br")
                        {
                            sb.Append("\n");
                        }
                        continue;
                    }
                    if (isInInlineCode)
                    {
                        continue;
                    }

                    if (name == "div")
                    {
                        string style = ExtractAttribute(attrs, "style");
                        bool isBold = IsStyleBold(style);
                        bool isItalic = IsStyleItalic(style);

                        if (isBold)
                        {
                            if (activeBoldLayers == 0) sb.Append("<strong>");
                            activeBoldLayers++;
                        }
                        if (isItalic)
                        {
                            if (activeItalicLayers == 0) sb.Append("<em>");
                            activeItalicLayers++;
                        }

                        stack.Push(new TagState
                        {
                            OriginalName = "div",
                            IsPreserved = false,
                            IntroducedBold = isBold,
                            IntroducedItalic = isItalic
                        });
                    }
                    else if (name == "p")
                    {
                        string pStyle = "";
                        if (formatInfo.IsFromActiveWord)
                        {
                            pStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
                        }
                        sb.Append($"<p class=\"MsoNormal\"{pStyle}>");
                        stack.Push(new TagState { OriginalName = "p", IsPreserved = true, PreservedTagOutput = "p" });
                    }
                    else if (name == "h1" || name == "h2" || name == "h3" || name == "h4" || name == "h5" || name == "h6")
                    {
                        int hLevel = int.Parse(name.Substring(1));
                        int targetLevel = Math.Min(9, parentLevel + hLevel);
                        string msoClass = $"MsoHeading{targetLevel}";
                        string headingStyle = "";
                        if (formatInfo.IsFromActiveWord)
                        {
                            headingStyle = $" style=\"font-family: '{formatInfo.FontName}';\"";
                        }
                        sb.Append($"<h{targetLevel} class=\"{msoClass}\"{headingStyle}>");
                        stack.Push(new TagState { OriginalName = name, IsPreserved = true, PreservedTagOutput = $"h{targetLevel}" });
                    }
                    else if (name == "ul" || name == "ol")
                    {
                        string listStyle = "";
                        if (formatInfo.IsFromActiveWord)
                        {
                            listStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
                        }
                        sb.Append($"<{name}{listStyle}>");
                        stack.Push(new TagState { OriginalName = name, IsPreserved = true, PreservedTagOutput = name });
                    }
                    else if (name == "li")
                    {
                        string liStyle = "";
                        if (formatInfo.IsFromActiveWord)
                        {
                            liStyle = $" style=\"font-family: '{formatInfo.FontName}'; font-size: {formatInfo.FontSize}pt;\"";
                        }
                        sb.Append($"<li{liStyle}>");
                        stack.Push(new TagState { OriginalName = "li", IsPreserved = true, PreservedTagOutput = "li" });
                    }
                    else if (name == "pre")
                    {
                        sb.Append("<div class=\"MsoNormal\" style=\"font-family: 'Consolas', 'Courier New', monospace; font-size: 10pt; background-color: #f8f9fa; border: 1px solid #e9ecef; padding: 8pt; margin: 6pt 0; white-space: pre; line-height: 1.2; color: #212529;\">");
                        isInPre = true;
                        stack.Push(new TagState { OriginalName = "pre", IsPreserved = true, PreservedTagOutput = "div" });
                    }
                    else if (name == "code")
                    {
                        sb.Append("<code style=\"font-family: 'Consolas', 'Courier New', monospace; font-size: 10pt; background-color: #f8f9fa; border: 1px solid #e9ecef; padding: 2px 4px; border-radius: 3px; color: #e83e8c;\">");
                        isInInlineCode = true;
                        stack.Push(new TagState { OriginalName = "code", IsPreserved = true, PreservedTagOutput = "code" });
                    }
                    else if (name == "table")
                    {
                        sb.Append("<table class=\"MsoNormalTable\" style=\"border-collapse: collapse; width: 100%; border: 1px solid #cccccc; margin: 6pt 0;\">");
                        stack.Push(new TagState { OriginalName = "table", IsPreserved = true, PreservedTagOutput = "table" });
                    }
                    else if (name == "tr")
                    {
                        sb.Append("<tr>");
                        stack.Push(new TagState { OriginalName = "tr", IsPreserved = true, PreservedTagOutput = "tr" });
                    }
                    else if (name == "th")
                    {
                        sb.Append(FormatTableCell("th", attrs, "border: 1px solid #cccccc; padding: 6px; background-color: #f2f2f2; font-weight: bold; text-align: left;"));
                        stack.Push(new TagState { OriginalName = "th", IsPreserved = true, PreservedTagOutput = "th" });
                    }
                    else if (name == "td")
                    {
                        sb.Append(FormatTableCell("td", attrs, "border: 1px solid #cccccc; padding: 6px; text-align: left;"));
                        stack.Push(new TagState { OriginalName = "td", IsPreserved = true, PreservedTagOutput = "td" });
                    }
                    else if (name == "thead" || name == "tbody")
                    {
                        sb.Append($"<{name}>");
                        stack.Push(new TagState { OriginalName = name, IsPreserved = true, PreservedTagOutput = name });
                    }
                    else if (name == "blockquote")
                    {
                        sb.Append("<blockquote style=\"border-left: 3px solid #cccccc; padding-left: 10pt; margin: 6pt 0 6pt 12pt; color: #555555; font-style: italic;\">");
                        stack.Push(new TagState { OriginalName = "blockquote", IsPreserved = true, PreservedTagOutput = "blockquote" });
                    }
                    else if (name == "a")
                    {
                        string href = ExtractAttribute(attrs, "href");
                        sb.Append($"<a href=\"{href}\" style=\"color: #0563c1; text-decoration: underline;\">");
                        stack.Push(new TagState { OriginalName = "a", IsPreserved = true, PreservedTagOutput = "a" });
                    }
                    else if (name == "br")
                    {
                        sb.Append("<br />");
                    }
                    else
                    {
                        string style = ExtractAttribute(attrs, "style");
                        
                        bool isBoldTag = (name == "b" || name == "strong");
                        bool isItalicTag = (name == "i" || name == "em");

                        bool styleBold = IsStyleBold(style);
                        bool styleNormalWeight = IsStyleHasNormalWeight(style);

                        bool styleItalic = IsStyleItalic(style);
                        bool styleNormalStyle = IsStyleHasNormalStyle(style);

                        bool shouldBeBold = (isBoldTag && !styleNormalWeight) || styleBold;
                        bool shouldBeItalic = (isItalicTag && !styleNormalStyle) || styleItalic;

                        if (shouldBeBold)
                        {
                            if (activeBoldLayers == 0) sb.Append("<strong>");
                            activeBoldLayers++;
                        }
                        if (shouldBeItalic)
                        {
                            if (activeItalicLayers == 0) sb.Append("<em>");
                            activeItalicLayers++;
                        }

                        stack.Push(new TagState
                        {
                            OriginalName = name,
                            IsPreserved = false,
                            IntroducedBold = shouldBeBold,
                            IntroducedItalic = shouldBeItalic
                        });
                    }
                }
                else if (token.Type == TokenType.EndTag)
                {
                    string name = token.Name;

                    if (name == "col" || name == "colgroup" || name == "meta" || name == "title" || name == "link" || name == "style" || name == "script")
                    {
                        continue;
                    }

                    if (isInPre)
                    {
                        if (name == "pre")
                        {
                            isInPre = false;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else if (isInInlineCode)
                    {
                        if (name == "code")
                        {
                            isInInlineCode = false;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    bool found = false;
                    var tempStack = new System.Collections.Generic.List<TagState>();

                    foreach (var state in stack)
                    {
                        tempStack.Add(state);
                        if (state.OriginalName == name)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        for (int k = 0; k < tempStack.Count; k++)
                        {
                            var state = stack.Pop();
                            if (state.IsPreserved)
                            {
                                sb.Append($"</{state.PreservedTagOutput}>");
                            }
                            if (state.IntroducedBold)
                            {
                                activeBoldLayers--;
                                if (activeBoldLayers == 0) sb.Append("</strong>");
                            }
                            if (state.IntroducedItalic)
                            {
                                activeItalicLayers--;
                                if (activeItalicLayers == 0) sb.Append("</em>");
                            }
                        }
                    }
                }
            }

            while (stack.Count > 0)
            {
                var state = stack.Pop();
                if (state.IsPreserved)
                {
                    sb.Append($"</{state.PreservedTagOutput}>");
                }
                if (state.IntroducedBold)
                {
                    activeBoldLayers--;
                    if (activeBoldLayers == 0) sb.Append("</strong>");
                }
                if (state.IntroducedItalic)
                {
                    activeItalicLayers--;
                    if (activeItalicLayers == 0) sb.Append("</em>");
                }
            }

            return sb.ToString();
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

            // Tính số lượng byte UTF-8 của các phần để tính offset chính xác
            // Chú ý: Offset phải tính bằng byte chứ không phải bằng độ dài chuỗi ký tự
            byte[] headerBytes = Encoding.UTF8.GetBytes(string.Format(HeaderTemplate, 0, 0, 0, 0));
            byte[] startHtmlBytes = Encoding.UTF8.GetBytes(StartHtml);
            byte[] fragmentBytes = Encoding.UTF8.GetBytes(htmlFragment);
            byte[] endHtmlBytes = Encoding.UTF8.GetBytes(EndHtml);

            int startHtmlOffset = headerBytes.Length;
            int startFragmentOffset = startHtmlOffset + startHtmlBytes.Length;
            int endFragmentOffset = startFragmentOffset + fragmentBytes.Length;
            int endHtmlOffset = endFragmentOffset + endHtmlBytes.Length;

            // Tạo lại header với offset chính xác
            string finalHeader = string.Format(HeaderTemplate, startHtmlOffset, endHtmlOffset, startFragmentOffset, endFragmentOffset);

            return finalHeader + StartHtml + htmlFragment + EndHtml;
        }

        private static string ExtractAttribute(string attributesString, string attributeName)
        {
            var match = Regex.Match(attributesString, $@"\b{attributeName}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value != "" ? match.Groups[1].Value : 
                       match.Groups[2].Value != "" ? match.Groups[2].Value : 
                       match.Groups[3].Value;
            }
            return string.Empty;
        }

        private static string FormatTableCell(string tag, string attributes, string defaultStyle)
        {
            string colspan = ExtractAttribute(attributes, "colspan");
            string rowspan = ExtractAttribute(attributes, "rowspan");
            
            // Trích xuất chiều rộng để bảo toàn cấu trúc cột của bảng trong Word Online / Word
            string width = ExtractAttribute(attributes, "width");
            if (string.IsNullOrEmpty(width))
            {
                string styleAttr = ExtractAttribute(attributes, "style");
                var widthMatch = Regex.Match(styleAttr, @"\bwidth\s*:\s*([^;'""]+)", RegexOptions.IgnoreCase);
                if (widthMatch.Success)
                {
                    width = widthMatch.Groups[1].Value.Trim();
                }
            }

            string alignValue = "left";
            if (attributes.Contains("text-align"))
            {
                var match = Regex.Match(attributes, @"text-align\s*:\s*([^;""']*)", RegexOptions.IgnoreCase);
                if (match.Success) alignValue = match.Groups[1].Value.Trim();
            }
            else
            {
                string alignAttr = ExtractAttribute(attributes, "align");
                if (!string.IsNullOrEmpty(alignAttr)) alignValue = alignAttr;
            }

            string extra = "";
            if (!string.IsNullOrEmpty(colspan)) extra += $" colspan=\"{colspan}\"";
            if (!string.IsNullOrEmpty(rowspan)) extra += $" rowspan=\"{rowspan}\"";

            string finalStyle = defaultStyle.Replace("text-align: left;", $"text-align: {alignValue};");
            
            if (!string.IsNullOrEmpty(width))
            {
                // Nếu width là số thuần túy, thêm đơn vị mặc định px
                if (double.TryParse(width, out _))
                {
                    width += "px";
                }
                finalStyle += $" width: {width};";
            }

            return $"<{tag} style=\"{finalStyle}\"{extra}>";
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

        private static bool ContainsLatexCommands(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("\\sin") || text.Contains("\\cos") || text.Contains("\\tan") || 
                   text.Contains("\\frac") || text.Contains("\\alpha") || text.Contains("\\beta") || 
                   text.Contains("\\gamma") || text.Contains("\\theta") || text.Contains("\\epsilon") ||
                   text.Contains("\\E");
        }

        private static string ParseFractions(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return "";

            // Find \frac{num}{den}
            int index;
            while ((index = latex.IndexOf("\\frac{")) != -1)
            {
                int numStart = index + 6;
                int numEnd = FindMatchingBrace(latex, numStart);
                if (numEnd == -1) break;

                string numerator = latex.Substring(numStart, numEnd - numStart);

                int denStart = numEnd + 1;
                while (denStart < latex.Length && char.IsWhiteSpace(latex[denStart]))
                {
                    denStart++;
                }

                if (denStart < latex.Length && latex[denStart] == '{')
                {
                    int denEnd = FindMatchingBrace(latex, denStart + 1);
                    if (denEnd == -1) break;

                    string denominator = latex.Substring(denStart + 1, denEnd - (denStart + 1));

                    // Recursively parse numerator and denominator
                    numerator = ParseFractions(numerator);
                    denominator = ParseFractions(denominator);

                    string replacement = $"({numerator}) / ({denominator})";
                    latex = latex.Substring(0, index) + replacement + latex.Substring(denEnd + 1);
                }
                else
                {
                    break;
                }
            }
            return latex;
        }

        private static int FindMatchingBrace(string str, int startIdx)
        {
            int depth = 1;
            for (int i = startIdx; i < str.Length; i++)
            {
                if (str[i] == '{') depth++;
                else if (str[i] == '}') depth--;

                if (depth == 0) return i;
            }
            return -1;
        }

        public static string ProcessLatexInHtml(string text, WordFormatInfo formatInfo)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string result = text;
            
            // Clean up math multiplication typos (e.g., 3 ∈ sin -> 3 · sin)
            result = Regex.Replace(result, @"(\d)\s*(?:\\in|∈|€)\s*([a-zA-Z])", "$1 · $2");

            // Replace block math $$...$$
            result = Regex.Replace(result, @"\$\$(.*?)\$\$", match => 
            {
                if (formatInfo.IsWordTarget)
                {
                    string mathml = ConvertLatexToMathML(match.Value);
                    if (!string.IsNullOrEmpty(mathml)) return mathml;
                }
                return TranslateLatexToHtml(match.Value);
            }, RegexOptions.Singleline);

            // Replace block math \[...\]
            result = Regex.Replace(result, @"\\\[(.*?)\\\]", match => 
            {
                if (formatInfo.IsWordTarget)
                {
                    string mathml = ConvertLatexToMathML(match.Value);
                    if (!string.IsNullOrEmpty(mathml)) return mathml;
                }
                return TranslateLatexToHtml(match.Value);
            }, RegexOptions.Singleline);

            // Replace inline math \(...\)
            result = Regex.Replace(result, @"\\\((.*?)\\\)", match => 
            {
                if (formatInfo.IsWordTarget)
                {
                    string mathml = ConvertLatexToMathML(match.Value);
                    if (!string.IsNullOrEmpty(mathml)) return mathml;
                }
                return TranslateLatexToHtml(match.Value);
            }, RegexOptions.Singleline);

            // Replace inline math $...$
            var inlineMathRegex = new Regex(@"\$([^$\n]+)\$");
            result = inlineMathRegex.Replace(result, match =>
            {
                string inner = match.Groups[1].Value;
                if (inner.Contains("\\") || inner.Contains("_") || inner.Contains("^") || inner.Contains("{") || inner.Contains("}") || inner.Contains("+") || inner.Contains("=") || inner.Contains("-") || inner.Contains("*") || inner.Contains("/"))
                {
                    if (formatInfo.IsWordTarget)
                    {
                        string mathml = ConvertLatexToMathML(match.Value);
                        if (!string.IsNullOrEmpty(mathml)) return mathml;
                    }
                    return TranslateLatexToHtml(match.Value);
                }
                return match.Value;
            });

            // Global fallback for formulas without wrappers
            if (result.Contains("\\") && ContainsLatexCommands(result))
            {
                if (formatInfo.IsWordTarget)
                {
                    string mathml = ConvertLatexToMathML(result);
                    if (!string.IsNullOrEmpty(mathml)) return mathml;
                }
                result = TranslateLatexToHtml(result);
            }

            return result;
        }

        public static string ProcessLatexInPlainText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string result = text;
            
            // Clean up math multiplication typos (e.g., 3 ∈ sin -> 3 · sin)
            result = Regex.Replace(result, @"(\d)\s*(?:\\in|∈|€)\s*([a-zA-Z])", "$1 · $2");

            // Replace block math $$...$$
            result = Regex.Replace(result, @"\$\$(.*?)\$\$", match => 
            {
                string translated = TranslateLatexToHtml(match.Value);
                return StripHtmlForPlainText(translated);
            }, RegexOptions.Singleline);

            // Replace block math \[...\]
            result = Regex.Replace(result, @"\\\[(.*?)\\\]", match => 
            {
                string translated = TranslateLatexToHtml(match.Value);
                return StripHtmlForPlainText(translated);
            }, RegexOptions.Singleline);

            // Replace inline math \(...\)
            result = Regex.Replace(result, @"\\\((.*?)\\\)", match => 
            {
                string translated = TranslateLatexToHtml(match.Value);
                return StripHtmlForPlainText(translated);
            }, RegexOptions.Singleline);

            // Replace inline math $...$
            var inlineMathRegex = new Regex(@"\$([^$\n]+)\$");
            result = inlineMathRegex.Replace(result, match =>
            {
                string inner = match.Groups[1].Value;
                if (inner.Contains("\\") || inner.Contains("_") || inner.Contains("^") || inner.Contains("{") || inner.Contains("}") || inner.Contains("+") || inner.Contains("=") || inner.Contains("-") || inner.Contains("*") || inner.Contains("/"))
                {
                    string translated = TranslateLatexToHtml(match.Value);
                    return StripHtmlForPlainText(translated);
                }
                return match.Value;
            });

            // Global fallback for formulas without wrappers
            if (result.Contains("\\") && ContainsLatexCommands(result))
            {
                string translated = TranslateLatexToHtml(result);
                result = StripHtmlForPlainText(translated);
            }

            return result;
        }

        private static string StripHtmlForPlainText(string html)
        {
            string result = html;
            result = result.Replace("<sub>0</sub>", "₀").Replace("<sub>1</sub>", "₁").Replace("<sub>2</sub>", "₂")
                           .Replace("<sub>3</sub>", "₃").Replace("<sub>4</sub>", "₄").Replace("<sub>5</sub>", "₅")
                           .Replace("<sub>6</sub>", "₆").Replace("<sub>7</sub>", "₇").Replace("<sub>8</sub>", "₈")
                           .Replace("<sub>9</sub>", "₉");
            result = result.Replace("<sup>0</sup>", "⁰").Replace("<sup>1</sup>", "¹").Replace("<sup>2</sup>", "²")
                           .Replace("<sup>3</sup>", "³").Replace("<sup>4</sup>", "⁴").Replace("<sup>5</sup>", "⁵")
                           .Replace("<sup>6</sup>", "⁶").Replace("<sup>7</sup>", "⁷").Replace("<sup>8</sup>", "⁸")
                           .Replace("<sup>9</sup>", "⁹");

            result = Regex.Replace(result, @"<[^>]+>", "");
            return result;
        }

        public static string TranslateLatexToHtml(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return "";

            string result = latex;

            // 1. Remove delimiters
            if (result.StartsWith("\\[") && result.EndsWith("\\]")) result = result.Substring(2, result.Length - 4);
            else if (result.StartsWith("\\(") && result.EndsWith("\\)")) result = result.Substring(2, result.Length - 4);
            else if (result.StartsWith("$$") && result.EndsWith("$$")) result = result.Substring(2, result.Length - 4);
            else if (result.StartsWith("$") && result.EndsWith("$")) result = result.Substring(1, result.Length - 2);

            result = result.Trim();

            // 1.5. Parse fractions before stripping braces
            result = ParseFractions(result);
            result = Regex.Replace(result, @"\\frac\s*(\d)\s*(\d)", "($1) / ($2)");
            result = Regex.Replace(result, @"\\frac\s*([a-zA-Z])\s*([a-zA-Z])", "($1) / ($2)");
            result = Regex.Replace(result, @"\\frac\s*\\([a-zA-Z]+)\s*\\([a-zA-Z]+)", "($1) / ($2)");

            // Translate custom operator \E to middot
            result = result.Replace("\\E", "·");
            result = Regex.Replace(result, @"(\d)\s*(?:\\in|∈|€)\s*([a-zA-Z])", "$1 · $2");

            // 2. Remove format wrappers
            result = Regex.Replace(result, @"\\text\{([^}]+)\}", "$1");
            result = Regex.Replace(result, @"\\mathrm\{([^}]+)\}", "$1");
            result = Regex.Replace(result, @"\\mathbf\{([^}]+)\}", "<b>$1</b>");
            result = Regex.Replace(result, @"\\mathit\{([^}]+)\}", "<i>$1</i>");
            result = Regex.Replace(result, @"\\underline\{([^}]+)\}", "<u>$1</u>");

            // 3. Translate Greek letters (lowercase)
            var greekLower = new System.Collections.Generic.Dictionary<string, string>
            {
                { "alpha", "α" }, { "beta", "β" }, { "gamma", "γ" }, { "delta", "δ" },
                { "epsilon", "ε" }, { "zeta", "ζ" }, { "eta", "η" }, { "theta", "θ" },
                { "iota", "ι" }, { "kappa", "κ" }, { "lambda", "λ" }, { "mu", "μ" },
                { "nu", "ν" }, { "xi", "ξ" }, { "pi", "π" }, { "rho", "ρ" },
                { "sigma", "σ" }, { "tau", "τ" }, { "upsilon", "υ" }, { "phi", "φ" },
                { "chi", "χ" }, { "psi", "ψ" }, { "omega", "ω" }
            };
            foreach (var kv in greekLower)
            {
                result = Regex.Replace(result, $@"\\{kv.Key}(?![a-zA-Z])", kv.Value);
            }

            // 4. Translate Greek letters (uppercase)
            var greekUpper = new System.Collections.Generic.Dictionary<string, string>
            {
                { "Delta", "Δ" }, { "Omega", "Ω" }, { "Theta", "Θ" }, { "Phi", "Φ" },
                { "Pi", "Π" }, { "Sigma", "Σ" }, { "Gamma", "Γ" }, { "Lambda", "Λ" },
                { "Psi", "Ψ" }, { "Xi", "Ξ" }
            };
            foreach (var kv in greekUpper)
            {
                result = Regex.Replace(result, $@"\\{kv.Key}(?![a-zA-Z])", kv.Value);
            }

            // 5. Translate math operators and symbols
            var mathSymbols = new System.Collections.Generic.Dictionary<string, string>
            {
                { "lvert", "|" }, { "rvert", "|" }, { "vert", "|" }, { "Vert", "||" },
                { "times", "×" }, { "div", "÷" }, { "pm", "±" }, { "approx", "≈" },
                { "infty", "∞" }, { "cdot", "·" }, { "to", "→" }, { "gets", "←" },
                { "partial", "∂" }, { "nabla", "∇" }, { "forall", "∀" }, { "exists", "∃" },
                { "emptyset", "∅" }, { "in", "∈" }, { "notin", "∉" }, { "ni", "∋" },
                { "prod", "∏" }, { "sum", "∑" }, { "minus", "−" }, { "plus", "+" },
                { "geq", "≥" }, { "leq", "≤" }, { "ge", "≥" }, { "le", "≤" }, { "neq", "≠" }
            };
            foreach (var kv in mathSymbols)
            {
                result = Regex.Replace(result, $@"\\{kv.Key}(?![a-zA-Z])", kv.Value);
            }

            // 6. Translate functions
            var mathFuncs = new string[] { "sin", "cos", "tan", "cot", "sec", "csc", "log", "ln", "lim", "exp", "arccos", "arcsin", "arctan" };
            foreach (var func in mathFuncs)
            {
                result = Regex.Replace(result, $@"\\{func}(?![a-zA-Z])", func);
            }

            // 7. Strip backslashes from remaining commands
            result = result.Replace("\\,", " ");
            result = result.Replace("\\;", " ");
            result = result.Replace("\\!", "");
            result = result.Replace("\\quad", "  ");
            result = result.Replace("\\qquad", "    ");
            result = result.Replace("\\\\", "<br />");

            // 8. Handle subscripts
            result = Regex.Replace(result, @"_\{([^}]+)\}", "<sub>$1</sub>");
            result = Regex.Replace(result, @"_([0-9a-zA-Z])", "<sub>$1</sub>");

            // 9. Handle superscripts
            result = Regex.Replace(result, @"\^\{([^}]+)\}", "<sup>$1</sup>");
            result = Regex.Replace(result, @"\^([0-9a-zA-Z])", "<sup>$1</sup>");

            // 10. Clean up curly brackets
            result = result.Replace("{", "").Replace("}", "");

            return result;
        }

        private class MathNode
        {
            public string Type { get; set; } = ""; // "mi", "mn", "mo", "mfrac", "msup", "msub", "mrow", "text"
            public string Value { get; set; } = "";
            public System.Collections.Generic.List<MathNode> Children { get; set; } = new System.Collections.Generic.List<MathNode>();

            public string ToMathML()
            {
                if (Type == "mfrac")
                {
                    string num = Children.Count > 0 ? Children[0].ToMathML() : "<mrow></mrow>";
                    string den = Children.Count > 1 ? Children[1].ToMathML() : "<mrow></mrow>";
                    return $"<mfrac><mrow>{num}</mrow><mrow>{den}</mrow></mfrac>";
                }
                if (Type == "msup")
                {
                    string baseNode = Children.Count > 0 ? Children[0].ToMathML() : "<mrow></mrow>";
                    string expNode = Children.Count > 1 ? Children[1].ToMathML() : "<mrow></mrow>";
                    return $"<msup>{baseNode}{expNode}</msup>";
                }
                if (Type == "msub")
                {
                    string baseNode = Children.Count > 0 ? Children[0].ToMathML() : "<mrow></mrow>";
                    string subNode = Children.Count > 1 ? Children[1].ToMathML() : "<mrow></mrow>";
                    return $"<msub>{baseNode}{subNode}</msub>";
                }
                if (Type == "mrow")
                {
                    var sb = new StringBuilder();
                    foreach (var child in Children) sb.Append(child.ToMathML());
                    return $"<mrow>{sb.ToString()}</mrow>";
                }
                if (Type == "mi" || Type == "mn" || Type == "mo")
                {
                    return $"<{Type}>{Value}</{Type}>";
                }
                return Value;
            }
        }

        private class MathToken
        {
            public string Type { get; set; } = ""; // "command", "char", "number", "operator", "open_brace", "close_brace", "sub", "super"
            public string Value { get; set; } = "";
        }

        private static System.Collections.Generic.List<MathToken> TokenizeLatex(string latex)
        {
            var tokens = new System.Collections.Generic.List<MathToken>();
            int i = 0;
            int len = latex.Length;

            while (i < len)
            {
                char c = latex[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '\\')
                {
                    int start = i;
                    i++;
                    if (i < len && !char.IsLetter(latex[i]))
                    {
                        tokens.Add(new MathToken { Type = "command", Value = latex.Substring(start, 2) });
                        i++;
                    }
                    else
                    {
                        while (i < len && char.IsLetter(latex[i]))
                        {
                            i++;
                        }
                        tokens.Add(new MathToken { Type = "command", Value = latex.Substring(start, i - start) });
                    }
                    continue;
                }

                if (c == '{')
                {
                    tokens.Add(new MathToken { Type = "open_brace", Value = "{" });
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    tokens.Add(new MathToken { Type = "close_brace", Value = "}" });
                    i++;
                    continue;
                }

                if (c == '^')
                {
                    tokens.Add(new MathToken { Type = "super", Value = "^" });
                    i++;
                    continue;
                }

                if (c == '_')
                {
                    tokens.Add(new MathToken { Type = "sub", Value = "_" });
                    i++;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < len && char.IsDigit(latex[i]))
                    {
                        i++;
                    }
                    tokens.Add(new MathToken { Type = "number", Value = latex.Substring(start, i - start) });
                    continue;
                }

                if (char.IsLetter(c))
                {
                    tokens.Add(new MathToken { Type = "char", Value = c.ToString() });
                    i++;
                    continue;
                }

                tokens.Add(new MathToken { Type = "operator", Value = c.ToString() });
                i++;
            }

            return tokens;
        }

        private static System.Collections.Generic.List<MathNode> ParseTokens(System.Collections.Generic.List<MathToken> tokens, ref int index)
        {
            var nodes = new System.Collections.Generic.List<MathNode>();

            while (index < tokens.Count)
            {
                var token = tokens[index];

                if (token.Type == "close_brace")
                {
                    break;
                }

                if (token.Type == "open_brace")
                {
                    index++;
                    var groupNodes = ParseTokens(tokens, ref index);
                    if (index < tokens.Count && tokens[index].Type == "close_brace")
                    {
                        index++;
                    }
                    var row = new MathNode { Type = "mrow" };
                    row.Children.AddRange(groupNodes);
                    nodes.Add(row);
                    continue;
                }

                if (token.Type == "command")
                {
                    if (token.Value == "\\frac")
                    {
                        index++;
                        var numNode = ParseSingleNode(tokens, ref index);
                        var denNode = ParseSingleNode(tokens, ref index);

                        var frac = new MathNode { Type = "mfrac" };
                        frac.Children.Add(numNode);
                        frac.Children.Add(denNode);
                        nodes.Add(frac);
                    }
                    else
                    {
                        string cmd = token.Value;
                        string val = cmd;
                        string type = "mi";

                        var greekLower = new System.Collections.Generic.Dictionary<string, string>
                        {
                            { "alpha", "α" }, { "beta", "β" }, { "gamma", "γ" }, { "delta", "δ" },
                            { "epsilon", "ε" }, { "zeta", "ζ" }, { "eta", "η" }, { "theta", "θ" },
                            { "iota", "ι" }, { "kappa", "κ" }, { "lambda", "λ" }, { "mu", "μ" },
                            { "nu", "ν" }, { "xi", "ξ" }, { "pi", "π" }, { "rho", "ρ" },
                            { "sigma", "σ" }, { "tau", "τ" }, { "upsilon", "υ" }, { "phi", "φ" },
                            { "chi", "χ" }, { "psi", "ψ" }, { "omega", "ω" }
                        };
                        var greekUpper = new System.Collections.Generic.Dictionary<string, string>
                        {
                            { "Delta", "Δ" }, { "Omega", "Ω" }, { "Theta", "Θ" }, { "Phi", "Φ" },
                            { "Pi", "Π" }, { "Sigma", "Σ" }, { "Gamma", "Γ" }, { "Lambda", "Λ" },
                            { "Psi", "Ψ" }, { "Xi", "Ξ" }
                        };
                        var mathSymbols = new System.Collections.Generic.Dictionary<string, string>
                        {
                            { "times", "×" }, { "div", "÷" }, { "pm", "±" }, { "approx", "≈" },
                            { "infty", "∞" }, { "cdot", "·" }, { "geq", "≥" }, { "leq", "≤" },
                            { "ge", "≥" }, { "le", "≤" }, { "neq", "≠" }
                        };

                        string cmdName = cmd.Substring(1);
                        if (greekLower.TryGetValue(cmdName, out var gl))
                        {
                            val = gl;
                            type = "mi";
                        }
                        else if (greekUpper.TryGetValue(cmdName, out var gu))
                        {
                            val = gu;
                            type = "mi";
                        }
                        else if (mathSymbols.TryGetValue(cmdName, out var ms))
                        {
                            val = ms;
                            type = "mo";
                        }
                        else if (cmdName == "sin" || cmdName == "cos" || cmdName == "tan" || cmdName == "log" || cmdName == "ln" || cmdName == "lim")
                        {
                            val = cmdName;
                            type = "mo";
                        }
                        else
                        {
                            val = cmdName;
                            type = "mi";
                        }

                        nodes.Add(new MathNode { Type = type, Value = val });
                        index++;
                    }
                    continue;
                }

                if (token.Type == "super" || token.Type == "sub")
                {
                    if (nodes.Count > 0)
                    {
                        var prev = nodes[nodes.Count - 1];
                        nodes.RemoveAt(nodes.Count - 1);

                        index++;
                        var expNode = ParseSingleNode(tokens, ref index);

                        var parent = new MathNode { Type = token.Type == "super" ? "msup" : "msub" };
                        parent.Children.Add(prev);
                        parent.Children.Add(expNode);
                        nodes.Add(parent);
                    }
                    else
                    {
                        var prev = new MathNode { Type = "mrow" };
                        index++;
                        var expNode = ParseSingleNode(tokens, ref index);

                        var parent = new MathNode { Type = token.Type == "super" ? "msup" : "msub" };
                        parent.Children.Add(prev);
                        parent.Children.Add(expNode);
                        nodes.Add(parent);
                    }
                    continue;
                }

                if (token.Type == "number")
                {
                    nodes.Add(new MathNode { Type = "mn", Value = token.Value });
                    index++;
                    continue;
                }

                if (token.Type == "char")
                {
                    nodes.Add(new MathNode { Type = "mi", Value = token.Value });
                    index++;
                    continue;
                }

                if (token.Type == "operator")
                {
                    nodes.Add(new MathNode { Type = "mo", Value = token.Value });
                    index++;
                    continue;
                }

                index++;
            }

            return nodes;
        }

        private static MathNode ParseSingleNode(System.Collections.Generic.List<MathToken> tokens, ref int index)
        {
            if (index >= tokens.Count) return new MathNode { Type = "mrow" };

            var token = tokens[index];
            if (token.Type == "open_brace")
            {
                index++;
                var groupNodes = ParseTokens(tokens, ref index);
                if (index < tokens.Count && tokens[index].Type == "close_brace")
                {
                    index++;
                }
                var row = new MathNode { Type = "mrow" };
                row.Children.AddRange(groupNodes);
                return row;
            }
            else
            {
                var nodes = ParseTokens(tokens, ref index);
                if (nodes.Count == 1) return nodes[0];
                var row = new MathNode { Type = "mrow" };
                row.Children.AddRange(nodes);
                return row;
            }
        }

        public static string ConvertLatexToMathML(string latex)
        {
            try
            {
                // Remove delimiters
                if (latex.StartsWith("\\[") && latex.EndsWith("\\]")) latex = latex.Substring(2, latex.Length - 4);
                else if (latex.StartsWith("\\(") && latex.EndsWith("\\)")) latex = latex.Substring(2, latex.Length - 4);
                else if (latex.StartsWith("$$") && latex.EndsWith("$$")) latex = latex.Substring(2, latex.Length - 4);
                else if (latex.StartsWith("$") && latex.EndsWith("$")) latex = latex.Substring(1, latex.Length - 2);

                latex = latex.Trim();

                // Clean up math multiplication typos (e.g., 3 ∈ sin -> 3 \cdot sin)
                latex = latex.Replace("\\E", "\\cdot");
                latex = Regex.Replace(latex, @"(\d)\s*(?:\\in|∈|€)\s*([a-zA-Z])", "$1 \\cdot $2");

                var tokens = TokenizeLatex(latex);
                int index = 0;
                var nodes = ParseTokens(tokens, ref index);
                var root = new MathNode { Type = "mrow" };
                root.Children.AddRange(nodes);
                return $"<math xmlns=\"http://www.w3.org/1998/Math/MathML\">{root.ToMathML()}</math>";
            }
            catch
            {
                return "";
            }
        }
    }
}
