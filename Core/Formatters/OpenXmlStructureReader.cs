using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HapiCopi
{
    /// <summary>
    /// Bộ đọc Cấu trúc Tài liệu OpenXML chuẩn (OpenXmlStructureReader).
    ///
    /// Đo ĐÚNG những gì Word thực sự render, không chỉ đọc khai báo style:
    /// - Giải mã kế thừa: docDefaults -> Normal -> basedOn -> direct formatting (pPr/rPr).
    /// - Giải mã theme font khi tác giả khai báo theme; nếu không, dùng font mặc định
    ///   của Normal/docDefaults (không tự đổi sang script font của theme).
    /// - Đo Heading 1..6 từ chính các đoạn văn được dùng trong document.xml.
    /// - Dựng sườn đề mục (Outline) theo đúng thứ tự: số, tiêu đề, cấp.
    /// - Đo danh sách bullet và danh sách số thứ tự RIÊNG BIỆT từ numbering.xml + paragraph thật.
    /// - Đo khoảng cách trước/sau đoạn văn (SpaceBefore/SpaceAfter) — phần quan trọng của layout.
    ///
    /// Tuyệt đối không dùng heuristic mò số liệu, không lấy thông số của một file mẫu
    /// gán làm default cho toàn hệ thống.
    /// </summary>
    public static class OpenXmlStructureReader
    {
        private const string FallbackFont = "Times New Roman";

        // ------------------------------------------------------------------
        // API công khai
        // ------------------------------------------------------------------

        public static DocumentStructureTree ReadFromDocx(string docxPath)
        {
            if (string.IsNullOrWhiteSpace(docxPath) || !File.Exists(docxPath))
            {
                throw new FileNotFoundException($"Không tìm thấy file tài liệu: {docxPath}");
            }

            using var stream = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var tree = ReadFromStream(stream, Path.GetFileName(docxPath));
            tree.SourcePath = docxPath;
            return tree;
        }

        public static DocumentStructureTree ReadFromStream(Stream stream, string documentTitle)
        {
            var tree = new DocumentStructureTree
            {
                IsValid = true,
                DocumentTitle = documentTitle,
                CapturedAtUtc = DateTime.UtcNow
            };

            using var wordDoc = WordprocessingDocument.Open(stream, false);
            var mainPart = wordDoc.MainDocumentPart;
            if (mainPart == null) return tree;

            var resolver = new DocxStyleResolver(mainPart);
            resolver.Load();

            var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
            var numberingMap = NumberingMap.Build(numbering);

            var body = mainPart.Document?.Body;
            if (body != null)
            {
                var paragraphs = CollectBodyParagraphs(body);

                SampleBodyStyle(paragraphs, tree, resolver);
                SeedHeadingStyles(tree, resolver);

                var headingNumbers = new Dictionary<int, Dictionary<int, int>>();
                var headingProps = new List<MeasuredHeading>();

                foreach (var paragraph in paragraphs)
                {
                    var measured = MeasureParagraph(paragraph, resolver, tree);
                    if (!measured.IsHeading) continue;

                    if (!headingNumbers.TryGetValue(measured.NumId, out var counters))
                    {
                        counters = new Dictionary<int, int>();
                        headingNumbers[measured.NumId] = counters;
                    }

                    var (autoNumber, autoParts) = numberingMap.ComputeAutoNumber(
                        measured.NumId,
                        measured.Ilvl,
                        counters);

                    // Đoạn heading rỗng (thường là page break) không phải đề mục thật.
                    if (string.IsNullOrWhiteSpace(measured.Text) && string.IsNullOrWhiteSpace(autoNumber))
                    {
                        continue;
                    }

                    var entry = BuildOutlineEntry(measured, autoNumber, autoParts);
                    tree.Outline.Add(entry);

                    headingProps.Add(new MeasuredHeading
                    {
                        Level = measured.HeadingLevel,
                        Entry = entry,
                        Run = measured.Run,
                        Para = measured.Para,
                        EffectiveFont = measured.EffectiveFont,
                        EffectiveFontSize = measured.EffectiveFontSize
                    });
                }

                ApplyMeasuredHeadingStyles(tree, headingProps);
                SampleListStyles(paragraphs, tree, resolver, numberingMap);
                SampleTableStyle(body, tree);
            }

            return tree;
        }

        /// <summary>
        /// Lấy các đoạn văn thuộc thân tài liệu theo đúng thứ tự, gồm cả nội dung
        /// nằm trong content control, nhưng bỏ qua bảng và mục lục tự động.
        /// </summary>
        private static List<Paragraph> CollectBodyParagraphs(Body body)
        {
            var result = new List<Paragraph>();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                if (paragraph.Ancestors<Table>().Any()) continue;
                if (IsTableOfContentsParagraph(paragraph)) continue;
                result.Add(paragraph);
            }
            return result;
        }

        private static bool IsTableOfContentsParagraph(Paragraph paragraph)
        {
            string styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
            if (styleId.StartsWith("TOC", StringComparison.OrdinalIgnoreCase)) return true;

            foreach (var sdt in paragraph.Ancestors<SdtBlock>())
            {
                // w:docPartObj/w:docPartGallery val="Table of Contents"
                string sdtXml = sdt.SdtProperties?.OuterXml ?? "";
                if (sdtXml.Contains("Table of Contents", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Dữ liệu đo trung gian
        // ------------------------------------------------------------------

        private sealed class MeasuredHeading
        {
            public int Level { get; set; } = 1;
            public OutlineEntry Entry { get; set; } = new();
            public RunProps Run { get; set; } = new();
            public ParaProps Para { get; set; } = new();
            public string EffectiveFont { get; set; } = FallbackFont;
            public float EffectiveFontSize { get; set; } = 0;
        }

        private sealed class MeasuredParagraph
        {
            public string Text { get; set; } = "";
            public bool IsHeading { get; set; }
            public int HeadingLevel { get; set; }
            public bool HasNumbering { get; set; }
            public int NumId { get; set; }
            public int Ilvl { get; set; }
            public RunProps Run { get; set; } = new();
            public ParaProps Para { get; set; } = new();
            public string EffectiveFont { get; set; } = FallbackFont;
            public float EffectiveFontSize { get; set; } = 0;
            public string? StyleId { get; set; }
        }

        private sealed class RunProps
        {
            public string? FontName { get; set; }
            public string? FontTheme { get; set; }
            public float? FontSize { get; set; }
            public bool? Bold { get; set; }
            public bool? Italic { get; set; }
            public string? ColorHex { get; set; }

            public RunProps Clone() => (RunProps)MemberwiseClone();

            public void Apply(OpenXmlElement? rPr)
            {
                if (rPr == null) return;

                var fonts = rPr.GetFirstChild<RunFonts>();
                if (fonts != null)
                {
                    string? ascii = fonts.Ascii?.Value;
                    string? hAnsi = fonts.HighAnsi?.Value;
                    string? cs = fonts.ComplexScript?.Value;
                    string? theme = fonts.AsciiTheme?.InnerText ?? fonts.HighAnsiTheme?.InnerText;

                    if (!string.IsNullOrWhiteSpace(ascii))
                    {
                        FontName = ascii;
                        FontTheme = null;
                    }
                    else if (!string.IsNullOrWhiteSpace(hAnsi))
                    {
                        FontName = hAnsi;
                        FontTheme = null;
                    }
                    else if (!string.IsNullOrWhiteSpace(cs))
                    {
                        FontName = cs;
                        FontTheme = null;
                    }
                    else if (!string.IsNullOrWhiteSpace(theme))
                    {
                        FontTheme = theme;
                        FontName = null;
                    }
                }

                var size = rPr.GetFirstChild<FontSize>();
                if (size?.Val?.Value != null && float.TryParse(size.Val.Value, out float halfPoints))
                {
                    FontSize = halfPoints / 2.0f;
                }

                var bold = rPr.GetFirstChild<Bold>();
                if (bold != null)
                {
                    Bold = bold.Val == null || bold.Val.Value;
                }

                var italic = rPr.GetFirstChild<Italic>();
                if (italic != null)
                {
                    Italic = italic.Val == null || italic.Val.Value;
                }

                var color = rPr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Color>();
                if (color?.Val?.Value != null)
                {
                    string val = color.Val.Value;
                    ColorHex = string.Equals(val, "auto", StringComparison.OrdinalIgnoreCase)
                        ? "#000000"
                        : (val.StartsWith("#", StringComparison.Ordinal) ? val : "#" + val);
                }
            }
        }

        private sealed class ParaProps
        {
            public int? Alignment { get; set; }              // 0=Left, 1=Center, 2=Right, 3=Justify
            public float? FirstLineIndent { get; set; }
            public float? LeftIndent { get; set; }
            public float? HangingIndent { get; set; }
            public float? SpaceBefore { get; set; }
            public float? SpaceAfter { get; set; }
            public float? LineSpacing { get; set; }
            public bool? KeepNext { get; set; }
            public int? OutlineLevel { get; set; }           // 0-based
            public bool HasNumbering { get; set; }
            public int NumId { get; set; }
            public int Ilvl { get; set; }

            public ParaProps Clone() => (ParaProps)MemberwiseClone();

            public void Apply(OpenXmlElement? pPr)
            {
                if (pPr == null) return;

                var justify = pPr.GetFirstChild<Justification>();
                if (justify?.Val != null)
                {
                    string val = (justify.Val.InnerText ?? "").ToLowerInvariant();
                    Alignment = val switch
                    {
                        "center" => 1,
                        "right" => 2,
                        "both" => 3,
                        "distribute" => 3,
                        _ => 0
                    };
                }

                var spacing = pPr.GetFirstChild<SpacingBetweenLines>();
                if (spacing != null)
                {
                    if (spacing.Before?.Value != null && float.TryParse(spacing.Before.Value, out float before))
                    {
                        SpaceBefore = before / 20.0f;
                    }
                    if (spacing.After?.Value != null && float.TryParse(spacing.After.Value, out float after))
                    {
                        SpaceAfter = after / 20.0f;
                    }
                    if (spacing.Line?.Value != null && float.TryParse(spacing.Line.Value, out float line) && line > 0)
                    {
                        LineSpacing = (float)Math.Round(line / 240.0f, 2);
                    }
                }

                var ind = pPr.GetFirstChild<Indentation>();
                if (ind != null)
                {
                    if (ind.FirstLine?.Value != null && float.TryParse(ind.FirstLine.Value, out float first))
                    {
                        FirstLineIndent = (float)Math.Round(first / 20.0f, 2);
                    }
                    else if (ind.FirstLineChars?.Value != null && float.TryParse(ind.FirstLineChars.Value.ToString(), out float firstChars))
                    {
                        // 100 char units = 1 ký tự; quy đổi xấp xỉ theo cỡ chữ mặc định 13pt.
                        FirstLineIndent = (float)Math.Round(firstChars / 100.0f * 13.0f, 2);
                    }

                    if (ind.Left?.Value != null && float.TryParse(ind.Left.Value, out float left))
                    {
                        LeftIndent = (float)Math.Round(left / 20.0f, 2);
                    }
                    if (ind.Hanging?.Value != null && float.TryParse(ind.Hanging.Value, out float hanging))
                    {
                        HangingIndent = (float)Math.Round(hanging / 20.0f, 2);
                    }
                }

                var keepNext = pPr.GetFirstChild<KeepNext>();
                if (keepNext != null)
                {
                    KeepNext = keepNext.Val == null || keepNext.Val.Value;
                }

                var outline = pPr.GetFirstChild<OutlineLevel>();
                if (outline?.Val?.Value != null)
                {
                    OutlineLevel = outline.Val.Value;
                }

                var numPr = pPr.GetFirstChild<NumberingProperties>();
                if (numPr != null)
                {
                    if (numPr.NumberingId?.Val?.Value != null)
                    {
                        NumId = numPr.NumberingId.Val.Value;
                    }
                    if (numPr.NumberingLevelReference?.Val?.Value != null)
                    {
                        Ilvl = numPr.NumberingLevelReference.Val.Value;
                    }
                    // numId = 0 nghĩa là "bỏ numbering", không phải danh sách thật.
                    HasNumbering = NumId > 0;
                }
            }
        }

        private sealed class ResolvedStyle
        {
            public RunProps Run { get; set; } = new();
            public ParaProps Para { get; set; } = new();
            public string Name { get; set; } = "";
            public string Id { get; set; } = "";
        }

        // ------------------------------------------------------------------
        // Giải mã style + theme
        // ------------------------------------------------------------------

        private sealed class DocxStyleResolver
        {
            private readonly MainDocumentPart _mainPart;
            private readonly Dictionary<string, Style> _stylesById = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, Style> _stylesByName = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, ResolvedStyle> _resolved = new(StringComparer.OrdinalIgnoreCase);
            private readonly ResolvedStyle _docDefaults = new();
            private readonly Dictionary<string, ResolvedStyle> _defaultStyles = new(StringComparer.OrdinalIgnoreCase);

            private string _majorFont = "";
            private string _minorFont = "";
            private string _documentDefaultFont = "";
            private string _language = "";

            /// <summary>
            /// Font thân bài hiệu dụng:
            /// - Nếu docDefaults khai báo font rõ ràng => dùng font đó.
            /// - Nếu không, dùng fallback Times New Roman (quy ước văn bản Word phổ thông),
            ///   KHÔNG tự lấy script font của theme (ví dụ theme Việt có thể là Arial)
            ///   vì như vậy sẽ làm lệch khỏi font tác giả đang thấy.
            /// </summary>
            public string BodyFont => !string.IsNullOrWhiteSpace(_documentDefaultFont)
                ? _documentDefaultFont
                : FallbackFont;

            public DocxStyleResolver(MainDocumentPart mainPart)
            {
                _mainPart = mainPart;
            }

            public void Load()
            {
                LoadDocDefaults();
                LoadTheme();
                LoadStyles();

                // Font hiệu dụng của Normal là nền cho mọi style không khai báo font riêng.
                var normal = Resolve("Normal");
                if (!string.IsNullOrWhiteSpace(normal.Run.FontName))
                {
                    _documentDefaultFont = normal.Run.FontName!;
                }
                else if (!string.IsNullOrWhiteSpace(normal.Run.FontTheme))
                {
                    _documentDefaultFont = ResolveFont(null, normal.Run.FontTheme);
                }
            }

            private void LoadTheme()
            {
                try
                {
                    var themePart = _mainPart.ThemePart;
                    if (themePart == null) return;

                    using var stream = themePart.GetStream(FileMode.Open, FileAccess.Read);
                    var doc = XDocument.Load(stream);
                    XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";

                    var major = doc.Descendants(a + "majorFont").FirstOrDefault();
                    var minor = doc.Descendants(a + "minorFont").FirstOrDefault();

                    _majorFont = major?.Elements(a + "latin").FirstOrDefault()?.Attribute("typeface")?.Value ?? "";
                    _minorFont = minor?.Elements(a + "latin").FirstOrDefault()?.Attribute("typeface")?.Value ?? "";
                }
                catch
                {
                    // Theme không đọc được: dùng fallback an toàn, không suy đoán thêm.
                }
            }

            private void LoadDocDefaults()
            {
                var stylesPart = _mainPart.StyleDefinitionsPart;
                var styles = stylesPart?.Styles;
                if (styles == null) return;

                var docDefaults = styles.DocDefaults;

                var runBase = docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
                _docDefaults.Run.Apply(runBase);

                if (!string.IsNullOrWhiteSpace(_docDefaults.Run.FontName))
                {
                    _documentDefaultFont = _docDefaults.Run.FontName!;
                }

                var paraBase = docDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
                _docDefaults.Para.Apply(paraBase);

                // Ngôn ngữ tài liệu: giữ lại để chẩn đoán/log, không dùng để tự đổi font
                // khi tác giả không khai báo theme font.
                var lang = runBase?.GetFirstChild<Languages>();
                _language = lang?.Val?.Value
                    ?? _mainPart.DocumentSettingsPart?.Settings?.GetFirstChild<ThemeFontLanguages>()?.Val?.Value
                    ?? "";
            }

            private void LoadStyles()
            {
                var styles = _mainPart.StyleDefinitionsPart?.Styles;
                if (styles == null) return;

                foreach (var style in styles.Elements<Style>())
                {
                    string id = style.StyleId?.Value ?? "";
                    if (string.IsNullOrEmpty(id)) continue;

                    string name = style.StyleName?.Val?.Value ?? id;
                    _stylesById[id] = style;
                    if (!_stylesByName.ContainsKey(name))
                    {
                        _stylesByName[name] = style;
                    }
                }
            }

            public ResolvedStyle Resolve(string styleId)
            {
                var style = GetStyle(styleId);
                if (style == null)
                {
                    return ResolveDefault();
                }

                string id = style.StyleId?.Value ?? styleId;
                if (_resolved.TryGetValue(id, out var cached))
                {
                    return cached;
                }

                var result = ResolveDefault();
                result.Name = style.StyleName?.Val?.Value ?? id;
                result.Id = id;

                string? basedOn = style.BasedOn?.Val?.Value;
                if (!string.IsNullOrWhiteSpace(basedOn) && !string.Equals(basedOn, id, StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Resolve(basedOn);
                    result.Run = parent.Run.Clone();
                    result.Para = parent.Para.Clone();
                }

                result.Run.Apply(style.StyleRunProperties);
                result.Para.Apply(style.StyleParagraphProperties);
                _resolved[id] = result;
                return result;
            }

            public ResolvedStyle ResolveHeadingStyle(int level)
            {
                var candidates = new[]
                {
                    $"Heading{level}",
                    $"Heading {level}",
                    $"heading {level}",
                    $"Tiêu đề {level}",
                    $"Tieu de {level}"
                };

                foreach (string candidate in candidates)
                {
                    if (_defaultStyles.TryGetValue(candidate, out var cached)) return cached;

                    var style = GetStyle(candidate);
                    if (style == null) continue;
                    if (style.Type != null && style.Type.Value != StyleValues.Paragraph) continue;

                    var resolved = Resolve(style.StyleId?.Value ?? candidate);
                    _defaultStyles[candidate] = resolved;
                    return resolved;
                }

                var fallback = ResolveDefault();
                return fallback;
            }

            private ResolvedStyle ResolveDefault()
            {
                var result = new ResolvedStyle
                {
                    Run = _docDefaults.Run.Clone(),
                    Para = _docDefaults.Para.Clone(),
                    Name = "Normal",
                    Id = "Normal"
                };
                return result;
            }

            private Style? GetStyle(string idOrName)
            {
                if (string.IsNullOrWhiteSpace(idOrName)) return null;
                if (_stylesById.TryGetValue(idOrName, out var byId)) return byId;
                if (_stylesByName.TryGetValue(idOrName, out var byName)) return byName;
                return null;
            }

            public string ResolveFont(string? explicitFont, string? themeAttr)
            {
                if (!string.IsNullOrWhiteSpace(explicitFont)) return explicitFont;

                string theme = themeAttr ?? "";
                if (theme.StartsWith("major", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(_majorFont)) return _majorFont;
                }
                else if (theme.StartsWith("minor", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(_minorFont)) return _minorFont;
                }

                return BodyFont;
            }
        }

        // ------------------------------------------------------------------
        // numbering.xml: map numId -> abstractNum -> level
        // ------------------------------------------------------------------

        private sealed class NumberingMap
        {
            private readonly Dictionary<int, Dictionary<int, Level>> _numLevels = new();

            public static NumberingMap Build(Numbering? numbering)
            {
                var map = new NumberingMap();
                if (numbering == null) return map;

                var abstractLevels = new Dictionary<int, Dictionary<int, Level>>();
                foreach (var abstractNum in numbering.Elements<AbstractNum>())
                {
                    int abstractId = abstractNum.AbstractNumberId?.Value ?? 0;
                    var levels = new Dictionary<int, Level>();
                    foreach (var level in abstractNum.Elements<Level>())
                    {
                        int ilvl = level.LevelIndex?.Value ?? 0;
                        levels[ilvl] = level;
                    }
                    abstractLevels[abstractId] = levels;
                }

                foreach (var instance in numbering.Elements<NumberingInstance>())
                {
                    int numId = instance.NumberID?.Value ?? 0;
                    int abstractId = instance.AbstractNumId?.Val?.Value ?? 0;
                    if (numId <= 0) continue;

                    if (abstractLevels.TryGetValue(abstractId, out var levels))
                    {
                        map._numLevels[numId] = levels;
                    }
                }

                return map;
            }

            public Level? GetLevel(int numId, int ilvl)
            {
                if (_numLevels.TryGetValue(numId, out var levels) && levels.TryGetValue(ilvl, out var level))
                {
                    return level;
                }
                return null;
            }

            /// <summary>
            /// Sinh số đề mục tự động từ numPr (khi tác giả dùng Word numbering thay vì gõ số).
            /// counters: trạng thái đếm theo numId.
            /// </summary>
            public (string Number, List<int> Parts) ComputeAutoNumber(
                int numId,
                int ilvl,
                Dictionary<int, int> counters)
            {
                var level = GetLevel(numId, ilvl);
                if (level == null) return ("", new List<int>());

                string format = level.NumberingFormat?.Val?.InnerText ?? "decimal";
                if (string.Equals(format, "bullet", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(format, "none", StringComparison.OrdinalIgnoreCase))
                {
                    return ("", new List<int>());
                }

                int current = counters.TryGetValue(ilvl, out int c) ? c : 0;
                current++;
                counters[ilvl] = current;

                for (int deeper = ilvl + 1; deeper <= 8; deeper++)
                {
                    counters[deeper] = 0;
                }

                var parts = new List<int>();
                for (int i = 0; i <= ilvl; i++)
                {
                    if (counters.TryGetValue(i, out int value) && value > 0)
                    {
                        parts.Add(value);
                    }
                    else
                    {
                        parts.Add(1);
                    }
                }

                string levelText = level.LevelText?.Val?.Value ?? "";
                if (string.IsNullOrWhiteSpace(levelText))
                {
                    levelText = string.Join(".", Enumerable.Range(1, ilvl + 1).Select(i => "%" + i)) + ".";
                }

                string number = levelText;
                for (int i = 1; i <= 9; i++)
                {
                    if (number.Contains("%" + i))
                    {
                        int value = i - 1 < counters.Count && counters.TryGetValue(i - 1, out int v) && v > 0 ? v : 1;
                        number = number.Replace("%" + i, value.ToString());
                    }
                }

                return (number.Trim(), parts);
            }
        }

        // ------------------------------------------------------------------
        // Đo thân bài
        // ------------------------------------------------------------------

        private static void SampleBodyStyle(
            List<Paragraph> paragraphs,
            DocumentStructureTree tree,
            DocxStyleResolver resolver)
        {
            // Loại cả các dòng "trông giống đề mục" ngay từ vòng lấy mẫu thân bài,
            // tránh trường hợp heading gõ tay (chưa có style) làm nhiễu font/spacing body.
            var samples = paragraphs
                .Select(p => MeasureParagraph(p, resolver, tree))
                .Where(m => !m.IsHeading && !m.HasNumbering
                            && !LooksLikeNumberedHeadingText(m.Text)
                            && m.Text.Length > 30)
                .Take(80)
                .ToList();

            if (samples.Count == 0)
            {
                samples = paragraphs
                    .Select(p => MeasureParagraph(p, resolver, tree))
                    .Where(m => !m.IsHeading && !m.HasNumbering
                                && !LooksLikeNumberedHeadingText(m.Text)
                                && m.Text.Length > 5)
                    .Take(40)
                    .ToList();
            }

            if (samples.Count == 0) return;

            string font = Mode(samples.Select(s => s.EffectiveFont), StringComparer.OrdinalIgnoreCase) ?? FallbackFont;
            float fontSize = Mode(samples.Select(s => s.EffectiveFontSize)) ?? 0f;
            int alignment = Mode(samples.Select(s => s.Para.Alignment ?? 0)) ?? 0;
            float firstIndent = Mode(samples.Select(s => s.Para.FirstLineIndent ?? 0f)) ?? 0f;
            float lineSpacing = Mode(samples.Select(s => s.Para.LineSpacing ?? 1.15f)) ?? 1.15f;
            float spaceBefore = Mode(samples.Select(s => s.Para.SpaceBefore ?? 0f)) ?? 0f;
            float spaceAfter = Mode(samples.Select(s => s.Para.SpaceAfter ?? 0f)) ?? 0f;

            tree.BodyStyle.FontName = font;
            tree.BodyStyle.FontSize = fontSize > 0 ? fontSize : 13f;
            tree.BodyStyle.Alignment = alignment;
            tree.BodyStyle.FirstLineIndent = firstIndent;
            tree.BodyStyle.LineSpacing = lineSpacing > 0 ? lineSpacing : 1.15f;
            tree.BodyStyle.SpaceBefore = spaceBefore;
            tree.BodyStyle.SpaceAfter = spaceAfter;
        }

        private static void SeedHeadingStyles(DocumentStructureTree tree, DocxStyleResolver resolver)
        {
            for (int level = 1; level <= 6; level++)
            {
                var style = resolver.ResolveHeadingStyle(level);
                string font = resolver.ResolveFont(style.Run.FontName, style.Run.FontTheme);
                float size = style.Run.FontSize ?? 0f;

                tree.Headings[level] = new HeadingStyleNode
                {
                    Level = level,
                    FontName = font,
                    FontSize = size > 0 ? size : tree.BodyStyle.FontSize,
                    Bold = style.Run.Bold ?? true,
                    Italic = style.Run.Italic ?? false,
                    ColorHex = style.Run.ColorHex,
                    Alignment = style.Para.Alignment ?? 0,
                    SpaceBefore = style.Para.SpaceBefore ?? 0f,
                    SpaceAfter = style.Para.SpaceAfter ?? 0f
                };
            }
        }

        private static void ApplyMeasuredHeadingStyles(
            DocumentStructureTree tree,
            List<MeasuredHeading> headings)
        {
            // Khuôn số đề mục theo cấp, đo từ chính text của tài liệu; KHÔNG suy đoán.
            var formatGroups = headings
                .Where(h => h.Entry.NumberParts.Count > 0)
                .GroupBy(h => h.Level);

            foreach (var group in formatGroups)
            {
                string? format = Mode(group.Select(h => BuildNumberPattern(h.Entry)), StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(format))
                {
                    tree.HeadingNumberFormats[group.Key] = format;
                }
            }

            foreach (var group in headings.GroupBy(h => h.Level))
            {
                if (!tree.Headings.TryGetValue(group.Key, out var heading))
                {
                    heading = new HeadingStyleNode { Level = group.Key };
                    tree.Headings[group.Key] = heading;
                }

                string font = Mode(group.Select(h => h.EffectiveFont), StringComparer.OrdinalIgnoreCase) ?? heading.FontName;
                float size = Mode(group.Select(h => h.EffectiveFontSize)) ?? heading.FontSize;
                bool bold = Mode(group.Select(h => h.Run.Bold ?? false)) ?? heading.Bold;
                bool italic = Mode(group.Select(h => h.Run.Italic ?? false)) ?? heading.Italic;
                string? color = Mode(group.Select(h => h.Run.ColorHex ?? ""), StringComparer.OrdinalIgnoreCase);
                int alignment = Mode(group.Select(h => h.Para.Alignment ?? 0)) ?? heading.Alignment;

                // Khoảng cách trước/sau thường được tác giả chỉnh theo từng mục và rất dễ
                // lệch giữa các mục. Dùng mục CUỐI CÙNG cùng cấp làm chuẩn để nội dung mới
                // nối tiếp đúng mục gần nhất (ví dụ 4.5 phải khớp 4.3), thay vì lấy mode
                // của các mục cũ có thể đã bị chỉnh khác.
                float before = group.Last().Para.SpaceBefore ?? heading.SpaceBefore;
                float after = group.Last().Para.SpaceAfter ?? heading.SpaceAfter;

                heading.FontName = font;
                heading.FontSize = size > 0 ? size : heading.FontSize;
                heading.Bold = bold;
                heading.Italic = italic;
                heading.ColorHex = string.IsNullOrWhiteSpace(color) ? null : color;
                heading.Alignment = alignment;
                heading.SpaceBefore = before;
                heading.SpaceAfter = after;
            }
        }

        private static OutlineEntry BuildOutlineEntry(
            MeasuredParagraph measured,
            string autoNumber,
            List<int> autoParts)
        {
            string text = measured.Text.Trim();

            if (TryParseTextNumber(text, out string number, out List<int> parts, out string title))
            {
                return new OutlineEntry
                {
                    Level = measured.HeadingLevel,
                    Number = number,
                    NumberParts = parts,
                    Title = title,
                    Text = text,
                    NumberIsAutomatic = false
                };
            }

            if (!string.IsNullOrWhiteSpace(autoNumber))
            {
                return new OutlineEntry
                {
                    Level = measured.HeadingLevel,
                    Number = autoNumber.TrimEnd('.').Trim(),
                    NumberParts = autoParts,
                    Title = text,
                    Text = autoNumber + " " + text,
                    NumberIsAutomatic = true
                };
            }

            return new OutlineEntry
            {
                Level = measured.HeadingLevel,
                Number = "",
                NumberParts = new List<int>(),
                Title = text,
                Text = text,
                NumberIsAutomatic = false
            };
        }

        // ------------------------------------------------------------------
        // Đo danh sách
        // ------------------------------------------------------------------

        private sealed class ListSample
        {
            public int Level { get; set; }
            public bool Ordered { get; set; }
            public string Marker { get; set; } = "-";
            public string NumberFormat { get; set; } = "decimal";
            public float Left { get; set; }
            public float Hanging { get; set; }
            public float SpaceBefore { get; set; }
            public float SpaceAfter { get; set; }
        }

        private static void SampleListStyles(
            List<Paragraph> paragraphs,
            DocumentStructureTree tree,
            DocxStyleResolver resolver,
            NumberingMap numbering)
        {
            var samples = new List<ListSample>();

            foreach (var paragraph in paragraphs)
            {
                var measured = MeasureParagraph(paragraph, resolver, tree);
                if (!measured.HasNumbering) continue;

                var level = numbering.GetLevel(measured.NumId, measured.Ilvl);
                string format = level?.NumberingFormat?.Val?.InnerText ?? "bullet";
                bool ordered = !string.Equals(format, "bullet", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(format, "none", StringComparison.OrdinalIgnoreCase);

                float left = measured.Para.LeftIndent
                    ?? ReadIndent(level?.PreviousParagraphProperties?.Indentation?.Left?.Value)
                    ?? 36f;
                float hanging = measured.Para.HangingIndent
                    ?? ReadIndent(level?.PreviousParagraphProperties?.Indentation?.Hanging?.Value)
                    ?? 18f;

                samples.Add(new ListSample
                {
                    Level = measured.Ilvl + 1,
                    Ordered = ordered,
                    Marker = level?.LevelText?.Val?.Value ?? (ordered ? "%1." : "-"),
                    NumberFormat = format,
                    Left = left,
                    Hanging = hanging,
                    SpaceBefore = measured.Para.SpaceBefore ?? 0f,
                    SpaceAfter = measured.Para.SpaceAfter ?? 0f
                });
            }

            // Nếu tài liệu chỉ dùng bullet gõ tay ("- ..."), đo luôn kiểu gõ tay đó
            // nhưng chỉ khi KHÔNG có danh sách ngữ nghĩa nào ở cấp 1.
            if (!samples.Any(s => !s.Ordered && s.Level == 1))
            {
                foreach (var paragraph in paragraphs)
                {
                    var measured = MeasureParagraph(paragraph, resolver, tree);
                    if (measured.IsHeading || measured.HasNumbering) continue;

                    string text = measured.Text.TrimStart();
                    if (!(text.StartsWith("- ", StringComparison.Ordinal)
                          || text.StartsWith("• ", StringComparison.Ordinal)
                          || text.StartsWith("– ", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    samples.Add(new ListSample
                    {
                        Level = 1,
                        Ordered = false,
                        Marker = text[..1],
                        NumberFormat = "bullet",
                        Left = measured.Para.LeftIndent ?? 0f,
                        Hanging = measured.Para.HangingIndent ?? 0f,
                        SpaceBefore = measured.Para.SpaceBefore ?? 0f,
                        SpaceAfter = measured.Para.SpaceAfter ?? 0f
                    });
                }
            }

            if (samples.Count == 0) return;

            foreach (var group in samples.GroupBy(s => s.Level))
            {
                var bullet = group.Where(s => !s.Ordered).ToList();
                var ordered = group.Where(s => s.Ordered).ToList();

                if (bullet.Count > 0)
                {
                    tree.BulletLists[group.Key] = CreateListStyle(group.Key, false, bullet);
                }
                if (ordered.Count > 0)
                {
                    tree.OrderedLists[group.Key] = CreateListStyle(group.Key, true, ordered);
                }

                var dominant = bullet.Count >= ordered.Count ? bullet : ordered;
                if (dominant.Count > 0)
                {
                    tree.Lists[group.Key] = CreateListStyle(group.Key, dominant == ordered, dominant);
                }
            }
        }

        private static ListLevelStyleNode CreateListStyle(int level, bool ordered, List<ListSample> samples)
        {
            string marker = Mode(samples.Select(s => s.Marker), StringComparer.OrdinalIgnoreCase) ?? (ordered ? "%1." : "-");
            string numberFormat = Mode(samples.Select(s => s.NumberFormat), StringComparer.OrdinalIgnoreCase) ?? (ordered ? "decimal" : "bullet");

            // Bullet ký tự private-use (Wingdings/Symbol) không render được trong HTML.
            if (!ordered && marker.Length > 0 && marker[0] >= '\uE000')
            {
                marker = "•";
            }

            float left = Mode(samples.Select(s => s.Left)) ?? 36f;
            float hanging = Mode(samples.Select(s => s.Hanging)) ?? 18f;

            // Word thường đặt khoảng cách giữa các item bằng cách luân phiên
            // before=12/after=0 và before=0/after=12. Vì vậy phải lấy mode của
            // max(before, after) trên TỪNG item, rồi chuẩn hóa thành before/0.
            float itemSpacing = Mode(samples.Select(s => Math.Max(s.SpaceBefore, s.SpaceAfter))) ?? 0f;
            float before = itemSpacing;
            float after = 0f;

            return new ListLevelStyleNode
            {
                Level = level,
                BulletOrPrefix = marker,
                NumberFormat = numberFormat,
                IsOrdered = ordered,
                LeftIndent = left,
                HangingIndent = hanging,
                SpaceBefore = before,
                SpaceAfter = after
            };
        }

        private static float? ReadIndent(string? value)
        {
            if (value != null && float.TryParse(value, out float twips))
            {
                return (float)Math.Round(twips / 20.0f, 2);
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Đo bảng biểu
        // ------------------------------------------------------------------

        private static void SampleTableStyle(Body body, DocumentStructureTree tree)
        {
            var firstTable = body.Elements<Table>().FirstOrDefault();
            if (firstTable == null) return;

            var props = firstTable.Elements<TableProperties>().FirstOrDefault();
            if (props == null) return;

            if (props.TableJustification?.Val != null)
            {
                string align = (props.TableJustification.Val.InnerText ?? "").ToLowerInvariant();
                tree.TableStyle.Alignment = align switch
                {
                    "center" => 1,
                    "right" => 2,
                    _ => 0
                };
            }

            if (props.TableBorders?.TopBorder?.Color?.Value != null)
            {
                string color = props.TableBorders.TopBorder.Color.Value;
                if (!string.IsNullOrWhiteSpace(color) && color != "auto")
                {
                    tree.TableStyle.BorderColor = color.StartsWith("#", StringComparison.Ordinal) ? color : "#" + color;
                }
            }
        }

        // ------------------------------------------------------------------
        // Đo một đoạn văn (style + direct formatting + run thực tế)
        // ------------------------------------------------------------------

        private static MeasuredParagraph MeasureParagraph(
            Paragraph paragraph,
            DocxStyleResolver resolver,
            DocumentStructureTree tree)
        {
            var pPr = paragraph.ParagraphProperties;
            string styleId = pPr?.ParagraphStyleId?.Val?.Value ?? "Normal";
            var style = resolver.Resolve(styleId);

            var run = style.Run.Clone();
            var para = style.Para.Clone();

            // Direct formatting ở cấp paragraph.
            para.Apply(pPr);

            // Direct formatting trên paragraph mark cũng ảnh hưởng run mặc định.
            run.Apply(pPr?.ParagraphMarkRunProperties);

            // Chọn run có nhiều nội dung nhất làm đại diện (tránh run trang trí nhỏ).
            Run? dominantRun = null;
            int dominantLength = -1;
            foreach (var r in paragraph.Elements<Run>())
            {
                int length = r.InnerText?.Length ?? 0;
                if (length > dominantLength)
                {
                    dominantLength = length;
                    dominantRun = r;
                }
            }

            var runForMeasure = run.Clone();
            if (dominantRun != null)
            {
                runForMeasure.Apply(dominantRun.RunProperties);
            }

            string effectiveFont = resolver.ResolveFont(runForMeasure.FontName, runForMeasure.FontTheme);
            float effectiveSize = runForMeasure.FontSize ?? 0f;

            string text = paragraph.InnerText ?? "";
            text = Regex.Replace(text, @"[ \t\r\n]+", " ").Trim();

            int headingLevel = 0;
            int styleHeading = ParseHeadingLevel(styleId, style.Name);
            if (styleHeading >= 1 && styleHeading <= 6)
            {
                headingLevel = styleHeading;
            }
            else if (para.OutlineLevel.HasValue && para.OutlineLevel.Value >= 0 && para.OutlineLevel.Value <= 8)
            {
                headingLevel = para.OutlineLevel.Value + 1;
            }

            bool manualHeading = false;
            if (headingLevel == 0 && !para.HasNumbering && !string.IsNullOrWhiteSpace(text))
            {
                manualHeading = LooksLikeManualHeading(text, para, runForMeasure, effectiveSize, tree.BodyStyle.FontSize);
                if (manualHeading)
                {
                    TryParseTextNumber(text, out _, out var parts, out _);
                    headingLevel = Math.Clamp(parts.Count, 1, 6);
                }
            }

            bool isHeading = headingLevel >= 1 && headingLevel <= 6;
            if (isHeading && headingLevel == 0) headingLevel = 1;

            return new MeasuredParagraph
            {
                Text = text,
                IsHeading = isHeading,
                HeadingLevel = headingLevel,
                HasNumbering = para.HasNumbering,
                NumId = para.NumId,
                Ilvl = para.Ilvl,
                Run = runForMeasure,
                Para = para,
                EffectiveFont = effectiveFont,
                EffectiveFontSize = effectiveSize,
                StyleId = styleId
            };
        }

        /// <summary>
        /// Nhận diện dòng "trông giống đề mục" chỉ dựa trên text (không cần style).
        /// Dùng để loại nhiễu khi đo thân bài và khi tài liệu không dùng Heading style.
        /// </summary>
        private static bool LooksLikeNumberedHeadingText(string text)
        {
            if (!TryParseTextNumber(text, out _, out var parts, out string title)) return false;
            if (title.Length == 0 || title.Length > 160) return false;
            if (title.Contains(':')) return false;

            char end = title.TrimEnd().LastOrDefault();
            if (end == '.' || end == ':' || end == ';') return false;
            if (!char.IsUpper(title[0])) return false;

            if (parts.Count >= 2) return true;

            bool hasLetter = false;
            foreach (char c in title)
            {
                if (!char.IsLetter(c)) continue;
                hasLetter = true;
                if (char.IsLower(c)) return false;
            }
            return hasLetter;
        }

        private static bool LooksLikeManualHeading(
            string text,
            ParaProps para,
            RunProps run,
            float effectiveSize,
            float bodySize)
        {
            if (!TryParseTextNumber(text, out _, out var parts, out string title)) return false;
            if (title.Length == 0 || title.Length > 160) return false;
            if (title.Contains(':')) return false;

            char end = title.TrimEnd().LastOrDefault();
            if (end == '.' || end == ':' || end == ';') return false;
            if (!char.IsUpper(title[0])) return false;

            if (parts.Count >= 2) return true;

            bool bold = run.Bold ?? false;
            bool centered = (para.Alignment ?? 0) == 1;
            bool larger = effectiveSize > 0 && bodySize > 0 && effectiveSize >= bodySize + 0.5f;
            return bold || centered || larger;
        }

        // ------------------------------------------------------------------
        // Tiện ích số / thống kê
        // ------------------------------------------------------------------

        private static bool TryParseTextNumber(
            string text,
            out string number,
            out List<int> parts,
            out string title)
        {
            number = "";
            parts = new List<int>();
            title = text.Trim();

            var match = Regex.Match(text.Trim(), @"^(?<num>\d+(?:\.\d+){0,5})(?<dot>\.?)\s+(?<title>\S.*)$");
            if (!match.Success) return false;

            string rawNumber = match.Groups["num"].Value;
            var parsed = rawNumber.Split('.')
                .Select(p => int.TryParse(p, out int v) ? v : 0)
                .ToList();
            if (parsed.Count == 0 || parsed.Any(p => p <= 0)) return false;

            number = rawNumber;
            parts = parsed;
            title = match.Groups["title"].Value.Trim();
            return true;
        }

        private static string BuildNumberPattern(OutlineEntry entry)
        {
            if (entry.NumberParts.Count == 0) return "";
            var tokens = entry.NumberParts.Select((_, index) => "{" + (index + 1) + "}");
            string pattern = string.Join(".", tokens);
            return pattern + ".";
        }

        private static int ParseHeadingLevel(string styleId, string styleName)
        {
            string id = styleId ?? "";
            string name = styleName ?? "";

            bool looksHeading = id.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("heading", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("TieuDe", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Tiêu đề", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Tieu de", StringComparison.OrdinalIgnoreCase);

            if (!looksHeading) return 0;

            foreach (char c in id)
            {
                if (c >= '1' && c <= '6') return c - '0';
            }
            foreach (char c in name)
            {
                if (c >= '1' && c <= '6') return c - '0';
            }
            return 1;
        }

        private static T? Mode<T>(IEnumerable<T> values) where T : struct
        {
            return values
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .Select(g => (T?)g.Key)
                .FirstOrDefault();
        }

        private static string? Mode(IEnumerable<string> values, StringComparer comparer)
        {
            return values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .GroupBy(v => v, comparer)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }
    }
}
