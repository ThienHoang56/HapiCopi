using System;
using System.Windows.Forms;

namespace HapiCopi
{
    public static class ClipboardSimulator
    {
        private static string _originalText = "";
        private static string _originalHtml = "";
        private static bool _hasOriginalText = false;
        private static bool _hasOriginalHtml = false;

        public static string OriginalText => _originalText;
        public static string OriginalHtml => _originalHtml;

        /// <summary>
        /// Giả lập sao chép văn bản đang bôi đen bằng Ctrl+C và đọc văn bản từ clipboard.
        /// </summary>
        public static string GetSelectedTextViaClipboard()
        {
            string selectedText = "";

            // Phải chạy trên luồng STA vì Windows Forms Clipboard yêu cầu STA Thread
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // 1. Sao lưu clipboard hiện tại của người dùng
                    _hasOriginalText = false;
                    _hasOriginalHtml = false;

                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            _originalText = Clipboard.GetText();
                            _hasOriginalText = true;
                        }
                        if (Clipboard.ContainsText(TextDataFormat.Html))
                        {
                            _originalHtml = Clipboard.GetText(TextDataFormat.Html);
                            _hasOriginalHtml = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Log($"Lỗi khi sao lưu clipboard gốc: {ex.Message}");
                    }

                    // 2. Xóa sạch clipboard để kiểm tra dữ liệu mới
                    Clipboard.Clear();
                    System.Threading.Thread.Sleep(50);

                    // 3. Giả lập nhấn Ctrl + C để copy văn bản đang chọn
                    SendKeys.SendWait("^c");

                    // 4. Chờ clipboard nhận dữ liệu (tối đa 1 giây)
                    int retries = 20;
                    while (retries > 0)
                    {
                        if (Clipboard.ContainsText())
                        {
                            selectedText = Clipboard.GetText();
                            if (!string.IsNullOrEmpty(selectedText)) break;
                        }
                        System.Threading.Thread.Sleep(50);
                        retries--;
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi trong GetSelectedTextViaClipboard: {ex.Message}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            return selectedText;
        }

        /// <summary>
        /// Đưa văn bản định dạng vào clipboard, dán vào ứng dụng bằng Ctrl+V, và khôi phục lại clipboard gốc.
        /// </summary>
        public static bool WriteTextViaClipboard(string formattedText)
        {
            if (string.IsNullOrEmpty(formattedText)) return false;

            bool success = false;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // 1. Đưa văn bản định dạng mới vào clipboard
                    Clipboard.SetText(formattedText);
                    System.Threading.Thread.Sleep(50);

                    // 2. Giả lập nhấn Ctrl + V để dán đè lên đoạn bôi đen
                    try
                    {
                        HapiCopiApplicationContext.IsSimulatingPaste = true;
                        SendKeys.SendWait("^v");
                    }
                    finally
                    {
                        System.Threading.Thread.Sleep(100);
                        HapiCopiApplicationContext.IsSimulatingPaste = false;
                    }

                    // Chờ ứng dụng xử lý dán xong trước khi dọn/khôi phục clipboard
                    System.Threading.Thread.Sleep(250);

                    // 3. Khôi phục lại dữ liệu clipboard ban đầu của người dùng
                    RestoreClipboardInternal();
                    success = true;
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi trong WriteTextViaClipboard: {ex.Message}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            return success;
        }

        /// <summary>
        /// Đưa đồng thời văn bản thô và HTML định dạng Office vào clipboard, dán vào ứng dụng bằng Ctrl+V, và khôi phục lại clipboard gốc.
        /// </summary>
        public static bool WriteOfficeHtmlViaClipboard(string plainText, string officeHtml)
        {
            if (string.IsNullOrEmpty(officeHtml)) return false;

            bool success = false;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // 1. Đẩy đồng thời văn bản thô và HTML định dạng Office vào clipboard
                    ClipboardHelper.SetOfficeClipboard(plainText, officeHtml);
                    System.Threading.Thread.Sleep(50);

                    // 2. Giả lập nhấn Ctrl + V để dán đè lên đoạn bôi đen
                    try
                    {
                        HapiCopiApplicationContext.IsSimulatingPaste = true;
                        SendKeys.SendWait("^v");
                    }
                    finally
                    {
                        System.Threading.Thread.Sleep(100);
                        HapiCopiApplicationContext.IsSimulatingPaste = false;
                    }

                    // Chờ ứng dụng xử lý dán xong trước khi dọn/khôi phục clipboard
                    System.Threading.Thread.Sleep(250);

                    // 3. Khôi phục lại dữ liệu clipboard ban đầu của người dùng
                    RestoreClipboardInternal();
                    success = true;
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi trong WriteOfficeHtmlViaClipboard: {ex.Message}");
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            return success;
        }

        /// <summary>
        /// Khôi phục lại clipboard gốc (sử dụng khi quá trình định dạng bị hủy hoặc không thay đổi).
        /// </summary>
        public static void RestoreClipboard()
        {
            var thread = new System.Threading.Thread(() =>
            {
                RestoreClipboardInternal();
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        private static void RestoreClipboardInternal()
        {
            try
            {
                Clipboard.Clear();
                if (_hasOriginalText && _hasOriginalHtml)
                {
                    ClipboardHelper.SetOfficeClipboard(_originalText, _originalHtml);
                }
                else if (_hasOriginalText)
                {
                    Clipboard.SetText(_originalText);
                }
                Program.Log("Đã khôi phục thành công clipboard gốc của người dùng.");
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi khôi phục clipboard gốc: {ex.Message}");
            }
        }
    }
}
