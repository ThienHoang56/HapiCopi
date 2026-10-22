using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HapiCopi
{
    /// <summary>
    /// Một khối SVG đã bóc tách được khỏi Clipboard, kèm nguồn gốc để ghi log và hiển thị thông báo.
    /// </summary>
    public sealed class SvgPayload
    {
        public string Markup { get; init; } = "";
        public string SourceLabel { get; init; } = "";
        public string SuggestedName { get; init; } = "hinh-svg";
    }

    /// <summary>
    /// Nhận diện và bóc tách SVG từ Clipboard, bất kể người dùng copy từ đâu:
    /// file .svg/.svgz trong Explorer, markup từ Figma/Illustrator/Inkscape, SVG nội tuyến trong HTML,
    /// hay tệp ảo từ Outlook/trình duyệt (không có đường dẫn trên đĩa).
    /// </summary>
    public static class SvgClipboardSource
    {
        private const long MaxSvgBytes = 8L * 1024 * 1024;

        /// <summary>
        /// Các định dạng clipboard mà công cụ vector đăng ký khi copy. Thứ tự ưu tiên từ chuẩn nhất xuống.
        /// </summary>
        private static readonly string[] SvgMimeFormats =
        {
            "image/svg+xml",
            "image/svg+xml;charset=utf-8",
            "image/svg-xml",
            "svg+xml",
            "image/x-inkscape-svg",
            "Inkscape SVG",
            "Scalable Vector Graphics",
            "SVG Image",
            "Adobe SVG",
            "text/svg+xml"
        };

        /// <summary>
        /// Trình soạn thảo mã: ở đây SVG là *mã nguồn* người dùng muốn dán dạng chữ, không phải ảnh.
        /// </summary>
        private static readonly string[] CodeEditorProcesses =
        {
            "code", "code - insiders", "cursor", "devenv", "rider64", "idea64", "pycharm64",
            "webstorm64", "sublime_text", "notepad++", "windsurf", "zed"
        };

        private static readonly Regex InlineSvgRegex = new Regex(
            @"<svg\b[^>]*>[\s\S]*?</svg\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Thử bóc SVG khỏi clipboard. Trả về false nếu clipboard không chứa SVG hoặc nội dung nên
        /// được đối xử như văn bản (copy từ IDE).
        /// </summary>
        public static bool TryExtract(IDataObject? dataObject, string activeProcessName, out SvgPayload? payload)
        {
            payload = null;
            if (dataObject == null)
            {
                return false;
            }

            try
            {
                payload = FromFileDrop(dataObject)
                          ?? FromMimeFormat(dataObject)
                          ?? FromVirtualFile(dataObject);

                if (payload != null)
                {
                    return true;
                }

                payload = FromHtmlFragment(dataObject) ?? FromPlainText(dataObject);
                if (payload == null)
                {
                    return false;
                }

                // Markup thuần là nguồn dễ nhầm nhất: copy mã SVG trong IDE phải giữ nguyên dạng chữ.
                if (IsCodeEditorCopy(dataObject, activeProcessName))
                {
                    Program.Log($"[SVG] Bỏ qua markup SVG: copy từ trình soạn thảo mã ({activeProcessName}).");
                    payload = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"[SVG] Lỗi khi dò SVG trên clipboard: {ex.Message}");
                payload = null;
                return false;
            }
        }

        // ============================
        // NGUỒN 1: FILE .SVG / .SVGZ
        // ============================

        private static SvgPayload? FromFileDrop(IDataObject dataObject)
        {
            if (!dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                return null;
            }

            if (dataObject.GetData(DataFormats.FileDrop) is not string[] paths)
            {
                return null;
            }

            string? svgPath = paths.FirstOrDefault(IsSvgPath);
            if (svgPath == null)
            {
                return null;
            }

            string? markup = ReadSvgFile(svgPath);
            if (markup == null)
            {
                return null;
            }

            return new SvgPayload
            {
                Markup = markup,
                SourceLabel = $"file {Path.GetFileName(svgPath)}",
                SuggestedName = Path.GetFileNameWithoutExtension(svgPath)
            };
        }

        public static bool IsSvgPath(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".svgz", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Đọc file .svg hoặc .svgz (gzip). Trả về null nếu file quá lớn, không đọc được hoặc không phải SVG.
        /// </summary>
        public static string? ReadSvgFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0 || info.Length > MaxSvgBytes)
                {
                    if (info.Exists && info.Length > MaxSvgBytes)
                    {
                        Program.Log($"[SVG] Bỏ qua {info.Name}: {info.Length} bytes vượt giới hạn {MaxSvgBytes}.");
                    }
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(path);
                string markup = DecodeSvgBytes(bytes);
                return LooksLikeSvg(markup) ? markup : null;
            }
            catch (Exception ex)
            {
                Program.Log($"[SVG] Không đọc được file {path}: {ex.Message}");
                return null;
            }
        }

        // ============================
        // NGUỒN 2: ĐỊNH DẠNG MIME SVG
        // ============================

        private static SvgPayload? FromMimeFormat(IDataObject dataObject)
        {
            foreach (string format in SvgMimeFormats)
            {
                if (!dataObject.GetDataPresent(format))
                {
                    continue;
                }

                string markup = ToMarkup(dataObject.GetData(format));
                if (LooksLikeSvg(markup))
                {
                    return new SvgPayload
                    {
                        Markup = markup,
                        SourceLabel = $"định dạng {format}",
                        SuggestedName = "hinh-svg"
                    };
                }
            }

            return null;
        }

        // ============================
        // NGUỒN 3: TỆP ẢO (Outlook, trình duyệt, web app)
        // ============================

        private static SvgPayload? FromVirtualFile(IDataObject dataObject)
        {
            var names = VirtualFileReader.GetVirtualFileNames(dataObject);
            if (names.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (!IsSvgPath(names[i]))
                {
                    continue;
                }

                byte[]? content = VirtualFileReader.ReadContent(dataObject, i, MaxSvgBytes);
                if (content == null || content.Length == 0)
                {
                    continue;
                }

                string markup = DecodeSvgBytes(content);
                if (LooksLikeSvg(markup))
                {
                    return new SvgPayload
                    {
                        Markup = markup,
                        SourceLabel = $"tệp đính kèm {names[i]}",
                        SuggestedName = Path.GetFileNameWithoutExtension(names[i])
                    };
                }
            }

            return null;
        }

        // ============================
        // NGUỒN 4: SVG NỘI TUYẾN TRONG HTML
        // ============================

        private static SvgPayload? FromHtmlFragment(IDataObject dataObject)
        {
            if (!dataObject.GetDataPresent(DataFormats.Html))
            {
                return null;
            }

            string html = ToMarkup(dataObject.GetData(DataFormats.Html));
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            Match match = InlineSvgRegex.Match(html);
            if (!match.Success)
            {
                return null;
            }

            // Một đoạn văn copy từ web thường kèm vài icon SVG nội tuyến. Chỉ nhận khi SVG *là* toàn bộ
            // nội dung được copy, nếu không sẽ biến cả đoạn văn thành ảnh.
            if (HasTextOutsideSvg(html))
            {
                return null;
            }

            string markup = match.Value;
            return LooksLikeSvg(markup)
                ? new SvgPayload { Markup = markup, SourceLabel = "SVG nội tuyến trong HTML", SuggestedName = "hinh-svg" }
                : null;
        }

        private static bool HasTextOutsideSvg(string html)
        {
            int fragmentStart = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            int fragmentEnd = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            string fragment = fragmentStart >= 0 && fragmentEnd > fragmentStart
                ? html.Substring(fragmentStart, fragmentEnd - fragmentStart)
                : html;

            string withoutSvg = InlineSvgRegex.Replace(fragment, " ");
            string plain = Regex.Replace(withoutSvg, "<[^>]*>", " ");
            plain = System.Net.WebUtility.HtmlDecode(plain);
            return plain.Any(c => !char.IsWhiteSpace(c) && c != '\uFEFF');
        }

        // ============================
        // NGUỒN 5: MARKUP THUẦN
        // ============================

        private static SvgPayload? FromPlainText(IDataObject dataObject)
        {
            if (!dataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                return null;
            }

            string text = ToMarkup(dataObject.GetData(DataFormats.UnicodeText));
            return LooksLikeSvg(text)
                ? new SvgPayload { Markup = text.Trim(), SourceLabel = "markup SVG", SuggestedName = "hinh-svg" }
                : null;
        }

        private static bool IsCodeEditorCopy(IDataObject dataObject, string activeProcessName)
        {
            if (dataObject.GetDataPresent("vscode-editor-data"))
            {
                return true;
            }

            string process = (activeProcessName ?? "").ToLowerInvariant();
            return CodeEditorProcesses.Any(p => process == p);
        }

        // ============================
        // TIỆN ÍCH
        // ============================

        /// <summary>
        /// Nhận SVG khi nó là *toàn bộ* nội dung copy, không phải khi văn bản chỉ nhắc tới thẻ svg.
        /// </summary>
        public static bool LooksLikeSvg(string? markup)
        {
            if (string.IsNullOrWhiteSpace(markup))
            {
                return false;
            }

            string trimmed = markup.Trim().TrimStart('\uFEFF').TrimStart();

            bool validStart = trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                              || trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                              || trimmed.StartsWith("<!DOCTYPE svg", StringComparison.OrdinalIgnoreCase)
                              || trimmed.StartsWith("<!--", StringComparison.Ordinal);

            if (!validStart)
            {
                return false;
            }

            return trimmed.EndsWith(">", StringComparison.Ordinal)
                   && trimmed.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0
                   && trimmed.IndexOf("</svg", StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static string ToMarkup(object? data)
        {
            return data switch
            {
                string s => s,
                MemoryStream ms => DecodeSvgBytes(ms.ToArray()),
                byte[] bytes => DecodeSvgBytes(bytes),
                Stream stream => DecodeStream(stream),
                _ => ""
            };
        }

        private static string DecodeStream(Stream stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return DecodeSvgBytes(buffer.ToArray());
        }

        /// <summary>
        /// Giải mã byte sang markup: bung gzip cho .svgz, đọc BOM, mặc định UTF-8 và cắt phần đuôi NUL
        /// mà một số nguồn chèn thêm.
        /// </summary>
        private static string DecodeSvgBytes(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return "";
            }

            if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                try
                {
                    using var input = new MemoryStream(bytes);
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    gzip.CopyTo(output, 81920);
                    bytes = output.ToArray();
                }
                catch (Exception ex)
                {
                    Program.Log($"[SVG] Không bung được svgz: {ex.Message}");
                    return "";
                }
            }

            string text;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            else
            {
                text = Encoding.UTF8.GetString(bytes);
            }

            return text.TrimEnd('\0');
        }
    }
}
