using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HapiCopi
{
    public static class MermaidRenderer
    {
        struct BracketPair
        {
            public string Open;
            public string Close;
            public BracketPair(string open, string close) { Open = open; Close = close; }
        }

        private static readonly BracketPair[] Brackets = new BracketPair[]
        {
            new BracketPair("([", "])"), // Stadium
            new BracketPair("[(", ")]"), // Database
            new BracketPair("((", "))"), // Circle
            new BracketPair("[[", "]]"), // Subroutine
            new BracketPair("{{", "}}"), // Hexagon
            new BracketPair("[/", "/]"), // Parallelogram
            new BracketPair("[\\", "\\]"), // Parallelogram
            new BracketPair("[/", "\\]"), // Trapezoid
            new BracketPair("[\\", "/]"), // Trapezoid
            new BracketPair("[", "]"),   // Rectangle
            new BracketPair("(", ")"),   // Round
            new BracketPair("{", "}"),   // Rhombus
            new BracketPair(">", "]")    // Asymmetric
        };

        public static string SanitizeMermaidCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";

            // 1. Thay thế dấu nháy kép typographic/cong thành nháy thẳng
            code = code.Replace('“', '"')
                       .Replace('”', '"')
                       .Replace('‘', '\'')
                       .Replace('’', '\'');

            // 2. Sửa các mũi tên lỗi của AI trước khi phân tích
            code = code.Replace("==>>", "==>")
                       .Replace("-->>", "-->");

            // 3. Phân tích cú pháp để tìm và sửa nhãn của Node bằng thuật toán cân bằng ngoặc
            var nodeStartRegex = new Regex(@"\b([a-zA-Z0-9_-]+)(?=\s*(\(|\[|\{|>))", RegexOptions.Multiline);
            
            var matches = nodeStartRegex.Matches(code);
            for (int mIdx = matches.Count - 1; mIdx >= 0; mIdx--)
            {
                var match = matches[mIdx];
                string nodeId = match.Groups[1].Value;
                int nodeIdEndIndex = match.Index + match.Length;

                // Bỏ qua các từ khóa của Mermaid không phải nodeId
                if (nodeId == "graph" || nodeId == "flowchart" || nodeId == "subgraph" || 
                    nodeId == "direction" || nodeId == "end" || nodeId == "sequenceDiagram" ||
                    nodeId == "classDiagram" || nodeId == "stateDiagram" || nodeId == "erDiagram" ||
                    nodeId == "gantt" || nodeId == "pie" || nodeId == "gitGraph" ||
                    nodeId == "title" || nodeId == "style" || nodeId == "linkStyle" ||
                    nodeId == "classDef" || nodeId == "class" || nodeId == "click")
                {
                    continue;
                }

                // Tìm khoảng trắng trước dấu mở ngoặc
                int scanStart = nodeIdEndIndex;
                while (scanStart < code.Length && char.IsWhiteSpace(code[scanStart]))
                {
                    scanStart++;
                }

                if (scanStart >= code.Length) continue;

                BracketPair foundBracket = default;
                bool hasBracket = false;

                foreach (var bp in Brackets)
                {
                    if (scanStart + bp.Open.Length <= code.Length && 
                        code.Substring(scanStart, bp.Open.Length) == bp.Open)
                    {
                        foundBracket = bp;
                        hasBracket = true;
                        break;
                    }
                }

                if (!hasBracket) continue;

                int labelStartIndex = scanStart + foundBracket.Open.Length;
                
                int labelEndIndex = -1;
                int parenDepth = 0;
                int squareDepth = 0;
                int curlyDepth = 0;
                bool inQuotes = false;

                // Khởi tạo độ sâu ban đầu dựa trên bracket mở
                if (foundBracket.Open.Contains("(")) parenDepth += Regex.Matches(foundBracket.Open, @"\(").Count;
                if (foundBracket.Open.Contains("[")) squareDepth += Regex.Matches(foundBracket.Open, @"\[").Count;
                if (foundBracket.Open.Contains("{")) curlyDepth += Regex.Matches(foundBracket.Open, @"\{").Count;

                int i = labelStartIndex;
                while (i < code.Length)
                {
                    char c = code[i];

                    // Xử lý bỏ qua nội dung trong dấu nháy kép
                    if (c == '"' && (i == labelStartIndex || code[i - 1] != '\\'))
                    {
                        inQuotes = !inQuotes;
                        i++;
                        continue;
                    }

                    if (!inQuotes)
                    {
                        if (i + foundBracket.Close.Length <= code.Length && 
                            code.Substring(i, foundBracket.Close.Length) == foundBracket.Close)
                        {
                            int reqParen = Regex.Matches(foundBracket.Close, @"\)").Count;
                            int reqSquare = Regex.Matches(foundBracket.Close, @"\]").Count;
                            int reqCurly = Regex.Matches(foundBracket.Close, @"\}").Count;

                            if (parenDepth == reqParen && squareDepth == reqSquare && curlyDepth == reqCurly)
                            {
                                labelEndIndex = i;
                                break;
                            }
                        }

                        if (c == '(') parenDepth++;
                        else if (c == ')') parenDepth--;
                        else if (c == '[') squareDepth++;
                        else if (c == ']') squareDepth--;
                        else if (c == '{') curlyDepth++;
                        else if (c == '}') curlyDepth--;
                    }
                    i++;
                }

                if (labelEndIndex == -1) continue;

                string label = code.Substring(labelStartIndex, labelEndIndex - labelStartIndex);
                
                bool needsQuotes = label.Contains("(") || label.Contains(")") || 
                                   label.Contains("[") || label.Contains("]") ||
                                   label.Contains("{") || label.Contains("}") ||
                                   label.Contains("&") || label.Contains("<") || 
                                   label.Contains(">") || label.Contains(":") ||
                                   label.Trim().Contains(" ");
                                   
                if (needsQuotes && !label.Trim().StartsWith("\"") && !label.Trim().EndsWith("\""))
                {
                    // Escape các dấu nháy kép chưa được escape bên trong nhãn
                    string escapedLabel = Regex.Replace(label, @"(?<!\\)""", "\\\"");
                    label = $"\"{escapedLabel.Trim()}\"";
                }

                string before = code.Substring(0, scanStart);
                string after = code.Substring(labelEndIndex + foundBracket.Close.Length);
                code = before + foundBracket.Open + label + foundBracket.Close + after;
            }

            return code;
        }

        public static async Task<string> GetMermaidDataUriAsync(string code)
        {
            try
            {
                string cleanCode = SanitizeMermaidCode(code);
                
                // Encode sang Base64 dạng url-safe
                byte[] codeBytes = Encoding.UTF8.GetBytes(cleanCode);
                string base64 = Convert.ToBase64String(codeBytes)
                                    .Replace("+", "-")
                                    .Replace("/", "_")
                                    .Replace("=", "");

                string url = $"https://mermaid.ink/img/{base64}";
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    byte[] imageBytes = await client.GetByteArrayAsync(url);
                    
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        string imgBase64 = Convert.ToBase64String(imageBytes);
                        return $"data:image/png;base64,{imgBase64}";
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[MermaidRenderer LỖI] Không thể kết xuất Mermaid: {ex.Message}");
            }
            return "";
        }
    }
}
