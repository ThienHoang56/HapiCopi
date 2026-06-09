#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public class WordFormatInfo
    {
        public string FontName { get; set; } = "Times New Roman";
        public float FontSize { get; set; } = 13.0f;
        public int ParentHeadingLevel { get; set; } = 0;
        public string StyleName { get; set; } = "Normal";
        public float LineSpacing { get; set; } = 1.5f;
        public bool IsFromActiveWord { get; set; } = false;
        public bool IsWordTarget { get; set; } = false;
        public System.Collections.Generic.Dictionary<string, string> MermaidImageCache { get; set; } = new System.Collections.Generic.Dictionary<string, string>();
    }

    public static class WordInterop
    {
        private const string WordProgId = "Word.Application";
        private static readonly Guid WordClsid = new Guid("000209FF-0000-0000-C000-000000000046");

        [DllImport("ole32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        public static WordFormatInfo GetCurrentFormatWithTimeout(int timeoutMs = 80)
        {
            WordFormatInfo result = null;
            var thread = new System.Threading.Thread(() =>
            {
                result = GetCurrentFormat();
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            
            if (!thread.Join(timeoutMs))
            {
                Program.Log($"COM Interop quá thời hạn ({timeoutMs}ms). Sử dụng Fallback.");
                return new WordFormatInfo { IsFromActiveWord = false };
            }
            
            return result ?? new WordFormatInfo { IsFromActiveWord = false };
        }

        public static WordFormatInfo GetCurrentFormat()
        {
            var info = new WordFormatInfo(); // Khởi tạo với giá trị mặc định làm phương án dự phòng (fallback)

            try
            {
                // Gọi API COM để lấy instance Word đang chạy
                object wordAppObj = null;
                try
                {
                    Guid clsid = WordClsid;
                    GetActiveObject(ref clsid, IntPtr.Zero, out wordAppObj);
                }
                catch
                {
                    // Lỗi xảy ra khi Word chưa được mở
                    return info;
                }

                if (wordAppObj == null) return info;

                dynamic wordApp = wordAppObj;
                
                // Kiểm tra xem có văn bản nào đang mở không
                if (wordApp.Windows == null || wordApp.Windows.Count == 0) return info;

                dynamic activeWindow = wordApp.ActiveWindow;
                if (activeWindow == null) return info;
                
                dynamic selection = activeWindow.Selection;
                if (selection == null) return info;

                // Đánh dấu kết nối thành công với Word desktop đang hoạt động
                info.IsFromActiveWord = true;

                // 1. Đọc Font chữ tại con trỏ chuột
                try
                {
                    string fontName = selection.Font?.Name;
                    if (!string.IsNullOrEmpty(fontName))
                    {
                        info.FontName = fontName;
                    }
                }
                catch {}

                // 2. Đọc Size chữ tại con trỏ chuột
                try
                {
                    float fontSize = selection.Font?.Size ?? 0f;
                    // Giá trị hợp lệ thường nằm trong khoảng hợp lý
                    if (fontSize > 0 && fontSize < 100)
                    {
                        info.FontSize = fontSize;
                    }
                }
                catch {}

                // 3. Đọc Line Spacing (giãn dòng)
                try
                {
                    dynamic paragraphFormat = selection.ParagraphFormat;
                    if (paragraphFormat != null)
                    {
                        int spacingRule = paragraphFormat.LineSpacingRule;
                        
                        // wdLineSpaceSingle = 0, wdLineSpace1pt5 = 1, wdLineSpaceDouble = 2, wdLineSpaceMultiple = 5
                        if (spacingRule == 0) info.LineSpacing = 1.0f;
                        else if (spacingRule == 1) info.LineSpacing = 1.5f;
                        else if (spacingRule == 2) info.LineSpacing = 2.0f;
                        else
                        {
                            float spacing = paragraphFormat.LineSpacing; // point
                            if (spacing > 0)
                            {
                                info.LineSpacing = spacing / 12.0f; // 12pt xắp xỉ 1 dòng
                            }
                        }
                    }
                }
                catch {}

                // 4. Quét ngược để tìm cấp độ Heading gần nhất
                try
                {
                    dynamic paragraphs = selection.Paragraphs;
                    if (paragraphs != null && paragraphs.Count > 0)
                    {
                        dynamic currentParagraph = paragraphs[1];
                        int limit = 50; // Chỉ quét tối đa 50 paragraph ngược lên để tránh đơ máy
                        while (currentParagraph != null && limit > 0)
                        {
                            dynamic style = currentParagraph.Style;
                            string styleName = style?.NameLocal;
                            if (!string.IsNullOrEmpty(styleName))
                            {
                                int level = ExtractHeadingLevel(styleName);
                                if (level > 0)
                                {
                                    info.ParentHeadingLevel = level;
                                    info.StyleName = styleName;
                                    break;
                                }
                            }
                            
                            try
                            {
                                currentParagraph = currentParagraph.Previous();
                            }
                            catch
                            {
                                currentParagraph = null;
                            }
                            limit--;
                        }
                    }
                }
                catch {}

                // 5. Tự động phát hiện tài liệu trống hoặc dùng font mặc định của MS Word mới tạo (Calibri/Aptos size <= 11)
                // Chúng ta sẽ tự động áp chuẩn học thuật của Việt Nam (Nghị định 30): Times New Roman 13pt, line spacing 1.5
                bool isDefaultWordFont = info.FontName == "Calibri" || 
                                         info.FontName == "Aptos" || 
                                         info.FontName == "Aptos Display" || 
                                         info.FontName == "Aptos Body";
                                         
                bool isDocEmpty = true;
                try
                {
                    dynamic activeDoc = wordApp.ActiveDocument;
                    if (activeDoc != null)
                    {
                        int charCount = activeDoc.Characters.Count;
                        // Một tài liệu trống hoàn toàn trong Word vẫn chứa khoảng 1-2 ký tự điều khiển dòng ẩn
                        isDocEmpty = charCount <= 2;
                    }
                }
                catch 
                {
                    isDocEmpty = false; // Nếu lỗi đọc thì giả định không trống để giữ an toàn
                }

                if (isDocEmpty || (isDefaultWordFont && info.FontSize <= 11.5f))
                {
                    Program.Log("Phát hiện tài liệu trống hoặc đang dùng font mặc định của Word. Áp dụng chuẩn học thuật: Times New Roman 13pt.");
                    info.FontName = "Times New Roman";
                    info.FontSize = 13.0f;
                    info.LineSpacing = 1.5f;
                }
                
                // 6. Ghi log kiểm tra thành công
                Program.Log($"Quét Word hoàn tất: Font={info.FontName}, Size={info.FontSize}, Spacing={info.LineSpacing}, ParentHeadingLevel={info.ParentHeadingLevel}");
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi trong WordInterop.GetCurrentFormat: {ex.Message}");
            }

            return info;
        }

        public static string GetSelectedText()
        {
            string selectedText = "";
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    object wordAppObj = null;
                    try
                    {
                        Guid clsid = WordClsid;
                        GetActiveObject(ref clsid, IntPtr.Zero, out wordAppObj);
                    }
                    catch
                    {
                        return;
                    }

                    if (wordAppObj == null) return;

                    dynamic wordApp = wordAppObj;
                    if (wordApp.Windows == null || wordApp.Windows.Count == 0) return;

                    dynamic activeWindow = wordApp.ActiveWindow;
                    if (activeWindow == null) return;

                    dynamic selection = activeWindow.Selection;
                    if (selection == null) return;

                    selectedText = selection.Text;
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi trong WordInterop.GetSelectedText: {ex.Message}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            return selectedText;
        }

        public static bool WriteTextStreaming(string formattedText)
        {
            if (string.IsNullOrEmpty(formattedText)) return false;

            bool success = false;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // Lấy instance Word đang hoạt động
                    object wordAppObj = null;
                    try
                    {
                        Guid clsid = WordClsid;
                        GetActiveObject(ref clsid, IntPtr.Zero, out wordAppObj);
                    }
                    catch
                    {
                        Program.Log("Không kết nối được với MS Word (Word chưa mở hoặc lỗi COM).");
                        return;
                    }

                    if (wordAppObj == null) return;

                    dynamic wordApp = wordAppObj;
                    if (wordApp.Windows == null || wordApp.Windows.Count == 0) return;

                    dynamic activeWindow = wordApp.ActiveWindow;
                    if (activeWindow == null) return;

                    dynamic selection = activeWindow.Selection;
                    if (selection == null) return;

                    // Xóa phần bôi đen hiện tại (để con trỏ tại vị trí bắt đầu)
                    selection.Delete();

                    // Chia thành các đoạn văn
                    var paragraphs = formattedText.Split(new[] { "\r\n", "\r" }, StringSplitOptions.None);

                    // Thiết lập tốc độ stream động dựa trên độ dài văn bản để tránh chậm trễ với văn bản siêu dài
                    int wordCount = formattedText.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    int sleepPerWord = 10;
                    if (wordCount > 500) sleepPerWord = 0;
                    else if (wordCount > 300) sleepPerWord = 1;
                    else if (wordCount > 100) sleepPerWord = 4;

                    Program.Log($"Bắt đầu stream {wordCount} từ vào Word (sleep = {sleepPerWord}ms/từ)...");

                    for (int i = 0; i < paragraphs.Length; i++)
                    {
                        string paragraph = paragraphs[i];
                        var words = paragraph.Split(' ');

                        for (int j = 0; j < words.Length; j++)
                        {
                            string word = words[j];
                            if (j > 0) selection.TypeText(" ");
                            selection.TypeText(word);

                            if (sleepPerWord > 0)
                            {
                                System.Threading.Thread.Sleep(sleepPerWord);
                            }
                        }

                        // Nếu không phải là đoạn cuối cùng, chèn ngắt đoạn
                        if (i < paragraphs.Length - 1)
                        {
                            selection.TypeParagraph();
                            if (sleepPerWord > 0)
                            {
                                System.Threading.Thread.Sleep(sleepPerWord * 2);
                            }
                        }
                    }

                    Program.Log("Stream định dạng văn bản hoàn tất.");
                    success = true;
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi trong WriteTextStreaming: {ex.Message}\n{ex.StackTrace}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            return success;
        }

        private static int ExtractHeadingLevel(string styleName)
        {
            // Bắt khớp "Heading 1", "Tiêu đề 1", "Heading 2"...
            var match = Regex.Match(styleName, @"(?:Heading|Tiêu\s+đề)\s+(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int level))
            {
                return level;
            }
            return 0;
        }
    }
}
