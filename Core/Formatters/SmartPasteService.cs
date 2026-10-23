using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace HapiCopi
{
    /// <summary>
    /// Dịch vụ Đũa phép Word (Tree-based Rule Profile Mode).
    /// Trích xuất Cây cấu trúc tài liệu Word (Document Structure Tree) gồm:
    /// - Cây Tiêu đề (Headings H1-H6)
    /// - Nhánh Thân bài (Normal Paragraphs)
    /// - Nhánh Danh sách Đa cấp (Lists Level 1-5)
    /// - Ngữ cảnh Điểm dán (Active Insertion Context)
    ///
    /// Khi chế độ BẬT, dữ liệu copy được tự động hòa tan theo Cây cấu trúc ngay tại thời điểm Copy,
    /// sẵn sàng trên Clipboard để dán vào Word tức thì (Zero-latency) mà không làm vỡ cấu trúc tài liệu.
    /// </summary>
    public static class SmartPasteService
    {
        public static bool IsMagicWandActive { get; private set; } = false;
        public static bool IsGoogleDocsActive { get; private set; } = false;
        public static int GoogleDocsHeadingOffset { get; set; } = 0;
        public static DocumentStructureTree? ActiveStructureTree { get; private set; } = null;
        public static string? ActiveDocumentTitle => IsGoogleDocsActive ? "Google Docs" : ActiveStructureTree?.DocumentTitle;

        // Giữ tương thích ngược cho các nơi kiểm tra ActiveDocumentRules
        public static WordFormatInfo? ActiveDocumentRules => ActiveStructureTree != null
            ? new WordFormatInfo
            {
                FontName = ActiveStructureTree.BodyStyle.FontName,
                FontSize = ActiveStructureTree.BodyStyle.FontSize,
                Alignment = ActiveStructureTree.BodyStyle.Alignment,
                FirstLineIndent = ActiveStructureTree.BodyStyle.FirstLineIndent,
                LineSpacing = ActiveStructureTree.BodyStyle.LineSpacing,
                IsInList = ActiveStructureTree.Context.IsWithinList,
                ListBulletChar = ActiveStructureTree.Lists.TryGetValue(1, out var l) ? l.BulletOrPrefix : "-"
            }
            : null;

        public static event Action<bool>? StateChanged;

        /// <summary>
        /// Đồng bộ trạng thái từ StyleSyncService sang SmartPasteService.
        /// </summary>
        public static void SyncFromStyleSync(DocumentStructureTree? tree, bool active)
        {
            ActiveStructureTree = tree;
            IsMagicWandActive = active && tree != null;
            IsGoogleDocsActive = false;
            StateChanged?.Invoke(IsMagicWandActive);
        }

        /// <summary>
        /// Bật hoặc Tắt chế độ Đũa phép (Ctrl + Alt + W).
        /// Hỗ trợ cả Microsoft Word và Google Docs (tự động phân nhánh theo cửa sổ active).
        /// </summary>
        public static async Task ToggleMagicWandAsync(Action<string, string>? notifyAction = null)
        {
            Point cursorPos = Cursor.Position;
            ActiveAppInfo activeApp = ActiveWindowHelper.GetActiveAppInfo();

            // 1. Phân nhánh nếu đang ở Google Docs
            if (activeApp.IsGoogleDocs)
            {
                if (!IsMagicWandActive || !IsGoogleDocsActive)
                {
                    IsMagicWandActive = true;
                    IsGoogleDocsActive = true;
                    GoogleDocsHeadingOffset = 0;
                    StateChanged?.Invoke(true);

                    MagicWandIndicatorForm.ShowNearCursor(cursorPos);
                    MagicWandIndicatorForm.NotifySuccess();

                    notifyAction?.Invoke("Đã bật StyleSync Google Docs!",
                        "• Tự động đồng hóa theo Named Styles của Google Docs\n" +
                        "• Tịnh tiến cấp độ: Giữ nguyên (H1 -> H1)\n" +
                        "(Bấm tiếp phím tắt để đổi mức tịnh tiến: +1, +2, +3)");
                    Program.Log("[StyleSync] Đã bật chế độ Google Docs Semantic Mode (Offset=0).");
                }
                else
                {
                    // Đang bật ở Docs: Chuyển luân phiên mức tịnh tiến đề mục: 0 -> 1 -> 2 -> 3 -> Tắt
                    GoogleDocsHeadingOffset = (GoogleDocsHeadingOffset + 1) % 4;

                    if (GoogleDocsHeadingOffset == 0)
                    {
                        IsMagicWandActive = false;
                        IsGoogleDocsActive = false;
                        StateChanged?.Invoke(false);

                        MagicWandIndicatorForm.NotifyDeactivated();
                        notifyAction?.Invoke("Đã tắt StyleSync Google Docs", "Trở về chế độ dán thông thường.");
                        Program.Log("[StyleSync] Người dùng đã tắt StyleSync Google Docs.");
                    }
                    else
                    {
                        MagicWandIndicatorForm.ShowNearCursor(cursorPos);
                        MagicWandIndicatorForm.NotifySuccess();

                        notifyAction?.Invoke("Đổi mức Tịnh tiến Đề mục Docs!",
                            $"• Cấp độ mới: Mức +{GoogleDocsHeadingOffset}\n" +
                            $"• Ví dụ: H1 -> H{1 + GoogleDocsHeadingOffset}, H2 -> H{2 + GoogleDocsHeadingOffset}\n" +
                            $"(Nội dung AI sẽ tự động biến thành mục con/cháu).");
                        Program.Log($"[StyleSync] Đổi mức tịnh tiến đề mục Google Docs thành +{GoogleDocsHeadingOffset}.");
                    }
                }
                return;
            }

            // 2. Phân nhánh cho Word Desktop
            if (IsMagicWandActive)
            {
                // TẮT CHẾ ĐỘ
                IsMagicWandActive = false;
                IsGoogleDocsActive = false;
                ActiveStructureTree = null;
                StateChanged?.Invoke(false);

                MagicWandIndicatorForm.NotifyDeactivated();
                notifyAction?.Invoke("Đã tắt StyleSync (Ctrl+Alt+S)", "Trở về chế độ dán HapiCopi thông thường.");
                Program.Log("[StyleSync] Người dùng đã tắt chế độ StyleSync.");
                return;
            }

            // BẬT CHẾ ĐỘ CHO WORD
            MagicWandIndicatorForm.ShowNearCursor(cursorPos);
            Program.Log($"[MagicWand] Bắt đầu trích xuất Cây cấu trúc tài liệu ngầm tại ({cursorPos.X}, {cursorPos.Y})...");

            var tree = await WordInterop.ScanDocumentStructureTreeAsync(3000);

            ActiveStructureTree = tree;
            IsMagicWandActive = true;
            IsGoogleDocsActive = false;
            StateChanged?.Invoke(true);

            MagicWandIndicatorForm.NotifySuccess();

            string alignName = tree.BodyStyle.Alignment switch
            {
                1 => "Center (Căn giữa)",
                2 => "Right (Căn phải)",
                3 => "Justify (Đều 2 bên)",
                _ => "Left (Căn trái)"
            };
            string indentText = tree.BodyStyle.FirstLineIndent > 0 ? $"{tree.BodyStyle.FirstLineIndent:0.#}pt ({tree.BodyStyle.FirstLineIndent / 28.35f:0.##}cm)" : "0pt (Không thụt)";
            string bulletText = tree.Lists.TryGetValue(1, out var l1) ? l1.BulletOrPrefix : "-";
            string parentHeading = !string.IsNullOrEmpty(tree.Context.ParentHeadingTitle) ? $"\n• Mục hiện tại: {tree.Context.ParentHeadingTitle}" : "";

            notifyAction?.Invoke("Đã nạp Cây cấu trúc Word!",
                $"• Thân bài: {tree.BodyStyle.FontName} ({tree.BodyStyle.FontSize:0.#}pt) - {alignName}\n" +
                $"• Thụt lề: {indentText}\n" +
                $"• Danh sách: '{bulletText}'" +
                parentHeading + "\n" +
                "Mọi lần copy sẽ tự động đồng bộ theo Cây cấu trúc tài liệu.");

            Program.Log($"[MagicWand] Nạp Cây thành công: Font={tree.BodyStyle.FontName}, Size={tree.BodyStyle.FontSize}, Indent={tree.BodyStyle.FirstLineIndent}, Align={tree.BodyStyle.Alignment}");
        }

        /// <summary>
        /// Xử lý định dạng nội dung Clipboard theo Cây cấu trúc tài liệu đã nạp.
        /// Chạy ngay khi Copy (Zero-Latency khi Dán).
        /// </summary>
        public static async Task<bool> FormatClipboardWithActiveRulesAsync(
            string textToProcess,
            string htmlToProcess,
            Dictionary<string, string>? diagramCache)
        {
            if (!IsMagicWandActive) return false;

            // Xử lý riêng nếu đang ở Google Docs
            if (IsGoogleDocsActive)
            {
                string docsHtml = await GoogleDocsSemanticAdapter.ConvertMarkdownToGoogleDocsClipboardAsync(textToProcess ?? "", GoogleDocsHeadingOffset);
                string plainText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(textToProcess ?? "");
                FormulaTracker.ScanAndRegisterHtml(docsHtml);
                ClipboardHelper.SetOfficeClipboard(plainText, docsHtml);
                Program.Log($"[SmartPaste] Đã đóng gói Clipboard ngữ nghĩa cho Google Docs (Offset=+{GoogleDocsHeadingOffset}).");
                return true;
            }

            if (ActiveStructureTree == null) return false;

            var tree = ActiveStructureTree;
            string officeHtml;

            bool hasMermaid = (!string.IsNullOrEmpty(textToProcess) && KrokiRenderer.ContainsAnyDiagram(textToProcess)) ||
                              KrokiRenderer.ContainsAnyDiagramHtml(htmlToProcess);

            // 1. Nếu có HTML phong phú từ web (trừ Mermaid)
            if (!string.IsNullOrEmpty(htmlToProcess) && !hasMermaid)
            {
                string htmlFragment = MarkdownToWordConverter.ExtractHtmlFragment(htmlToProcess);
                var legacyInfo = new WordFormatInfo
                {
                    FontName = tree.BodyStyle.FontName,
                    FontSize = tree.BodyStyle.FontSize,
                    LineSpacing = tree.BodyStyle.LineSpacing,
                    Alignment = tree.BodyStyle.Alignment,
                    FirstLineIndent = tree.BodyStyle.FirstLineIndent,
                    IsWordTarget = true,
                    MermaidImageCache = diagramCache ?? new Dictionary<string, string>()
                };
                string cleanFragment = await MarkdownToWordConverter.CleanWebHtmlToOfficeAsync(htmlFragment, legacyInfo);
                string transformedFragment = TransformHtmlWithTree(cleanFragment, tree);
                officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(transformedFragment);
            }
            // 2. Nếu là văn bản Markdown hoặc Plain text
            else if (!string.IsNullOrWhiteSpace(textToProcess))
            {
                var legacyInfo = new WordFormatInfo
                {
                    FontName = tree.BodyStyle.FontName,
                    FontSize = tree.BodyStyle.FontSize,
                    LineSpacing = tree.BodyStyle.LineSpacing,
                    Alignment = tree.BodyStyle.Alignment,
                    FirstLineIndent = tree.BodyStyle.FirstLineIndent,
                    ParentHeadingLevel = tree.Context.ParentHeadingLevel,
                    IsWordTarget = true,
                    MermaidImageCache = diagramCache ?? new Dictionary<string, string>()
                };

                // Nếu văn bản có chứa Math ($ or $$) hoặc Mermaid hoặc Markdown bảng:
                // Sử dụng pipeline chuyển đổi WebView2 để sinh mã OMML / MathML chuẩn Word
                bool hasMathOrComplex = textToProcess.Contains('$') || textToProcess.Contains("```") || textToProcess.Contains('|');
                if (hasMathOrComplex)
                {
                    try
                    {
                        string fullOfficeHtml = await MarkdownToWordConverter.ConvertToOfficeHtmlAsync(textToProcess, legacyInfo);
                        string htmlFrag = MarkdownToWordConverter.ExtractHtmlFragment(fullOfficeHtml);
                        string transformedFragment = TransformHtmlWithTree(htmlFrag, tree);
                        officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(transformedFragment);
                    }
                    catch
                    {
                        string transformedFragment = TransformMarkdownAstToOfficeHtml(textToProcess, tree);
                        officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(transformedFragment);
                    }
                }
                else
                {
                    string transformedFragment = TransformMarkdownAstToOfficeHtml(textToProcess, tree);
                    officeHtml = MarkdownToWordConverter.PackageAsClipboardHtml(transformedFragment);
                }
            }
            else
            {
                return false;
            }

            string cleanPlainText = await MarkdownToWordConverter.ProcessLatexInPlainTextAsync(textToProcess ?? "");
            FormulaTracker.ScanAndRegisterHtml(officeHtml);
            ClipboardHelper.SetOfficeClipboard(cleanPlainText, officeHtml);
            return true;
        }

        /// <summary>
        /// Chuyển đổi văn bản Markdown/Plain text sang HTML Word dựa trên Cây cú pháp AST (Tree-to-Tree Mapping).
        /// Từng loại Node tự động rót vào đúng Khuôn của Cây tài liệu Word:
        /// - HeadingBlock -> HeadingHierarchy (không thụt lề, đúng font & cỡ chữ)
        /// - ParagraphBlock -> BodyStyle (bảo toàn bold, italic, code, links)
        /// - ListBlock -> ListHierarchy
        /// - FencedCodeBlock -> Monospace Code Block
        /// </summary>
        public static string TransformMarkdownAstToOfficeHtml(string text, DocumentStructureTree tree)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Bước quan trọng: nhận diện "4.5. Research Objectives" (không có dấu #)
            // trước khi Markdig hiểu nhầm thành ordered list, đồng thời đồng bộ cấp + số đề mục.
            string normalized = OutlineStructureService.NormalizePlainTextOutline(text, tree);

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            MarkdownDocument document = Markdown.Parse(normalized, pipeline);
            var sb = new StringBuilder();

            foreach (var block in document)
            {
                if (block is HeadingBlock heading)
                {
                    int level = Math.Clamp(heading.Level, 1, 6);
                    string headingHtml = RenderInlineToOfficeHtml(heading.Inline);
                    sb.Append(OutlineStructureService.BuildHeadingHtml(level, headingHtml, tree));
                }
                else if (block is ParagraphBlock paragraph)
                {
                    string paraHtml = RenderInlineToOfficeHtml(paragraph.Inline);
                    if (string.IsNullOrWhiteSpace(paraHtml)) continue;

                    sb.Append($"<p class=\"MsoNormal\" style=\"{tree.BodyStyle.ToCssStyle()}\">{paraHtml}</p>");
                }
                else if (block is ListBlock listBlock)
                {
                    RenderListBlock(listBlock, 1, tree, sb);
                }
                else if (block is FencedCodeBlock codeBlock)
                {
                    var codeBuilder = new StringBuilder();
                    foreach (var line in codeBlock.Lines)
                    {
                        codeBuilder.AppendLine(line.ToString());
                    }

                    string codeContent = codeBuilder.ToString().TrimEnd();
                    sb.Append("<div style=\"background: #f8f9fa; border: 1px solid #e2e8f0; border-radius: 4px; padding: 10px 14px; margin: 8pt 0; text-align: left; text-indent: 0pt;\">" +
                              $"<pre style=\"margin: 0; font-family: Consolas, 'Courier New', monospace; font-size: 10pt; line-height: 1.3;\">{System.Net.WebUtility.HtmlEncode(codeContent)}</pre>" +
                              "</div>");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Rót ListBlock (kể cả danh sách lồng nhau) vào đúng khuôn bullet/numbering
        /// đã đo từ tài liệu đích: marker, lề trái, hanging indent, khoảng cách.
        /// </summary>
        private static void RenderListBlock(
            ListBlock listBlock,
            int level,
            DocumentStructureTree tree,
            StringBuilder sb)
        {
            bool ordered = listBlock.IsOrdered;
            int itemIndex = 1;
            if (ordered && int.TryParse(listBlock.OrderedStart, out int start) && start > 0)
            {
                itemIndex = start;
            }

            foreach (var item in listBlock)
            {
                if (item is not ListItemBlock listItem) continue;

                var inlineHtml = new StringBuilder();
                var nestedLists = new List<ListBlock>();

                foreach (var subBlock in listItem)
                {
                    if (subBlock is ParagraphBlock itemPara)
                    {
                        if (inlineHtml.Length > 0) inlineHtml.Append("<br/>");
                        inlineHtml.Append(RenderInlineToOfficeHtml(itemPara.Inline));
                    }
                    else if (subBlock is ListBlock nested)
                    {
                        nestedLists.Add(nested);
                    }
                }

                string content = inlineHtml.ToString().Trim();
                if (content.Length > 0)
                {
                    sb.Append(OutlineStructureService.BuildListParagraphHtml(
                        content,
                        level,
                        ordered,
                        itemIndex,
                        tree));
                }

                foreach (var nested in nestedLists)
                {
                    RenderListBlock(nested, level + 1, tree, sb);
                }

                itemIndex++;
            }
        }

        private static string RenderInlineToOfficeHtml(ContainerInline? container)
        {
            if (container == null) return "";
            var sb = new StringBuilder();
            foreach (var inline in container)
            {
                if (inline is LiteralInline literal)
                {
                    sb.Append(System.Net.WebUtility.HtmlEncode(literal.Content.ToString()));
                }
                else if (inline is EmphasisInline emphasis)
                {
                    string tag = emphasis.DelimiterCount == 2 ? "strong" : "em";
                    sb.Append($"<{tag}>");
                    sb.Append(RenderInlineToOfficeHtml(emphasis));
                    sb.Append($"</{tag}>");
                }
                else if (inline is CodeInline code)
                {
                    sb.Append($"<code style=\"font-family: Consolas, monospace; font-size: 9.5pt; background: #f1f5f9; padding: 2px 4px; border-radius: 3px;\">{System.Net.WebUtility.HtmlEncode(code.Content)}</code>");
                }
                else if (inline is LinkInline link)
                {
                    sb.Append($"<a href=\"{System.Net.WebUtility.HtmlEncode(link.Url)}\" style=\"color: #0563c1; text-decoration: underline;\">");
                    sb.Append(RenderInlineToOfficeHtml(link));
                    sb.Append("</a>");
                }
                else if (inline is LineBreakInline)
                {
                    sb.Append("<br/>");
                }
                else if (inline is ContainerInline childContainer)
                {
                    sb.Append(RenderInlineToOfficeHtml(childContainer));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Ánh xạ cây thẻ HTML từ web nguồn vào đúng Cây cấu trúc Word:
        /// - Thẻ h1-h6 -> HeadingHierarchy (không có text-indent)
        /// - Thẻ p -> BodyStyle
        /// - Thẻ li -> ListHierarchy
        /// - Thẻ pre/code -> Giữ nguyên Monospace
        /// </summary>
        public static string TransformHtmlWithTree(
            string htmlFragment,
            DocumentStructureTree tree,
            int parentHeadingLevel = 0)
        {
            if (string.IsNullOrWhiteSpace(htmlFragment)) return htmlFragment;

            // 1. Nâng cấp các <p> dạng "4.5. Tên mục" thành Heading đúng cấp,
            //    ánh xạ độ sâu số đề mục và đánh số lại theo sườn tài liệu.
            string processed = OutlineStructureService.PromoteAndStyleHeadings(
                htmlFragment,
                tree,
                parentHeadingLevel);

            // 2. Word không tôn trọng CSS list-style khi dán clipboard.
            //    Rót <ul>/<ol> vào đúng khuôn marker/lề/hanging của tài liệu.
            processed = OutlineStructureService.ConvertHtmlListsToParagraphs(processed, tree);

            // 3. Ánh xạ các đoạn văn thân bài còn lại.
            string paragraphSource = processed;
            int lastParagraphIndex = 0;
            int tableDepth = 0;

            processed = Regex.Replace(paragraphSource, @"<p(\s+[^>]*)?>(.*?)</p>", m =>
            {
                string gap = paragraphSource.Substring(lastParagraphIndex, Math.Max(0, m.Index - lastParagraphIndex));
                tableDepth += Regex.Matches(gap, @"<table\b", RegexOptions.IgnoreCase).Count;
                tableDepth -= Regex.Matches(gap, @"</table\s*>", RegexOptions.IgnoreCase).Count;
                if (tableDepth < 0) tableDepth = 0;
                lastParagraphIndex = m.Index + m.Length;

                string attrs = m.Groups[1].Value;
                string inner = m.Groups[2].Value;

                // Bỏ qua nếu là công thức toán dạng khối hoặc ảnh hoặc comment
                if (attrs.Contains("data-xpm-math-type=\"block\"") || attrs.Contains("latex-math-block") || attrs.Contains("math") || attrs.Contains("mso-element:comment"))
                {
                    return m.Value;
                }

                // Danh sách đã được rót khuôn riêng ở bước 2.
                if (attrs.Contains("MsoListParagraph", StringComparison.OrdinalIgnoreCase))
                {
                    return m.Value;
                }

                // Không áp margin thân bài (12pt) vào paragraph trong ô bảng — sẽ làm bảng phình to.
                if (tableDepth > 0)
                {
                    return m.Value;
                }

                // Nếu là đoạn căn giữa (ảnh hoặc caption) thì tôn trọng căn giữa và không thụt đầu dòng
                if (attrs.Contains("text-align: center") || attrs.Contains("text-align:center"))
                {
                    string centerStyle = $"text-align: center; font-family: '{tree.BodyStyle.FontName}'; font-size: {tree.BodyStyle.FontSize:0.#}pt; line-height: {tree.BodyStyle.LineSpacing:0.##}; text-indent: 0pt;";
                    return $"<p class=\"MsoNormal\" style=\"{centerStyle}\">{inner}</p>";
                }

                return $"<p class=\"MsoNormal\" style=\"{tree.BodyStyle.ToCssStyle()}\">{inner}</p>";
            }, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return processed;
        }

        /// <summary>
        /// Hàm chuyển đổi hỗ trợ cho văn bản thô (Backward-compatible).
        /// </summary>
        public static string ConvertPlainTextToSmartParagraphs(string plainText, WordFormatInfo formatInfo)
        {
            var tree = DocumentStructureTree.CreateDefaultSafeTree();
            tree.BodyStyle.FontName = formatInfo.FontName ?? "Times New Roman";
            tree.BodyStyle.FontSize = formatInfo.FontSize > 0 ? formatInfo.FontSize : 13.0f;
            tree.BodyStyle.Alignment = formatInfo.Alignment;
            tree.BodyStyle.FirstLineIndent = formatInfo.FirstLineIndent;
            tree.BodyStyle.LineSpacing = formatInfo.LineSpacing;

            if (!string.IsNullOrEmpty(formatInfo.ListBulletChar))
            {
                tree.Lists[1] = new ListLevelStyleNode { Level = 1, BulletOrPrefix = formatInfo.ListBulletChar };
            }

            return TransformMarkdownAstToOfficeHtml(plainText, tree);
        }

        /// <summary>
        /// Hàm hỗ trợ cho văn bản HTML (Backward-compatible).
        /// </summary>
        public static string ApplySmartWordStyles(string htmlFragment, WordFormatInfo formatInfo)
        {
            var tree = DocumentStructureTree.CreateDefaultSafeTree();
            tree.BodyStyle.FontName = formatInfo.FontName ?? "Times New Roman";
            tree.BodyStyle.FontSize = formatInfo.FontSize > 0 ? formatInfo.FontSize : 13.0f;
            tree.BodyStyle.Alignment = formatInfo.Alignment;
            tree.BodyStyle.FirstLineIndent = formatInfo.FirstLineIndent;
            tree.BodyStyle.LineSpacing = formatInfo.LineSpacing;

            if (!string.IsNullOrEmpty(formatInfo.ListBulletChar))
            {
                tree.Lists[1] = new ListLevelStyleNode { Level = 1, BulletOrPrefix = formatInfo.ListBulletChar };
            }

            return TransformHtmlWithTree(htmlFragment, tree);
        }
    }
}
