using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HapiCopi
{
    public class SponsorItem
    {
        public string Text { get; set; } = "Bàn vẽ điện tử Wacom One giảm 15% cho học sinh/sinh viên [Xem ưu đãi ↗]";
        public string Url { get; set; } = "https://shopee.vn";
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Quản lý dòng tài trợ tinh tế (Native Sponsor Tip):
    /// - Hiển thị 1 hàng text nhỏ xíu (24px) dưới thanh công thức.
    /// - Tải động từ file JSON trên GitHub/CDN (chỉ mất 0.05s).
    /// - Nếu là bản Pro: Ẩn hoàn toàn 100%.
    /// - Nếu mất mạng: Dùng fallback mặc định hoặc ẩn sạch sẽ.
    /// </summary>
    public static class SponsorService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        private static SponsorItem _currentSponsor = new SponsorItem();
        private static bool _hasFetched = false;

        private const string RemoteSponsorUrl =
            "https://raw.githubusercontent.com/ThienHoang56/HapiCopi/main/config/sponsor.json";

        static SponsorService()
        {
            // Tải ngầm trong nền lúc khởi động
            _ = FetchRemoteSponsorAsync();
        }

        public static async Task FetchRemoteSponsorAsync()
        {
            if (_hasFetched) return;

            try
            {
                string json = await _httpClient.GetStringAsync(RemoteSponsorUrl);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var item = JsonSerializer.Deserialize<SponsorItem>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (item != null && !string.IsNullOrWhiteSpace(item.Text))
                    {
                        _currentSponsor = item;
                        _hasFetched = true;
                        Program.Log($"[SponsorService] Đã nạp thành công sponsor: {_currentSponsor.Text}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback im lặng, không làm phiền người dùng
                Program.Log($"[SponsorService] Không thể nạp sponsor từ CDN ({ex.Message}), dùng fallback.");
            }
        }

        /// <summary>
        /// Lấy thông tin tài trợ hiện tại. Nếu là bản Pro, luôn trả về null (ẩn hoàn toàn).
        /// </summary>
        public static SponsorItem? GetActiveSponsor()
        {
            if (LicenseService.IsPro)
            {
                return null;
            }

            if (!_currentSponsor.Enabled)
            {
                return null;
            }

            return _currentSponsor;
        }
    }
}
