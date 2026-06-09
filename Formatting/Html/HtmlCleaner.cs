using System;
using System.Text;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public static class HtmlCleaner
    {
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

        private class TagState
        {
            public string OriginalName { get; set; } = "";
            public bool IntroducedBold { get; set; }
            public bool IntroducedItalic { get; set; }
            public bool IsPreserved { get; set; }
            public string PreservedTagOutput { get; set; } = "";
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

        public static string FormatTableCell(string tag, string attributes, string defaultStyle)
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

        private static string ReplaceWithMermaidImage(string plainCode, string fallback, System.Collections.Generic.Dictionary<string, string> cache)
        {
            if (plainCode.StartsWith("graph ", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("gantt", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("pie", StringComparison.OrdinalIgnoreCase) ||
                plainCode.StartsWith("gitGraph", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in cache)
                {
                    string cachedClean = MermaidRenderer.SanitizeMermaidCode(kv.Key).Replace("\r", "").Trim();
                    string currentClean = MermaidRenderer.SanitizeMermaidCode(plainCode).Replace("\r", "").Trim();

                    if (cachedClean.Contains(currentClean) || currentClean.Contains(cachedClean) ||
                        kv.Key.Replace("\r", "").Trim().Contains(plainCode.Replace("\r", "").Trim()) ||
                        plainCode.Replace("\r", "").Trim().Contains(kv.Key.Replace("\r", "").Trim()))
                    {
                        if (!string.IsNullOrEmpty(kv.Value))
                        {
                            return $"<p><img src=\"{kv.Value}\" alt=\"Mermaid Diagram\" style=\"max-width: 100%; height: auto;\" /></p>";
                        }
                    }
                }
            }
            return fallback;
        }

        public static string CleanWebHtmlToOffice(string htmlFragment, WordFormatInfo formatInfo)
        {
            if (string.IsNullOrEmpty(htmlFragment)) return "";

            // Tiền xử lý thay thế biểu đồ Mermaid bằng ảnh
            if (formatInfo.MermaidImageCache != null && formatInfo.MermaidImageCache.Count > 0)
            {
                // 1. Khớp các khối <pre><code> chuẩn
                var preCodeRegex = new Regex(@"<pre[^>]*>(?:<code[^>]*>)?(.*?)(?:</code>)?</pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                htmlFragment = preCodeRegex.Replace(htmlFragment, match =>
                {
                    string inner = match.Groups[1].Value;
                    string decoded = System.Net.WebUtility.HtmlDecode(inner).Trim();
                    string plainCode = Regex.Replace(decoded, "<[^>]*>", "").Trim();
                    return ReplaceWithMermaidImage(plainCode, match.Value, formatInfo.MermaidImageCache);
                });

                // 2. Khớp các khối code dạng div (từ Monaco Editor / Antigravity IDE)
                var divMermaidRegex = new Regex(@"(?:<div>|<p[^>]*>)\s*```mermaid\s*(?:</div>|</p>)(.*?)(?:<div>|<p[^>]*>)\s*```\s*(?:</div>|</p>)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                htmlFragment = divMermaidRegex.Replace(htmlFragment, match =>
                {
                    string inner = match.Groups[1].Value;
                    string normalizedText = Regex.Replace(inner, @"<(?:div|p|br)[^>]*>", "\n");
                    string plainCode = Regex.Replace(normalizedText, "<[^>]*>", "").Trim();
                    plainCode = System.Net.WebUtility.HtmlDecode(plainCode).Trim();
                    return ReplaceWithMermaidImage(plainCode, match.Value, formatInfo.MermaidImageCache);
                });
            }

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
                                                token.Name == "meta" || token.Name == "link" || token.Name == "style" || 
                                                token.Name == "script" || token.Name == "col" || token.Name == "wbr" || 
                                                token.RawAttributes.EndsWith("/");
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
                    sb.Append(LatexParser.ProcessLatexInHtml(token.Content, formatInfo));
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
                                string mathml = LatexParser.ConvertLatexToMathML(decoded);
                                if (!string.IsNullOrEmpty(mathml))
                                {
                                    sb.Append(mathml);
                                }
                                else
                                {
                                    sb.Append(LatexParser.TranslateLatexToHtml(decoded));
                                }
                            }
                            else
                            {
                                string translatedHtml = LatexParser.TranslateLatexToHtml(decoded);
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

                        sb.Append("<div>");

                        stack.Push(new TagState
                        {
                            OriginalName = "div",
                            IsPreserved = true,
                            PreservedTagOutput = "div",
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
    }
}
