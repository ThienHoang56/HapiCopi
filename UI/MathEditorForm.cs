using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace HapiCopi
{
    /// <summary>
    /// Cửa sổ soạn công thức toán realtime: gõ AsciiMath/LaTeX -> xem trước ngay -> Copy ảnh PNG dán vào Word/Docs,
    /// hoặc chỉnh sửa trực tiếp in-place hai chiều khi chọn ảnh công thức trong Microsoft Word hoặc Google Docs.
    /// </summary>
    public class MathEditorForm : Form
    {
        private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web;
        private readonly Action<string, string> _setClipboard;
        private bool _ready;
        private bool _attaching;
        private bool _isDocsMode;
        private IntPtr _docsSourceWindow = IntPtr.Zero;
        private (string formula, string mode, float fontSize, bool isWord, bool isDocs)? _pendingFormula;

        public MathEditorForm(Action<string, string> setClipboard)
        {
            _setClipboard = setClipboard;

            Text = "HapiCopi - Soạn công thức toán";
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            Width = 980;
            Height = 620;
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
                    BeginInvoke(new Action(() => _web.Focus()));
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

                if (_pendingFormula.HasValue)
                {
                    var pending = _pendingFormula.Value;
                    _pendingFormula = null;
                    SendFormulaToWebView(pending.formula, pending.mode, pending.fontSize, pending.isWord, pending.isDocs);
                }
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

        public void OpenForWordFormula(WordFormulaInfo info)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OpenForWordFormula(info)));
                return;
            }

            _isDocsMode = false;
            _docsSourceWindow = IntPtr.Zero;

            EditorWindow.Present(this);
            RequestReady();

            string mode = (info.Formula.Contains("\\") || info.Formula.Contains("{")) ? "latex" : "asciimath";
            SendFormulaToWebView(info.Formula, mode, info.FontSize, isWord: true, isDocs: false);
        }

        public void OpenForFormulaText(string formula, string mode = "latex", float fontSize = 11f, bool isDocs = false, IntPtr sourceWindow = default)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OpenForFormulaText(formula, mode, fontSize, isDocs, sourceWindow)));
                return;
            }

            _isDocsMode = isDocs;
            _docsSourceWindow = sourceWindow;

            EditorWindow.Present(this);
            RequestReady();

            SendFormulaToWebView(formula, mode, fontSize, isWord: false, isDocs: isDocs);
        }

        public void ResetWordMode()
        {
            _isDocsMode = false;
            _docsSourceWindow = IntPtr.Zero;
            if (_ready && _web.CoreWebView2 != null)
            {
                _web.CoreWebView2.ExecuteScriptAsync(
                    "window._isWordMode = false; window._isDocsMode = false; var b = document.getElementById('copyBtn'); if (b) b.textContent = 'Copy ảnh công thức (Ctrl+Enter)';");
            }
        }

        private void SendFormulaToWebView(string formula, string mode, float fontSize, bool isWord, bool isDocs = false)
        {
            if (!_ready || _web.CoreWebView2 == null)
            {
                _pendingFormula = (formula, mode, fontSize, isWord, isDocs);
                return;
            }

            string safeFormula = System.Text.Json.JsonSerializer.Serialize(formula);
            string safeMode = System.Text.Json.JsonSerializer.Serialize(mode);
            string isWordStr = isWord ? "true" : "false";
            string isDocsStr = isDocs ? "true" : "false";

            _web.CoreWebView2.ExecuteScriptAsync(
                $"if (typeof setFormula === 'function') setFormula({safeFormula}, {safeMode}, {fontSize}, {isWordStr}, {isDocsStr});");
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

                if (type == "close")
                {
                    Hide();
                    return;
                }

                if (type == "copy")
                {
                    string png = root.TryGetProperty("png", out var p) ? (p.GetString() ?? "") : "";
                    string source = root.TryGetProperty("source", out var s) ? (s.GetString() ?? "") : "";
                    string widthPt = root.TryGetProperty("widthPt", out var wp) ? (wp.GetString() ?? "") : "";
                    string heightPt = root.TryGetProperty("heightPt", out var hp) ? (hp.GetString() ?? "") : "";
                    string vAlignPt = root.TryGetProperty("verticalAlignPt", out var vp) ? (vp.GetString() ?? "") : "";
                    bool isDisplay = root.TryGetProperty("isDisplay", out var dp) && dp.GetBoolean();
                    bool isWordUpdate = root.TryGetProperty("isWordUpdate", out var wu) && wu.GetBoolean();
                    bool isDocsUpdate = root.TryGetProperty("isDocsUpdate", out var du) && du.GetBoolean();
                    float.TryParse(widthPt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float wPt);
                    float.TryParse(heightPt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float hPt);
                    float.TryParse(vAlignPt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float vPt);

                    if (isWordUpdate && !string.IsNullOrEmpty(png))
                    {
                        bool success = WordInterop.ReplaceSelectedFormulaInWord(png, source, wPt, hPt, vPt);
                        if (success)
                        {
                            Program.Log($"[MathEditor] Đã cập nhật công thức trực tiếp trong Word: {source}");
                            FormulaTracker.Register(png, source, widthPt: wPt, heightPt: hPt);
                            Hide();
                            WordInterop.ActivateWordWindow();
                            return;
                        }
                        else
                        {
                            Program.Log("[MathEditor] Cập nhật trực tiếp vào Word thất bại, chuyển sang fallback copy Clipboard.");
                        }
                    }

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
                        FormulaTracker.Register(png, source, widthPt: wPt, heightPt: hPt);
                        Program.Log($"[MathEditor] Đã copy ảnh công thức vào clipboard (Size={heightPt}pt, Display={isDisplay}).");

                        if ((_isDocsMode || isDocsUpdate) && _docsSourceWindow != IntPtr.Zero)
                        {
                            Hide();
                            IntPtr targetWnd = _docsSourceWindow;
                            _isDocsMode = false;
                            _docsSourceWindow = IntPtr.Zero;

                            Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(100);
                                    ActiveWindowHelper.SetForegroundWindow(targetWnd);
                                    await Task.Delay(120);
                                    ClipboardSimulator.SimulateCtrlV();
                                    Program.Log("[MathEditor] Đã tự động dán công thức cập nhật vào Google Docs!");
                                }
                                catch (Exception ex)
                                {
                                    Program.Log($"[MathEditor LỖI] Tự động dán Docs thất bại: {ex.Message}");
                                }
                            });
                            return;
                        }
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
