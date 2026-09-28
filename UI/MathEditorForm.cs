using System;
using System.Drawing;
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
        private bool _ready;
        private bool _attaching;

        public MathEditorForm(Action<string, string> setClipboard)
        {
            _setClipboard = setClipboard;

            Text = "HapiCopi - Soạn công thức toán";
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            Width = 940;
            Height = 580;
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
                    SyncWordFontSize();
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
                await WebViewManager.AttachEditorAsync(_web, "matheditor.html", OnWebMessage);
                _ready = true;
                SyncWordFontSize();
            }
            catch (Exception ex)
            {
                _attaching = false;
                Program.Log($"[MathEditor LỖI] Khởi tạo WebView2 thất bại: {ex.Message}");
                if (Visible && Opacity > 0)
                {
                    MessageBox.Show("Không mở được soạn công thức:\n" + ex.Message, "HapiCopi",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void SyncWordFontSize()
        {
            try
            {
                var format = WordInterop.GetCurrentFormatWithTimeout(60);
                if (format.IsFromActiveWord && format.FontSize > 0)
                {
                    int rounded = (int)Math.Round(format.FontSize);
                    if (_ready && _web.CoreWebView2 != null)
                    {
                        _web.CoreWebView2.ExecuteScriptAsync($"if (typeof setFontSize === 'function') setFontSize({rounded});");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[MathEditor] Lỗi đồng bộ cỡ chữ Word: {ex.Message}");
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
                    string widthPt = root.TryGetProperty("widthPt", out var wp) ? (wp.GetString() ?? "") : "";
                    string heightPt = root.TryGetProperty("heightPt", out var hp) ? (hp.GetString() ?? "") : "";
                    string vAlignPt = root.TryGetProperty("verticalAlignPt", out var vp) ? (vp.GetString() ?? "") : "";
                    bool isDisplay = root.TryGetProperty("isDisplay", out var dp) && dp.GetBoolean();

                    if (!string.IsNullOrEmpty(png) && png.StartsWith("data:image"))
                    {
                        string style = "";
                        if (!string.IsNullOrEmpty(heightPt)) style += $"height:{heightPt}pt; ";
                        if (!string.IsNullOrEmpty(widthPt)) style += $"width:{widthPt}pt; ";
                        if (isDisplay)
                        {
                            style += "display:block; margin:6pt auto; ";
                        }
                        else if (!string.IsNullOrEmpty(vAlignPt))
                        {
                            style += $"vertical-align:{vAlignPt}pt; ";
                        }
                        else
                        {
                            style += "vertical-align:middle; ";
                        }

                        string safeSource = System.Net.WebUtility.HtmlEncode(source);
                        string fragment = $"<img src=\"{png}\" alt=\"[HapiCopi:Math] {safeSource}\" title=\"[HapiCopi:Math] {safeSource}\" style=\"{style.Trim()}\" />";
                        string cfHtml = MarkdownToWordConverter.PackageAsClipboardHtml(fragment);
                        _setClipboard(source, cfHtml);
                        Program.Log($"[MathEditor] Đã copy ảnh công thức vào clipboard (Size={heightPt}pt, Display={isDisplay}).");
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
