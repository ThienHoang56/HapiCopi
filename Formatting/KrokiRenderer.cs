using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HapiCopi
{
    public static class KrokiRenderer
    {
        // Chỉ giữ các loại đã xác nhận render được qua kroki.io.
        // ĐÃ LOẠI: tikz (cần full LaTeX document), họ blockdiag/seqdiag/actdiag/nwdiag/packetdiag/rackdiag
        // (backend blockdiag của kroki.io public trả về rỗng).
        private static readonly HashSet<string> SupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "d2", "plantuml", "dot", "graphviz", "vega", "vegalite", "structurizr", "nomnoml", "c4plantuml", "wireviz", "wavedrom", "erd", "dbml", "bytefield", "pikchr", "svgbob"
        };

        public static bool IsSupportedDiagram(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return SupportedTypes.Contains(type.Trim());
        }

        public static string DetectRawDiagramLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (text.Contains("\\begin{tikzpicture}", StringComparison.OrdinalIgnoreCase)
                || text.Contains("\\begin{axis}", StringComparison.OrdinalIgnoreCase)) return "tikz";
            if (text.Contains("@startuml", StringComparison.OrdinalIgnoreCase)) return "plantuml";
            if (text.Contains("digraph ", StringComparison.OrdinalIgnoreCase)) return "graphviz";
            if (text.Contains("{") && text.Contains("\"$schema\"") && text.Contains("vega.github.io")) return "vegalite";
            if (text.Contains("{") && text.Contains("\"signal\"") && text.Contains("\"wave\"")) return "wavedrom";
            
            // D2 heuristics (very basic)
            if (text.Contains("direction: right") || text.Contains("direction: down") || text.Contains("shape: cylinder"))
            {
                if (text.Contains("{") && text.Contains("}")) return "d2";
            }
            if (text.Contains("Table ") && text.Contains("{") && text.Contains("}")) return "dbml";
            
            return null;
        }

        public static bool ContainsAnyDiagram(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (MermaidRenderer.IsRawMermaidCode(text)) return true;
            if (DetectRawDiagramLanguage(text) != null) return true;

            
            var diagramRegex = new System.Text.RegularExpressions.Regex(@"```([a-zA-Z0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var matches = diagramRegex.Matches(text);
            foreach(System.Text.RegularExpressions.Match match in matches)
            {
                string lang = match.Groups[1].Value.ToLowerInvariant();
                if (lang == "mermaid" || IsSupportedDiagram(lang) || TikzRenderer.IsTikz(lang)) return true;
            }
            return false;
        }

        public static bool ContainsAnyDiagramHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;
            var htmlRegex = new System.Text.RegularExpressions.Regex(@"<code[^>]*class=""[^""]*language-([a-zA-Z0-9]+)[^""]*""[^>]*>|<div[^>]*class=""[^""]*mermaid[^""]*""[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var matches = htmlRegex.Matches(html);
            foreach(System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups[1].Success)
                {
                    string lang = match.Groups[1].Value.ToLowerInvariant();
                    if (lang == "mermaid" || IsSupportedDiagram(lang) || TikzRenderer.IsTikz(lang)) return true;
                }
                else
                {
                    // It's a div.mermaid
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Lấy mã SVG thô từ Kroki để tiện rasterize sang PNG (Word không nhúng được SVG trong HTML clipboard).
        /// </summary>
        public static async Task<string> GetKrokiSvgAsync(string code, string diagramType)
        {
            byte[] bytes = await GetKrokiBytesAsync(code, diagramType, "svg");
            if (bytes != null && bytes.Length > 0)
            {
                return Encoding.UTF8.GetString(bytes);
            }
            return "";
        }

        /// <summary>
        /// Trả về data URI của sơ đồ. Mặc định "png" để Word/Google Docs nhúng được ảnh.
        /// Với "svg" chỉ nên dùng khi hiển thị trên trình duyệt.
        /// </summary>
        public static async Task<string> GetKrokiDataUriAsync(string code, string diagramType, string format = "png")
        {
            byte[] imageBytes = await GetKrokiBytesAsync(code, diagramType, format);
            if (imageBytes != null && imageBytes.Length > 0)
            {
                string imgBase64 = Convert.ToBase64String(imageBytes);
                string mimeType = format.Equals("svg", StringComparison.OrdinalIgnoreCase) ? "svg+xml" : format.ToLowerInvariant();
                return $"data:image/{mimeType};base64,{imgBase64}";
            }
            return "";
        }

        private static async Task<byte[]> GetKrokiBytesAsync(string code, string diagramType, string format)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code) || !IsSupportedDiagram(diagramType))
                    return null;

                string type = diagramType.Trim().ToLowerInvariant();
                if (type == "dot") type = "graphviz"; // Alias

                // Replace Non-Breaking Spaces with normal spaces (often found when copying from Word/Docs)
                code = code.Replace("\u00A0", " ");

                string url = "https://kroki.io/";

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);

                    var payload = new
                    {
                        diagram_source = code,
                        diagram_type = type,
                        output_format = format
                    };
                    string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            return imageBytes;
                        }
                    }
                    else
                    {
                        string errorText = await response.Content.ReadAsStringAsync();
                        Program.Log($"[KrokiRenderer LỖI] Lỗi HTTP {response.StatusCode} từ Kroki ({type}/{format}): {errorText}");
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[KrokiRenderer LỖI] Không thể kết xuất qua Kroki: {ex.Message}");
            }
            return null;
        }
    }
}
