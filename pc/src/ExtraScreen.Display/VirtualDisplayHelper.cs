using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ExtraScreen.Display;

public record VirtualDisplayStatus(bool IsInstalled, string DriverName, string Details);

[SupportedOSPlatform("windows")]
public static class VirtualDisplayHelper
{
    private static readonly string[] KnownVirtualKeywords =
    [
        "Virtual",
        "Parsec",
        "IddCx",
        "Amyuni",
        "spacedesk",
        "DATRONICSOFT",
        "USB Display"
    ];

    /// <summary>
    /// Kiểm tra trạng thái của Virtual Display Driver trên hệ thống Windows.
    /// Kết hợp truy vấn nhanh qua Windows CCD API (CcdDisplayConfig) và WMI fallback.
    /// </summary>
    public static VirtualDisplayStatus CheckStatus()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new VirtualDisplayStatus(false, "Không hỗ trợ", "Chỉ hỗ trợ hệ điều hành Windows.");
        }

        try
        {
            // 1. Kiểm tra nhanh qua Windows CCD API (QueryDisplayConfig) học từ spacedeskConsole.exe
            var ccdTargets = CcdDisplayConfig.QueryActiveTargets();
            foreach (var target in ccdTargets)
            {
                foreach (var keyword in KnownVirtualKeywords)
                {
                    if (target.FriendlyName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        target.DevicePath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        string displayName = string.IsNullOrWhiteSpace(target.FriendlyName) ? keyword : target.FriendlyName;
                        return new VirtualDisplayStatus(
                            IsInstalled: true,
                            DriverName: displayName,
                            Details: $"Đã phát hiện màn hình ảo hoạt động (CCD): {displayName}"
                        );
                    }
                }
            }

            // 2. Fallback qua WMI Win32_VideoController (nếu màn hình ảo đã cài nhưng đang ở trạng thái ngắt kết nối)
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, Status FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? string.Empty;
                string version = obj["DriverVersion"]?.ToString() ?? string.Empty;

                foreach (var keyword in KnownVirtualKeywords)
                {
                    if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return new VirtualDisplayStatus(
                            IsInstalled: true,
                            DriverName: name,
                            Details: $"Đã phát hiện driver ảo: {name} (Version {version})"
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return new VirtualDisplayStatus(false, "Lỗi kiểm tra", ex.Message);
        }

        return new VirtualDisplayStatus(
            IsInstalled: false,
            DriverName: "Chưa cài đặt",
            Details: "Chưa phát hiện Virtual Display Driver. Chế độ Phản chiếu (Mirror) sẵn sàng hoạt động; hoặc cài đặt Parsec VDD / Spacedesk IddCx để mở rộng độc lập."
        );
    }
}
