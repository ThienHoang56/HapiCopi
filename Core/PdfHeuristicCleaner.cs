using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public sealed class PdfRepairResult
    {
        public string Plain { get; init; } = "";
        public string HtmlFragment { get; init; } = "";
    }

    /// <summary>
    /// Copy từ PDF: nối dòng bị bẻ, tách chữ dính, dựng lại heading/list/đoạn.
    /// Không đưa text qua Markdown — marked.js biến "1. Tiêu đề" thành list và phá cấu trúc.
    /// </summary>
    public static class PdfHeuristicCleaner
    {
        private static readonly string[] PdfProcessNames =
        {
            "acrord32", "acrobat", "acrobatdc", "adobeacrobat",
            "sumatrapdf", "foxitreader", "foxitpdfreader", "foxitphantompdf",
            "pdfxcview", "nitropdf", "pdf24"
        };

        private static readonly string[] HeadingPrefixes =
        {
            "chương", "phần", "mục", "điều", "bài", "tiêu đề", "chapter", "section", "appendix"
        };

        private static readonly Regex NumberedLine = new(
            @"^\s*(\d{1,2})[.)]\s*(.+)$", RegexOptions.Compiled);

        private static readonly Regex ListLine = new(
            @"^\s*[-–—*•]\s*(\S.*)$", RegexOptions.Compiled);

        private static readonly Regex GluedListLine = new(
            @"^\s*[-–—*•](\S.*)$", RegexOptions.Compiled);

        private static readonly Regex StepLine = new(
            @"^\s*step\s*(\d+)\s*[:.\-]?\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsLikelyPdfText(string text, string html) =>
            LooksLikePdfCopy(text, html, null);

        public static bool LooksLikePdfCopy(string text, string html, ActiveAppInfo? source)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (ClipboardHelper.IsVscodeEditorCopy()) return false;
            if (ClipboardSource.IsOfficeOrDocsHtml(html)) return false;

            if (source != null && IsPdfReader(source, text)) return true;
            if (LooksLikeNormalWebPage(html)) return false;
            if (HasNumberedSections(text)) return true;
            return HasPdfWrapShape(text);
        }

        public static string Repair(string text) => RepairStructured(text).Plain;

        public static PdfRepairResult RepairStructured(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new PdfRepairResult();
            }

            string work = AutoFormatter.FixPdfLigatures(text);
            work = work.Replace("\u00AD", "");
            work = work.Replace("\u2010", "-").Replace("\u2011", "-");
            work = work.Replace("\r\n", "\n").Replace('\r', '\n');

            var rawLines = new List<string>();
            foreach (string raw in work.Split('\n'))
            {
                string line = SplitStuckWords(CollapseHorizontalSpace(raw));
                if (AutoFormatter.IsPdfHeaderFooterOrPageNumber(line))
                {
                    continue;
                }

                rawLines.Add(line);
            }

            var blocks = new List<PdfBlock>();
            var body = new List<string>();

            void FlushBody()
            {
                if (body.Count == 0) return;
                string joined = AutoFormatter.CleanPunctuation(JoinWrappedLines(body).Trim());
                if (joined.Length > 0)
                {
                    blocks.Add(new PdfBlock { Kind = PdfKind.Paragraph, Text = joined });
                }

                body.Clear();
            }

            foreach (string line in rawLines)
            {
                if (line.Length == 0)
                {
                    FlushBody();
                    continue;
                }

                if (TryParseHeading(line, out string heading))
                {
                    FlushBody();
                    blocks.Add(new PdfBlock { Kind = PdfKind.Heading, Text = heading });
                    continue;
                }

                if (TryParseList(line, out string item))
                {
                    FlushBody();
                    blocks.Add(new PdfBlock { Kind = PdfKind.ListItem, Text = item });
                    continue;
                }

                if (TryParseStep(line, out string step))
                {
                    FlushBody();
                    blocks.Add(new PdfBlock { Kind = PdfKind.Step, Text = step });
                    continue;
                }

                if (LooksLikeTitle(line, blocks.Count == 0 && body.Count == 0))
                {
                    FlushBody();
                    blocks.Add(new PdfBlock { Kind = PdfKind.Title, Text = line });
                    continue;
                }

                if (LooksLikeFixedColumns(line))
                {
                    FlushBody();
                    blocks.Add(new PdfBlock { Kind = PdfKind.Paragraph, Text = line });
                    continue;
                }

                if (body.Count == 0 || ShouldJoin(body[^1], line))
                {
                    body.Add(line);
                }
                else
                {
                    FlushBody();
                    body.Add(line);
                }
            }

            FlushBody();
            PromoteLeadingTitles(blocks);

            return new PdfRepairResult
            {
                Plain = BuildPlain(blocks),
                HtmlFragment = BuildHtml(blocks)
            };
        }

        public static string SelfCheck()
        {
            string wrap = Repair("Trong nhiều hệ thống thực tế,\ndữ liệu không phải lúc nào cũng sạch.");
            if (!wrap.Contains("thực tế, dữ liệu")) return "wrap failed: " + wrap;

            string hyphen = Repair("infor-\nmation is missing");
            if (!hyphen.Contains("information is missing")) return "hyphen failed: " + hyphen;

            string camel = SplitStuckWords("ProductOverview UserManual Thistransmission");
            if (!camel.Contains("Product Overview") || !camel.Contains("User Manual") || !camel.Contains("This transmission"))
            {
                return "stuck words failed: " + camel;
            }

            PdfRepairResult structured = RepairStructured(
                "TransmissionSolenoidvalve\nUserManual\n1.ProductOverview\nThis valve is key.\n2.Specifications\n- Operating Voltage: DC 12V\n- Coil Resistance: 10 ohm");
            if (!structured.HtmlFragment.Contains("<h1>") || !structured.HtmlFragment.Contains("<h2>"))
            {
                return "heading html failed: " + structured.HtmlFragment;
            }

            if (!structured.HtmlFragment.Contains("<ul>") || !structured.HtmlFragment.Contains("<li>"))
            {
                return "list html failed: " + structured.HtmlFragment;
            }

            if (structured.HtmlFragment.Contains("<ol>"))
            {
                return "numbered heading became ol: " + structured.HtmlFragment;
            }

            if (!structured.Plain.Contains("Product Overview") || !structured.Plain.Contains("Operating Voltage"))
            {
                return "plain structure failed: " + structured.Plain;
            }

            return "";
        }

        private enum PdfKind { Title, Heading, Paragraph, ListItem, Step }

        private sealed class PdfBlock
        {
            public PdfKind Kind { get; init; }
            public string Text { get; init; } = "";
        }

        private static bool TryParseHeading(string line, out string heading)
        {
            heading = "";
            Match m = NumberedLine.Match(line);
            if (!m.Success) return false;

            string rest = m.Groups[2].Value.Trim();
            if (rest.Length == 0 || rest.Length > 90) return false;
            if (rest.EndsWith('.') || rest.EndsWith('?') || rest.EndsWith('!')) return false;

            heading = m.Groups[1].Value + ". " + rest;
            return true;
        }

        private static bool TryParseList(string line, out string item)
        {
            item = "";
            Match m = ListLine.Match(line);
            if (!m.Success) m = GluedListLine.Match(line);
            if (!m.Success) return false;
            item = m.Groups[1].Value.Trim();
            return item.Length > 0;
        }

        private static bool TryParseStep(string line, out string step)
        {
            step = "";
            Match m = StepLine.Match(line);
            if (!m.Success) return false;
            string rest = m.Groups[2].Value.Trim();
            step = rest.Length == 0
                ? "Step " + m.Groups[1].Value
                : "Step " + m.Groups[1].Value + " — " + rest;
            return true;
        }

        private static bool LooksLikeTitle(string line, bool atStart)
        {
            if (!atStart || line.Length >= 80) return false;
            if (line.EndsWith('.') || line.EndsWith('?') || line.EndsWith('!')) return false;
            string lower = line.ToLowerInvariant();
            if (HeadingPrefixes.Any(p => lower.StartsWith(p))) return true;

            int letters = 0, upper = 0;
            foreach (char c in line)
            {
                if (!char.IsLetter(c)) continue;
                letters++;
                if (char.IsUpper(c)) upper++;
            }

            return letters >= 4 && (double)upper / letters >= 0.35;
        }

        private static void PromoteLeadingTitles(List<PdfBlock> blocks)
        {
            int titles = 0;
            for (int i = 0; i < blocks.Count && titles < 2; i++)
            {
                if (blocks[i].Kind != PdfKind.Paragraph) break;
                if (blocks[i].Text.Length >= 80) break;
                if (blocks[i].Text.Contains(". ")) break;
                blocks[i] = new PdfBlock { Kind = PdfKind.Title, Text = blocks[i].Text };
                titles++;
            }
        }

        private static string BuildPlain(List<PdfBlock> blocks)
        {
            var sb = new StringBuilder();
            foreach (PdfBlock block in blocks)
            {
                switch (block.Kind)
                {
                    case PdfKind.Title:
                    case PdfKind.Heading:
                    case PdfKind.Step:
                    case PdfKind.Paragraph:
                        if (sb.Length > 0) sb.Append("\r\n\r\n");
                        sb.Append(block.Text);
                        break;
                    case PdfKind.ListItem:
                        if (sb.Length > 0) sb.Append("\r\n");
                        sb.Append("- ");
                        sb.Append(block.Text);
                        break;
                }
            }

            return sb.ToString();
        }

        private static string BuildHtml(List<PdfBlock> blocks)
        {
            var sb = new StringBuilder();
            bool inList = false;

            void CloseList()
            {
                if (!inList) return;
                sb.Append("</ul>");
                inList = false;
            }

            foreach (PdfBlock block in blocks)
            {
                string t = WebUtility.HtmlEncode(block.Text);
                switch (block.Kind)
                {
                    case PdfKind.Title:
                        CloseList();
                        sb.Append("<h1>").Append(t).Append("</h1>");
                        break;
                    case PdfKind.Heading:
                        CloseList();
                        sb.Append("<h2>").Append(t).Append("</h2>");
                        break;
                    case PdfKind.Step:
                        CloseList();
                        sb.Append("<h3>").Append(t).Append("</h3>");
                        break;
                    case PdfKind.Paragraph:
                        CloseList();
                        sb.Append("<p>").Append(t).Append("</p>");
                        break;
                    case PdfKind.ListItem:
                        if (!inList)
                        {
                            sb.Append("<ul>");
                            inList = true;
                        }

                        sb.Append("<li>").Append(t).Append("</li>");
                        break;
                }
            }

            CloseList();
            return sb.ToString();
        }

        internal static string SplitStuckWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string s = Regex.Replace(text, @"\b(This|That|These|Those|The|And|For|With|From|Into)([a-z])", "$1 $2");
            s = Regex.Replace(s, @"([a-z])([A-Z])", "$1 $2");
            s = Regex.Replace(s, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
            s = Regex.Replace(s, @"([A-Za-z])(\d)", "$1 $2");
            s = Regex.Replace(s, @"(\d)([A-Z])", "$1 $2");
            return s;
        }

        private static bool IsPdfReader(ActiveAppInfo app, string text)
        {
            string process = app.ProcessName ?? "";
            if (PdfProcessNames.Any(n => process.Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            bool browser = process.Equals("chrome", StringComparison.OrdinalIgnoreCase)
                || process.Equals("msedge", StringComparison.OrdinalIgnoreCase)
                || process.Equals("firefox", StringComparison.OrdinalIgnoreCase);
            if (!browser) return false;

            string title = app.WindowTitle ?? "";
            string url = app.BrowserUrl ?? "";
            if (title.IndexOf(".pdf", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf(".pdf", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            bool localFile = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
            return localFile && (HasNumberedSections(text) || HasPdfWrapShape(text));
        }

        private static bool HasNumberedSections(string text)
        {
            int n = 0;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (TryParseHeading(SplitStuckWords(line.Trim()), out _)) n++;
            }

            return n >= 2;
        }

        private static bool LooksLikeNormalWebPage(string html)
        {
            if (string.IsNullOrEmpty(html)) return false;
            if (html.IndexOf(".pdf", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (html.IndexOf("SourceURL:file:", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return html.IndexOf("SourceURL:", StringComparison.OrdinalIgnoreCase) >= 0
                && (html.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0
                    || html.IndexOf("class=", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasPdfWrapShape(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Select(l => l.TrimEnd())
                .ToArray();
            if (lines.Length < 4) return false;

            int wraps = 0;
            int candidates = 0;
            int totalChars = 0;
            int nonempty = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                nonempty++;
                totalChars += line.Length;
                if (i + 1 >= lines.Length) continue;
                string next = lines[i + 1].Trim();
                if (next.Length == 0) continue;
                candidates++;
                if (ShouldJoin(line, next)) wraps++;
            }

            if (nonempty < 4 || candidates == 0) return false;
            double avg = (double)totalChars / nonempty;
            if (avg < 28 || avg > 110) return false;
            return (double)wraps / candidates >= 0.32;
        }

        private static bool ShouldJoin(string previous, string next)
        {
            if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(next)) return false;
            if (TryParseHeading(next, out _) || TryParseList(next, out _) || TryParseStep(next, out _))
            {
                return false;
            }

            if (LooksLikeFixedColumns(next) || LooksLikeFixedColumns(previous))
            {
                return false;
            }

            if (EndsWithHyphenatedWord(previous)) return true;

            char last = LastNonSpace(previous);
            char first = next[0];

            if (IsSentenceEnd(last) && (char.IsUpper(first) || first == '"' || first == '“'))
            {
                return false;
            }

            if (last == ',' || last == ';' || last == ':' || last == '—' || last == '–')
            {
                return true;
            }

            if (char.IsLetterOrDigit(last) && (char.IsLower(first) || char.IsDigit(first)))
            {
                return true;
            }

            return false;
        }

        private static string JoinWrappedLines(List<string> lines)
        {
            var sb = new StringBuilder();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (sb.Length == 0)
                {
                    sb.Append(line);
                    continue;
                }

                if (EndsWithHyphenatedWord(sb.ToString()))
                {
                    sb.Length -= 1;
                    sb.Append(line);
                }
                else
                {
                    sb.Append(' ');
                    sb.Append(line);
                }
            }

            return sb.ToString();
        }

        private static bool EndsWithHyphenatedWord(string text) =>
            text.Length >= 2 && text[^1] == '-' && char.IsLetter(text[^2]);

        private static bool LooksLikeFixedColumns(string line) =>
            line.Contains('\t') || Regex.IsMatch(line, @"\S  +\S  +\S");

        private static bool IsSentenceEnd(char c) =>
            c == '.' || c == '!' || c == '?' || c == '…';

        private static char LastNonSpace(string text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(text[i])) return text[i];
            }

            return '\0';
        }

        private static string CollapseHorizontalSpace(string line) =>
            Regex.Replace(line ?? "", @"[ \t]+", " ").Trim();
    }
}
