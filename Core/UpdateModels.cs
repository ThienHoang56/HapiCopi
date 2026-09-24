using System;
using System.Collections.Generic;

namespace HapiCopi
{
    /// <summary>
    /// Kênh của một bản phát hành. Phân loại theo vị trí: bản mới nhất luôn là thử nghiệm,
    /// bản liền trước là bản đã chạy được nên được khuyến nghị.
    /// </summary>
    public enum ReleaseChannel
    {
        Experimental,
        Recommended,
        Older
    }

    public sealed class ReleaseInfo
    {
        public string Version { get; init; } = "";
        public string Released { get; init; } = "";
        public string Notes { get; init; } = "";

        /// <summary>Tên file bộ cài, nằm cùng thư mục/URL với versions.json.</summary>
        public string File { get; init; } = "";

        public long Size { get; init; }
        public string Sha256 { get; init; } = "";

        public ReleaseChannel Channel { get; set; } = ReleaseChannel.Older;
        public bool IsCurrent { get; set; }

        /// <summary>Đã tải về máy nên quay lui được kể cả khi mất mạng.</summary>
        public bool IsCached { get; set; }

        public Version ParsedVersion => UpdateManager.ParseVersion(Version);

        public string ChannelLabel => Channel switch
        {
            ReleaseChannel.Recommended => "Khuyến nghị",
            ReleaseChannel.Experimental => "Thử nghiệm",
            _ => "Bản cũ"
        };
    }

    public sealed class UpdateManifest
    {
        public string App { get; init; } = "HapiCopi";
        public List<ReleaseInfo> Releases { get; init; } = new List<ReleaseInfo>();
    }

    public sealed class UpdateCheckResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = "";
        public List<ReleaseInfo> Releases { get; init; } = new List<ReleaseInfo>();

        public ReleaseInfo? Recommended { get; init; }
        public ReleaseInfo? Experimental { get; init; }
        public Version CurrentVersion { get; init; } = new Version(0, 0, 0);

        /// <summary>Có bản mới hơn bản đang chạy hay không.</summary>
        public bool HasNewer { get; init; }
    }
}
