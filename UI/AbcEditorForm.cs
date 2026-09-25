using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ soạn bản nhạc bằng ABC notation: gõ/ dán ABC -> xem bản nhạc realtime,
    /// nghe thử bằng synth, và Copy bản nhạc thành ảnh PNG để dán vào Word/Docs.
    /// </summary>
    public class AbcEditorForm : Form
    {
        private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web;
        private readonly Action<string, string> _setClipboard;
        private bool _ready;
        private bool _attaching;

        public AbcEditorForm(Action<string, string> setClipboard)
        {
            _setClipboard = setClipboard;

            Text = "HapiCopi - Soạn bản nhạc (ABC notation)";
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            Width = 1040;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;

            try { Icon = UiTheme.AppIcon; } catch { }

            _web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_web);

            FormClosing += OnFormClosing;
            VisibleChanged += (_, _) =>
            {
                if (Visible)
                {
                    RequestReady();
                }
            };
        }

        public void RequestReady() => _ = EnsureReadyAsync();

        private async Task EnsureReadyAsync()
        {
            if (_ready || _attaching)
            {
                return;
            }

            _attaching = true;
            try
            {
                await WebViewManager.AttachEditorAsync(_web, "abceditor.html", OnWebMessage);
                _ready = true;
            }
            catch (Exception ex)
            {
                _attaching = false;
                Program.Log($"[AbcEditor LỖI] Khởi tạo WebView2 thất bại: {ex.Message}");
                if (Visible && Opacity > 0)
                {
                    MessageBox.Show("Không mở được soạn bản nhạc:\n" + ex.Message, "HapiCopi",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
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
                        string fragment = $"<img src=\"{png}\" style=\"display:block;\" />";
                        string cfHtml = MarkdownToWordConverter.PackageAsClipboardHtml(fragment);
                        _setClipboard(source, cfHtml);
                        Program.Log("[AbcEditor] Đã copy ảnh bản nhạc vào clipboard.");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[AbcEditor LỖI] Xử lý message thất bại: {ex.Message}");
            }
        }
    }
}
