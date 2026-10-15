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
        private readonly Action<string, string, Image?> _setClipboard;
        private bool _ready;
        private bool _attaching;

        public AbcEditorForm(Action<string, string, Image?> setClipboard)
        {
            _setClipboard = setClipboard;

            Text = "HapiCopi Studio - Soạn bản nhạc & Ký âm (Chuẩn MuseScore)";
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            Width = 1200;
            Height = 760;
            MinimumSize = new Size(920, 580);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

            try { Icon = UiTheme.AppIcon; } catch { }

            _web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_web);

            FormClosing += OnFormClosing;
            VisibleChanged += (_, _) =>
            {
                if (Visible)
                {
                    if (_web.CoreWebView2 != null)
                    {
                        try { _web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
                    }
                    RequestReady();
                    BeginInvoke(new Action(() => _web.Focus()));
                }
                else
                {
                    if (_web.CoreWebView2 != null)
                    {
                        try { _web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
                    }
                    MemoryOptimizer.OptimizeMemory(aggressive: true);
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
                if (!Visible && _web.CoreWebView2 != null)
                {
                    try { _web.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
                }
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

                        Image? img = null;
                        try
                        {
                            int commaIdx = png.IndexOf(',');
                            if (commaIdx != -1)
                            {
                                byte[] bytes = Convert.FromBase64String(png.Substring(commaIdx + 1));
                                using var ms = new System.IO.MemoryStream(bytes);
                                using var temp = new Bitmap(ms);
                                img = new Bitmap(temp); // Nhân bản Bitmap độc lập khỏi MemoryStream
                            }
                        }
                        catch (Exception exImg)
                        {
                            Program.Log($"[AbcEditor] Không tạo được Bitmap từ PNG: {exImg.Message}");
                        }

                        try
                        {
                            _setClipboard(source, cfHtml, img);
                            Program.Log("[AbcEditor] Đã copy ảnh bản nhạc (HTML + Bitmap) vào clipboard.");
                        }
                        finally
                        {
                            img?.Dispose();
                        }
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
