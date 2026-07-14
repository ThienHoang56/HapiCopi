using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ soạn công thức toán realtime: gõ AsciiMath/LaTeX -> xem trước ngay -> Copy ảnh PNG dán vào Word/Docs.
    /// </summary>
    public class MathEditorForm : Form
    {
        private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web;
        private readonly Action<string, string> _setClipboard;

        /// <param name="setClipboard">Callback đặt clipboard (plainText, cfHtml) — dùng lại cơ chế STA + chống tự xử lý lại của app.</param>
        public MathEditorForm(Action<string, string> setClipboard)
        {
            _setClipboard = setClipboard;

            Text = "HapiCopi - Soạn công thức toán";
            Width = 940;
            Height = 580;
            StartPosition = FormStartPosition.CenterScreen;

            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_web);

            InitAsync();
        }

        private async void InitAsync()
        {
            try
            {
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HapiCopiWebViewCache");

                var env = await CoreWebView2Environment.CreateAsync(null, cacheDir);
                await _web.EnsureCoreWebView2Async(env);

                string localFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebViewResources");
                if (!Directory.Exists(localFolder))
                {
                    localFolder = Path.Combine(
                        Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.FullName ?? "",
                        "WebViewResources");
                }

                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hapicopi.local", localFolder, CoreWebView2HostResourceAccessKind.Allow);

                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;

                _web.CoreWebView2.WebMessageReceived += OnWebMessage;
                _web.Source = new Uri("http://hapicopi.local/matheditor.html");
            }
            catch (Exception ex)
            {
                Program.Log($"[MathEditor LỖI] Khởi tạo WebView2 thất bại: {ex.Message}");
            }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(json)) return;

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                string type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";

                if (type == "copy")
                {
                    string png = root.TryGetProperty("png", out var p) ? (p.GetString() ?? "") : "";
                    string source = root.TryGetProperty("source", out var s) ? (s.GetString() ?? "") : "";
                    if (!string.IsNullOrEmpty(png) && png.StartsWith("data:image"))
                    {
                        string fragment = $"<img src=\"{png}\" style=\"vertical-align:middle;\" />";
                        string cfHtml = MarkdownToWordConverter.PackageAsClipboardHtml(fragment);
                        _setClipboard(source, cfHtml);
                        Program.Log("[MathEditor] Đã copy ảnh công thức vào clipboard.");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[MathEditor LỖI] Xử lý message thất bại: {ex.Message}");
            }
        }
    }
}
