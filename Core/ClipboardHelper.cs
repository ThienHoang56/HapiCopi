using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HapiCopi
{
    public static class ClipboardHelper
    {
        private const string MarkerFormat = "HapiCopiProcessed";

        /// <summary>
        /// Kiểm tra xem dữ liệu trong Clipboard đã được HapiCopi xử lý chưa.
        /// Tự động chạy trên luồng STA nếu luồng gọi hiện tại là MTA.
        /// </summary>
        public static bool IsAlreadyProcessed()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                bool result = false;
                var thread = new Thread(() => result = IsAlreadyProcessed());
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                return result;
            }

            try
            {
                IDataObject? dataObj = Clipboard.GetDataObject();
                if (dataObj != null)
                {
                    return dataObj.GetDataPresent(MarkerFormat);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"Lỗi kiểm tra Marker Clipboard: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Kiểm tra xem dữ liệu có nguồn gốc từ trình soạn thảo VS Code Editor hay không.
        /// Tự động chạy trên luồng STA nếu luồng gọi hiện tại là MTA.
        /// </summary>
        public static bool IsVscodeEditorCopy()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                bool result = false;
                var thread = new Thread(() => result = IsVscodeEditorCopy());
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                return result;
            }

            try
            {
                IDataObject? dataObj = Clipboard.GetDataObject();
                if (dataObj != null)
                {
                    return dataObj.GetDataPresent("vscode-editor-data");
                }
            }
            catch {}
            return false;
        }

        /// <summary>
        /// Đọc văn bản thô từ Clipboard một cách an toàn.
        /// Tự động chạy trên luồng STA nếu luồng gọi hiện tại là MTA.
        /// </summary>
        public static string GetPlainText()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                string result = string.Empty;
                var thread = new Thread(() => result = GetPlainText());
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                return result;
            }

            // Thực hiện thử lại (retry loop) vì Clipboard có thể đang bị ứng dụng khác khóa tạm thời
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    {
                        return Clipboard.GetText(TextDataFormat.UnicodeText);
                    }
                    return string.Empty;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(10); // Chờ 10ms rồi thử lại
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi đọc Clipboard: {ex.Message}");
                    break;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Đẩy đồng thời văn bản thô (giữ nguyên) và HTML (định dạng Office) vào Clipboard cùng Marker chống đệ quy.
        /// Tự động chạy trên luồng STA nếu luồng gọi hiện tại là MTA.
        /// </summary>
        public static bool SetOfficeClipboard(string plainText, string officeHtml)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                bool result = false;
                var thread = new Thread(() => result = SetOfficeClipboard(plainText, officeHtml));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                return result;
            }

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    var dataObject = new DataObject();
                    
                    // 1. Đưa định dạng HTML chuẩn Office vào Clipboard
                    dataObject.SetText(officeHtml, TextDataFormat.Html);
                    
                    // 2. Đưa định dạng Unicode Text
                    dataObject.SetText(plainText, TextDataFormat.UnicodeText);
                    
                    // 3. Đánh dấu Marker để tránh đệ quy vô hạn
                    dataObject.SetData(MarkerFormat, true);
                    
                    // Thực hiện ghi đè Clipboard
                    Clipboard.SetDataObject(dataObject, true);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(10); // Chờ 10ms nếu bị khóa bởi tiến trình khác
                }
                catch (Exception ex)
                {
                    Program.Log($"Lỗi ghi Clipboard: {ex.Message}");
                    break;
                }
            }
            return false;
        }
    }
}
