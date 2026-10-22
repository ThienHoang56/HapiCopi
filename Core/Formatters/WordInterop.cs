#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HapiCopi
{
    public class WordFormatInfo
    {
        public string FontName { get; set; } = null;
        public float FontSize { get; set; } = 0f;
        public int ParentHeadingLevel { get; set; } = 0;
        public string StyleName { get; set; } = "Normal";
        public float LineSpacing { get; set; } = 1.5f;
        public float FirstLineIndent { get; set; } = 0f;
        public float LeftIndent { get; set; } = 0f;
        public int Alignment { get; set; } = 0; // 0=Left, 1=Center, 2=Right, 3=Justify
        public bool IsInList { get; set; } = false;
        public string ListBulletChar { get; set; } = "";
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        public static bool IsWordRunning()
        {
            return FindWindow("OpusApp", null) != IntPtr.Zero;
        }

        public static Task<WordFormatInfo> ScanDocumentRulesAsync(int timeoutMs = 3000)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<WordFormatInfo>();
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var rules = ScanDocumentRules();
                    tcs.TrySetResult(rules);
                }
                catch (Exception ex)
                {
                    Program.Log($"[WordInterop] Lỗi quét tài liệu ngầm: {ex.Message}");
                    tcs.TrySetResult(new WordFormatInfo { IsFromActiveWord = false });
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return System.Threading.Tasks.Task.Run(async () =>
            {
                var completed = await System.Threading.Tasks.Task.WhenAny(tcs.Task, System.Threading.Tasks.Task.Delay(timeoutMs));
                if (completed == tcs.Task)
                {
                    return await tcs.Task;
                }
                Program.Log($"[WordInterop] Quét tài liệu quá thời hạn {timeoutMs}ms, dùng fallback.");
                return new WordFormatInfo { IsFromActiveWord = false };
            });
        }

        public static WordFormatInfo ScanDocumentRules()
        {
            var info = new WordFormatInfo();
            if (!IsWordRunning()) return info;

            object wordAppObj = null;
            try
            {
                Guid clsid = WordClsid;
                GetActiveObject(ref clsid, IntPtr.Zero, out wordAppObj);
            }
            catch
            {
                return info;
            }

            if (wordAppObj == null) return info;

            try
            {
                dynamic wordApp = wordAppObj;
                if (wordApp.Documents == null || wordApp.Documents.Count == 0) return info;

                dynamic doc = null;
                try { doc = wordApp.ActiveDocument; } catch { }
                if (doc == null && wordApp.Documents.Count > 0)
                {
                    try { doc = wordApp.Documents[1]; } catch { }
                }
                if (doc == null) return info;

                info.IsFromActiveWord = true;

                // 1. Đọc Style "Normal" của tài liệu
                try
                {
                    dynamic normalStyle = doc.Styles["Normal"];
                    if (normalStyle != null)
                    {
                        try { info.FontName = normalStyle.Font?.Name; } catch { }
                        try { info.FontSize = (float)(normalStyle.Font?.Size ?? 0f); } catch { }
                        try
                        {
                            dynamic pf = normalStyle.ParagraphFormat;
                            if (pf != null)
                            {
                                info.Alignment = (int)pf.Alignment;
                                info.FirstLineIndent = (float)pf.FirstLineIndent;
                                info.LeftIndent = (float)pf.LeftIndent;
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // 2. Lấy mẫu đại diện các đoạn văn thực tế trong tài liệu (Representative Sampling)
                try
                {
                    dynamic paragraphs = doc.Paragraphs;
                    int count = (int)(paragraphs?.Count ?? 0);
                    int sampled = 0;

                    for (int i = 1; i <= Math.Min(count, 30) && sampled < 8; i++)
                    {
                        try
                        {
                            dynamic p = paragraphs[i];
                            string text = p?.Range?.Text;
                            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 5) continue;

                            dynamic pf = p.Format;
                            if (pf != null)
                            {
                                float firstLine = (float)pf.FirstLineIndent;
                                int align = (int)pf.Alignment;
                                float left = (float)pf.LeftIndent;

                                if (firstLine > 0 && info.FirstLineIndent <= 0)
                                {
                                    info.FirstLineIndent = firstLine;
                                }
                                if (align == 3) // Justify
                                {
                                    info.Alignment = 3;
                                }
                                else if (info.Alignment == 0 && align != 0)
                                {
                                    info.Alignment = align;
                                }
                                if (left > 0 && info.LeftIndent <= 0)
                                {
                                    info.LeftIndent = left;
                                }

                                string fName = p.Range?.Font?.Name;
                                if (!string.IsNullOrEmpty(fName) && (string.IsNullOrEmpty(info.FontName) || info.FontName == "Normal"))
                                {
                                    info.FontName = fName;
                                }
                                float fSize = (float)(p.Range?.Font?.Size ?? 0f);
                                if (fSize > 0 && info.FontSize <= 0)
                                {
                                    info.FontSize = fSize;
                                }
                            }
                            sampled++;
                        }
                        catch { }
                    }
                }
                catch { }

                // 3. Đọc danh sách / bullet nếu có trong tài liệu
                try
                {
                    dynamic lists = doc.Lists;
                    if (lists != null && lists.Count > 0)
                    {
                        info.IsInList = true;
                        dynamic firstList = lists[1];
                        if (firstList != null && firstList.ListParagraphs != null && firstList.ListParagraphs.Count > 0)
                        {
                            dynamic lp = firstList.ListParagraphs[1];
                            string listStr = lp?.Range?.ListFormat?.ListString;
                            if (!string.IsNullOrEmpty(listStr))
                            {
                                info.ListBulletChar = listStr.Trim();
                            }
                        }
                    }
                }
                catch { }

                // 4. Kiểm tra Selection nếu có
                try
                {
                    dynamic activeWin = wordApp.ActiveWindow;
                    dynamic sel = activeWin?.Selection;
                    if (sel != null)
                    {
                        dynamic range = sel.Range;
                        if (range != null && range.ListFormat != null && (int)range.ListFormat.ListType > 0)
                        {
                            info.IsInList = true;
                            string s = range.ListFormat.ListString;
                            if (!string.IsNullOrEmpty(s)) info.ListBulletChar = s.Trim();
                        }
                    }
                }
                catch { }

                // Thiết lập fallback chuẩn văn bản tiếng Việt nếu thiếu trường
                if (string.IsNullOrEmpty(info.FontName)) info.FontName = "Times New Roman";
                if (info.FontSize <= 0) info.FontSize = 13.0f;
                if (info.FirstLineIndent <= 0) info.FirstLineIndent = 28.35f; // 1.0cm chuẩn TCVN
                if (info.Alignment == 0) info.Alignment = 3; // Mặc định Justify
                if (info.LineSpacing <= 0) info.LineSpacing = 1.5f;

                Program.Log($"[WordInterop] Quét tài liệu thành công: Font='{info.FontName}', Size={info.FontSize}pt, Indent={info.FirstLineIndent:0.#}pt, Align={info.Alignment}, Bullet='{info.ListBulletChar}'");
            }
            catch (Exception ex)
            {
                Program.Log($"[WordInterop] Lỗi khi quét tài liệu: {ex.Message}");
            }

            return info;
        }

        public static WordFormatInfo GetCurrentFormatWithTimeout(int timeoutMs = 80)
        {
            if (!IsWordRunning())
            {
                return new WordFormatInfo { IsFromActiveWord = false };
            }

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

            if (!IsWordRunning()) return info;

            object wordAppObj = null;
            try
            {
                // Gọi API COM để lấy instance Word đang chạy
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

                        // Căn lề: 0=Left, 1=Center, 2=Right, 3=Justify
                        try { info.Alignment = (int)paragraphFormat.Alignment; } catch { }

                        // Thụt lề đầu dòng & thụt lề trái
                        try { info.FirstLineIndent = (float)paragraphFormat.FirstLineIndent; } catch { }
                        try { info.LeftIndent = (float)paragraphFormat.LeftIndent; } catch { }
                    }
                }
                catch {}

                // 3b. Đọc ListFormat nếu con trỏ đang nằm trong danh sách
                try
                {
                    dynamic range = selection.Range;
                    if (range != null && range.ListFormat != null)
                    {
                        dynamic listFormat = range.ListFormat;
                        int listType = (int)listFormat.ListType;
                        if (listType > 0) // wdListBullet, wdListSimpleNumbering, etc.
                        {
                            info.IsInList = true;
                            string listString = listFormat.ListString;
                            if (!string.IsNullOrEmpty(listString))
                            {
                                info.ListBulletChar = listString.Trim();
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


                
                // 6. Ghi log kiểm tra thành công
                Program.Log($"Quét Word hoàn tất: Font={info.FontName}, Size={info.FontSize}, Spacing={info.LineSpacing}, ParentHeadingLevel={info.ParentHeadingLevel}");
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi trong WordInterop.GetCurrentFormat: {ex.Message}");
            }
            finally
            {
                if (wordAppObj != null)
                {
                    try { Marshal.ReleaseComObject(wordAppObj); } catch { }
                }
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

        public static void StartLiveGhostTyping(System.Collections.Concurrent.BlockingCollection<string> chunkQueue)
        {
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
                        Program.Log("Ghost Typing: Không kết nối được với MS Word.");
                        return;
                    }

                    if (wordAppObj == null) return;

                    dynamic wordApp = wordAppObj;
                    if (wordApp.Windows == null || wordApp.Windows.Count == 0) return;
                    dynamic activeWindow = wordApp.ActiveWindow;
                    if (activeWindow == null) return;
                    dynamic selection = activeWindow.Selection;
                    if (selection == null) return;

                    // Xóa phần bôi đen
                    selection.Delete();

                    while (!chunkQueue.IsCompleted)
                    {
                        if (chunkQueue.TryTake(out string chunk, 100))
                        {
                            if (string.IsNullOrEmpty(chunk)) continue;
                            
                            string[] parts = chunk.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
                            for(int i = 0; i < parts.Length; i++)
                            {
                                if (parts[i].Length > 0)
                                {
                                    selection.TypeText(parts[i]);
                                }
                                if (i < parts.Length - 1)
                                {
                                    selection.TypeParagraph();
                                }
                            }
                        }
                    }
                    Program.Log("Ghost Typing hoàn tất.");
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi Ghost Typing STA: {ex.Message}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
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

        public static WordFormulaInfo GetSelectedFormulaInfo()
        {
            WordFormulaInfo result = null;
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

                    int selType = 0;
                    try { selType = (int)selection.Type; } catch {}
                    // wdSelectionIP = 1 (con trỏ đang ở trong text, không chọn shape nào)
                    if (selType == 1) return;

                    // 1. Kiểm tra InlineShapes (ảnh trong dòng)
                    if (selection.InlineShapes != null && selection.InlineShapes.Count > 0)
                    {
                        dynamic shape = selection.InlineShapes[1];
                        string alt = shape.AlternativeText ?? shape.Title ?? "";
                        if (alt.StartsWith("[HapiCopi:Math]"))
                        {
                            string formula = alt.Substring("[HapiCopi:Math]".Length).Trim();
                            formula = System.Net.WebUtility.HtmlDecode(formula);
                            float fontSize = 11f;
                            try { fontSize = selection.Font?.Size ?? 11f; } catch {}
                            result = new WordFormulaInfo
                            {
                                Formula = formula,
                                WidthPt = (float)shape.Width,
                                HeightPt = (float)shape.Height,
                                FontSize = fontSize,
                                IsInline = true
                            };
                            return;
                        }
                    }

                    // 2. Kiểm tra ShapeRange (ảnh trôi nổi)
                    if (selection.ShapeRange != null && selection.ShapeRange.Count > 0)
                    {
                        dynamic shape = selection.ShapeRange[1];
                        string alt = shape.AlternativeText ?? shape.Title ?? "";
                        if (alt.StartsWith("[HapiCopi:Math]"))
                        {
                            string formula = alt.Substring("[HapiCopi:Math]".Length).Trim();
                            formula = System.Net.WebUtility.HtmlDecode(formula);
                            float fontSize = 11f;
                            try { fontSize = selection.Font?.Size ?? 11f; } catch {}
                            result = new WordFormulaInfo
                            {
                                Formula = formula,
                                WidthPt = (float)shape.Width,
                                HeightPt = (float)shape.Height,
                                FontSize = fontSize,
                                IsInline = false
                            };
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"[WordInterop] Lỗi đọc công thức: {ex.Message}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join(150);
            return result;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void ActivateWordWindow()
        {
            try
            {
                object wordAppObj = null;
                Guid clsid = WordClsid;
                GetActiveObject(ref clsid, IntPtr.Zero, out wordAppObj);
                if (wordAppObj != null)
                {
                    dynamic wordApp = wordAppObj;
                    if (wordApp.ActiveWindow != null)
                    {
                        IntPtr hwnd = (IntPtr)wordApp.ActiveWindow.Hwnd;
                        if (hwnd != IntPtr.Zero)
                        {
                            SetForegroundWindow(hwnd);
                        }
                    }
                }
            }
            catch {}
        }

        public static bool ReplaceSelectedFormulaInWord(string pngBase64, string newFormula, float widthPt, float heightPt, float vAlignPt)
        {
            bool success = false;
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
                    catch { return; }

                    if (wordAppObj == null) return;
                    dynamic wordApp = wordAppObj;
                    if (wordApp.Windows == null || wordApp.Windows.Count == 0) return;
                    dynamic activeWindow = wordApp.ActiveWindow;
                    if (activeWindow == null) return;
                    dynamic selection = activeWindow.Selection;
                    if (selection == null) return;

                    dynamic targetShape = null;
                    bool isInline = true;

                    if (selection.InlineShapes != null && selection.InlineShapes.Count > 0)
                    {
                        targetShape = selection.InlineShapes[1];
                        isInline = true;
                    }
                    else if (selection.ShapeRange != null && selection.ShapeRange.Count > 0)
                    {
                        targetShape = selection.ShapeRange[1];
                        isInline = false;
                    }

                    if (targetShape == null) return;

                    string rawBase64 = pngBase64;
                    if (rawBase64.Contains(",")) rawBase64 = rawBase64.Substring(rawBase64.IndexOf(",") + 1);
                    byte[] bytes = Convert.FromBase64String(rawBase64);
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HapiCopi_Formula_Update.png");
                    System.IO.File.WriteAllBytes(tempFile, bytes);

                    dynamic doc = activeWindow.Document;

                    if (isInline)
                    {
                        dynamic range = targetShape.Range;
                        int start = (int)range.Start;
                        targetShape.Delete();
                        dynamic insertRange = doc.Range(start, start);
                        dynamic newShape = insertRange.InlineShapes.AddPicture(tempFile, false, true);
                        newShape.AlternativeText = "[HapiCopi:Math] " + newFormula;
                        newShape.Title = "[HapiCopi:Math] " + newFormula;
                        if (widthPt > 0) newShape.Width = widthPt;
                        if (heightPt > 0) newShape.Height = heightPt;
                        if (vAlignPt != 0)
                        {
                            try { newShape.Range.Font.Position = (float)Math.Round(vAlignPt); } catch {}
                        }
                        newShape.Select();
                    }
                    else
                    {
                        dynamic anchor = targetShape.Anchor;
                        int start = (int)anchor.Start;
                        targetShape.Delete();
                        dynamic insertRange = doc.Range(start, start);
                        dynamic newShape = insertRange.InlineShapes.AddPicture(tempFile, false, true);
                        newShape.AlternativeText = "[HapiCopi:Math] " + newFormula;
                        newShape.Title = "[HapiCopi:Math] " + newFormula;
                        if (widthPt > 0) newShape.Width = widthPt;
                        if (heightPt > 0) newShape.Height = heightPt;
                        newShape.Select();
                    }

                    success = true;
                    Program.Log("[WordInterop] Đã thay thế công thức trực tiếp trong Word thành công!");
                }
                catch (Exception ex)
                {
                    Program.Log($"[WordInterop] Lỗi thay thế công thức: {ex.Message}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join(2500);
            return success;
        }
    }

    public class WordFormulaInfo
    {
        public string Formula { get; set; } = "";
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
        public float FontSize { get; set; } = 11f;
        public bool IsInline { get; set; } = true;
    }
}
