using System;
using System.Text;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public static class LatexParser
    {
        private class MathToken
        {
            public string Type { get; set; } = ""; // "command", "char", "number", "operator", "open_brace", "close_brace", "sub", "super"
            public string Value { get; set; } = "";
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
    }
}
