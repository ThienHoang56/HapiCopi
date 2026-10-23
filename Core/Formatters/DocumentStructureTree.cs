using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace HapiCopi
{
    /// <summary>
    /// Cây Cấu Trúc Tài Liệu (Document Structure Tree).
    /// Đại diện cho toàn bộ mô hình cây phân cấp được đo đạc thực tế từ văn bản Word:
    /// - Nhánh Thân bài (Normal / Paragraph Properties)
    /// - Nhánh Phân cấp Tiêu đề (Headings H1..H6) + Sườn đề mục thực tế (Outline)
    /// - Nhánh Danh sách Đa cấp (Lists Level 1..9, Bullet / Numbering)
    /// - Nhánh Bảng biểu (Tables)
    /// - Ngữ cảnh Điểm dán Con trỏ (Active Insertion Context)
    ///
    /// Nguyên tắc: mọi giá trị ở đây phải được ĐO từ file .docx thật.
    /// Không suy đoán, không lấy thông số của một file mẫu làm mặc định hệ thống.
    /// </summary>
    public class DocumentStructureTree
    {
        public bool IsValid { get; set; } = false;
        public int SectionIndex { get; set; } = 1;
        public string? DocumentTitle { get; set; }
        public string? SourcePath { get; set; }
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Khuôn thân bài chuẩn của tài liệu hoặc Section hiện tại.</summary>
        public ParagraphStyleNode BodyStyle { get; set; } = new();

        /// <summary>Cây kiểu mẫu Tiêu đề phân cấp theo cấp độ (Level 1..6).</summary>
        public Dictionary<int, HeadingStyleNode> Headings { get; set; } = new();

        /// <summary>Cây kiểu mẫu Danh sách theo từng cấp độ (Level 1..9).</summary>
        public Dictionary<int, ListLevelStyleNode> Lists { get; set; } = new();

        /// <summary>Danh sách gạch đầu dòng đo được (tách riêng khỏi danh sách số thứ tự).</summary>
        public Dictionary<int, ListLevelStyleNode> BulletLists { get; set; } = new();

        /// <summary>Danh sách số thứ tự đo được (tách riêng khỏi danh sách gạch đầu dòng).</summary>
        public Dictionary<int, ListLevelStyleNode> OrderedLists { get; set; } = new();

        /// <summary>
        /// Sườn đề mục thực tế theo đúng thứ tự xuất hiện trong tài liệu.
        /// Ví dụ: 1. INTRODUCTION, 2.1. Industry, 4.3. Brand issue...
        /// Dùng để đồng bộ cấp bậc và đánh số lại khi dán nội dung từ AI.
        /// </summary>
        public List<OutlineEntry> Outline { get; set; } = new();

        /// <summary>
        /// Các đề mục đã được đánh số trong phiên làm việc hiện tại (chưa nằm trong file mẫu).
        /// Chỉ giữ trong RAM để lần copy kế tiếp biết số vừa cấp, tránh lặp 4.4 mãi.
        /// Không lưu xuống JSON: hồ sơ phải phản ánh đúng file mẫu.
        /// </summary>
        [JsonIgnore]
        public List<OutlineEntry> ProjectedOutline { get; set; } = new();

        /// <summary>
        /// Bộ đếm số cuối cùng theo từng nhánh cha trong phiên (ví dụ "4" -> 4).
        /// Giữ riêng để không phụ thuộc vào việc danh sách ProjectedOutline bị giới hạn độ dài.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, int> ProjectedSiblingCounters { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Khuôn số thứ tự đo được cho từng cấp (ví dụ "{1}." hoặc "{1}.{2}.").
        /// Chỉ chứa những cấp thực sự có số trong tài liệu.
        /// </summary>
        public Dictionary<int, string> HeadingNumberFormats { get; set; } = new();

        /// <summary>Tự động đánh số lại đề mục theo sườn tài liệu (4.5 -> 4.4).</summary>
        public bool RenumberOutline { get; set; } = true;

        /// <summary>Kiểu mẫu Bảng biểu đo được trong tài liệu.</summary>
        public TableStyleNode TableStyle { get; set; } = new();

        /// <summary>Ngữ cảnh kỹ thuật tại điểm chèn con trỏ hiện tại trong Word.</summary>
        public InsertionContextNode Context { get; set; } = new();

        /// <summary>
        /// Ánh xạ độ sâu số đề mục (4.5 => 2) sang cấp Heading thật của tài liệu.
        /// Dùng số đo từ Outline; nếu tài liệu không có sườn thì fallback về chính độ sâu.
        /// </summary>
        public int MapLevelByNumberDepth(int depth)
        {
            if (depth <= 0) return 1;

            var measured = Outline
                .Where(o => o.Level >= 1 && o.Level <= 6 && o.NumberParts.Count == depth)
                .GroupBy(o => o.Level)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .FirstOrDefault();

            if (measured != null) return measured.Key;
            return Math.Clamp(depth, 1, 6);
        }

        /// <summary>Lấy khuôn số của một cấp; nếu chưa đo được thì dựng theo cấp.</summary>
        public string GetHeadingNumberFormat(int level)
        {
            if (HeadingNumberFormats.TryGetValue(level, out var fmt) && !string.IsNullOrWhiteSpace(fmt))
            {
                return fmt;
            }

            var parts = new List<string>();
            for (int i = 1; i <= Math.Clamp(level, 1, 6); i++)
            {
                parts.Add("{" + i + "}");
            }
            return string.Join(".", parts) + ".";
        }

        /// <summary>
        /// Lấy khuôn danh sách theo loại (bullet / ordered) và cấp.
        /// Ưu tiên số đo riêng; fallback sang bảng Lists chung; cuối cùng mới dựng an toàn.
        /// </summary>
        public ListLevelStyleNode GetListStyle(bool ordered, int level)
        {
            level = Math.Clamp(level, 1, 9);

            var specific = ordered ? OrderedLists : BulletLists;
            if (specific.TryGetValue(level, out var measured))
            {
                return measured;
            }

            if (Lists.TryGetValue(level, out var common))
            {
                return common;
            }

            // Fallback có kiểm soát: chỉ dùng khi tài liệu chưa từng có danh sách.
            // Mức 1 dùng 36pt/18pt (0.63cm/0.32cm) — đây là kích thước Word mặc định,
            // không phải số liệu của một file mẫu cụ thể.
            float left = 36f + (level - 1) * 18f;
            return new ListLevelStyleNode
            {
                Level = level,
                BulletOrPrefix = ordered ? "%1." : "-",
                IsOrdered = ordered,
                LeftIndent = left,
                HangingIndent = 18f
            };
        }

        /// <summary>
        /// Tạo cây mặc định an toàn cho văn bản khi không kết nối được Word.
        /// Tuyệt đối không giả định thụt lề 1cm hay ép Justify mù quáng.
        /// </summary>
        public static DocumentStructureTree CreateDefaultSafeTree()
        {
            var tree = new DocumentStructureTree
            {
                IsValid = false,
                BodyStyle = new ParagraphStyleNode
                {
                    FontName = "Times New Roman",
                    FontSize = 13.0f,
                    FirstLineIndent = 0f,
                    Alignment = 0, // Căn trái an toàn (Left)
                    LineSpacing = 1.15f
                }
            };

            tree.Headings[1] = new HeadingStyleNode { Level = 1, FontName = "Times New Roman", FontSize = 16f, Bold = true, SpaceBefore = 12f, SpaceAfter = 6f };
            tree.Headings[2] = new HeadingStyleNode { Level = 2, FontName = "Times New Roman", FontSize = 14f, Bold = true, SpaceBefore = 6f, SpaceAfter = 3f };
            tree.Headings[3] = new HeadingStyleNode { Level = 3, FontName = "Times New Roman", FontSize = 13f, Bold = true, SpaceBefore = 3f, SpaceAfter = 2f };

            tree.Lists[1] = new ListLevelStyleNode { Level = 1, BulletOrPrefix = "-", LeftIndent = 36.0f, HangingIndent = 18.0f };
            tree.Lists[2] = new ListLevelStyleNode { Level = 2, BulletOrPrefix = "-", LeftIndent = 54.0f, HangingIndent = 18.0f };

            return tree;
        }
    }

    /// <summary>
    /// Một đề mục thực tế đo được trong tài liệu (sườn tài liệu).
    /// </summary>
    public class OutlineEntry
    {
        public int Level { get; set; } = 1;

        /// <summary>Số đề mục không kèm dấu chấm cuối, ví dụ "4.3".</summary>
        public string Number { get; set; } = "";

        /// <summary>Các thành phần số, ví dụ [4, 3].</summary>
        public List<int> NumberParts { get; set; } = new();

        /// <summary>Tiêu đề không kèm số, ví dụ "Brand issue".</summary>
        public string Title { get; set; } = "";

        /// <summary>Toàn bộ text đo được, ví dụ "4.3. Brand issue".</summary>
        public string Text { get; set; } = "";

        /// <summary>True nếu số thứ tự do Word sinh từ numbering.xml (không nằm trong text).</summary>
        public bool NumberIsAutomatic { get; set; }
    }

    /// <summary>
    /// Thuộc tính của một đoạn văn bản thân bài (Đo đạc từ văn bản thực tế).
    /// </summary>
    public class ParagraphStyleNode
    {
        public string FontName { get; set; } = "Times New Roman";
        public float FontSize { get; set; } = 13.0f;
        public float FirstLineIndent { get; set; } = 0f; // Điểm thụt đầu dòng (points)
        public float LeftIndent { get; set; } = 0f;      // Thụt lề trái (points)
        public int Alignment { get; set; } = 0;          // 0=Left, 1=Center, 2=Right, 3=Justify
        public float LineSpacing { get; set; } = 1.15f;  // Giãn dòng (1.0, 1.15, 1.5, 2.0...)
        public float SpaceBefore { get; set; } = 0f;
        public float SpaceAfter { get; set; } = 0f;

        public string ToCssStyle()
        {
            string font = !string.IsNullOrEmpty(FontName) ? $"font-family: '{FontName}';" : "";
            string size = FontSize > 0 ? $"font-size: {FontSize:0.#}pt;" : "";
            string align = Alignment switch
            {
                1 => "text-align: center;",
                2 => "text-align: right;",
                3 => "text-align: justify;",
                _ => "text-align: left;"
            };
            string indent = FirstLineIndent > 0 ? $"text-indent: {FirstLineIndent:0.##}pt;" : "";
            string left = LeftIndent > 0 ? $"margin-left: {LeftIndent:0.##}pt;" : "";
            string line = LineSpacing > 0 ? $"line-height: {LineSpacing:0.##};" : "";
            string spacing = (SpaceBefore > 0 || SpaceAfter > 0) ? $"margin-top: {SpaceBefore:0.#}pt; margin-bottom: {SpaceAfter:0.#}pt;" : "";

            return $"{font} {size} {align} {indent} {left} {line} {spacing}".Trim();
        }
    }

    /// <summary>
    /// Thuộc tính kiểu dáng Tiêu đề cho từng cấp phân cấp (H1..H6).
    /// Ràng buộc kỹ thuật: Tiêu đề không bao giờ kế thừa thụt đầu dòng của đoạn thân bài.
    /// </summary>
    public class HeadingStyleNode
    {
        public int Level { get; set; } = 1;
        public string FontName { get; set; } = "Times New Roman";
        public float FontSize { get; set; } = 14.0f;
        public bool Bold { get; set; } = true;
        public bool Italic { get; set; } = false;
        public string? ColorHex { get; set; }
        public int Alignment { get; set; } = 0;
        public float SpaceBefore { get; set; } = 6.0f;
        public float SpaceAfter { get; set; } = 3.0f;

        public string ToCssStyle()
        {
            string font = !string.IsNullOrEmpty(FontName) ? $"font-family: '{FontName}';" : "";
            string size = FontSize > 0 ? $"font-size: {FontSize:0.#}pt;" : "";
            string weight = Bold ? "font-weight: bold;" : "font-weight: normal;";
            string italic = Italic ? "font-style: italic;" : "";
            string color = !string.IsNullOrEmpty(ColorHex) ? $"color: {ColorHex};" : "";
            string align = Alignment switch
            {
                1 => "text-align: center;",
                2 => "text-align: right;",
                3 => "text-align: justify;",
                _ => "text-align: left;"
            };
            string margin = $"margin-top: {SpaceBefore:0.#}pt; margin-bottom: {SpaceAfter:0.#}pt;";
            // mso-outline-level giúp Word giữ đúng cấp đề mục (Navigation/TOC) khi dán clipboard,
            // song song với việc vẫn áp direct formatting để khớp typography thực tế của tác giả.
            string outline = $"mso-outline-level: {Math.Clamp(Level, 1, 6)};";
            // Ràng buộc kiến trúc: Tiêu đề không bao giờ thụt đầu dòng
            return $"{font} {size} {weight} {italic} {color} {align} {margin} {outline} text-indent: 0pt;".Trim();
        }
    }

    /// <summary>
    /// Thuộc tính kiểu dáng Danh sách cho từng cấp thụt lùi (Level 1..9).
    /// </summary>
    public class ListLevelStyleNode
    {
        public int Level { get; set; } = 1;
        public string BulletOrPrefix { get; set; } = "-";
        public string NumberFormat { get; set; } = "decimal"; // decimal, lowerLetter, upperLetter, lowerRoman, upperRoman...
        public bool IsOrdered { get; set; } = false;
        public float LeftIndent { get; set; } = 36.0f;    // 720 dxa = 36 pt
        public float HangingIndent { get; set; } = 18.0f; // 360 dxa = 18 pt
        public float SpaceBefore { get; set; } = 0f;
        public float SpaceAfter { get; set; } = 0f;

        public string ToCssStyle(ParagraphStyleNode? baseStyle = null)
        {
            string font = baseStyle != null && !string.IsNullOrEmpty(baseStyle.FontName) ? $"font-family: '{baseStyle.FontName}';" : "";
            string size = baseStyle != null && baseStyle.FontSize > 0 ? $"font-size: {baseStyle.FontSize:0.#}pt;" : "";
            string line = baseStyle != null && baseStyle.LineSpacing > 0 ? $"line-height: {baseStyle.LineSpacing:0.##};" : "";
            string align = baseStyle != null && baseStyle.Alignment == 3 ? "text-align: justify;" : "text-align: left;";
            string margin = $"margin-left: {LeftIndent:0.#}pt;";
            string indent = $"text-indent: -{HangingIndent:0.#}pt;";
            string spacing = (SpaceBefore > 0 || SpaceAfter > 0) ? $"margin-top: {SpaceBefore:0.#}pt; margin-bottom: {SpaceAfter:0.#}pt;" : "";

            return $"{font} {size} {line} {align} {margin} {indent} {spacing}".Trim();
        }
    }

    /// <summary>
    /// Thuộc tính kiểu dáng Bảng biểu đo được trong tài liệu Word.
    /// </summary>
    public class TableStyleNode
    {
        public string BorderColor { get; set; } = "#cccccc";
        public float BorderWidthPt { get; set; } = 0.75f;
        public int Alignment { get; set; } = 0; // 0=Left, 1=Center, 2=Right
        public bool HasFixedLayout { get; set; } = true;
    }

    /// <summary>
    /// Ngữ cảnh tức thời tại vị trí chèn con trỏ trong Microsoft Word.
    /// </summary>
    public class InsertionContextNode
    {
        public int ParentHeadingLevel { get; set; } = 0;
        public string ParentHeadingTitle { get; set; } = "";
        public bool IsWithinTable { get; set; } = false;
        public bool IsWithinList { get; set; } = false;
        public int ActiveListLevel { get; set; } = 1;
    }
}
