using System.Diagnostics;
using System.IO;
using System.Management;

namespace ExtraScreen.Encoding;

public class EncoderDetector
{
    private readonly string _ffmpegPath;

    public EncoderDetector(string? customFfmpegPath = null)
    {
        _ffmpegPath = LocateFFmpeg(customFfmpegPath);
    }

    public string FFmpegPath => _ffmpegPath;
    public bool IsFFmpegAvailable => File.Exists(_ffmpegPath);

    public static string LocateFFmpeg(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return customPath;

        // 1. App base dir tools
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string bundled = Path.Combine(baseDir, "tools", "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;

        // Relative path from development
        string projectRelative = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "tools", "ffmpeg.exe"));
        if (File.Exists(projectRelative)) return projectRelative;

        // 2. WinGet package folder
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPackages))
        {
            var match = Directory.GetFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (match != null) return match;
        }

        // 3. Fallback to "ffmpeg"
        return "ffmpeg";
    }

    public List<HardwareEncoderOption> DetectAvailableEncoders()
    {
        var options = new List<HardwareEncoderOption>();
        string gpuName = GetGpuName().ToLowerInvariant();

        bool hasNvidia = gpuName.Contains("nvidia") || gpuName.Contains("geforce") || gpuName.Contains("rtx") || gpuName.Contains("gtx");
        bool hasIntel = gpuName.Contains("intel") || gpuName.Contains("arc") || gpuName.Contains("uhd") || gpuName.Contains("iris");
        bool hasAmd = gpuName.Contains("amd") || gpuName.Contains("radeon");

        if (hasNvidia)
        {
            options.Add(new HardwareEncoderOption
            {
                Type = HardwareEncoderType.Nvenc,
                Name = "NVIDIA NVENC (Phần cứng GPU - Khuyên dùng)",
                H264Codec = "h264_nvenc",
                HevcCodec = "hevc_nvenc",
                IsHevcSupported = true
            });
        }

        if (hasIntel)
        {
            options.Add(new HardwareEncoderOption
            {
                Type = HardwareEncoderType.QuickSync,
                Name = "Intel QuickSync (Phần cứng iGPU)",
                H264Codec = "h264_qsv",
                HevcCodec = "hevc_qsv",
                IsHevcSupported = true
            });
        }

        if (hasAmd)
        {
            options.Add(new HardwareEncoderOption
            {
                Type = HardwareEncoderType.Amf,
                Name = "AMD AMF (Phần cứng GPU AMD)",
                H264Codec = "h264_amf",
                HevcCodec = "hevc_amf",
                IsHevcSupported = true
            });
        }

        // Always provide CPU fallback
        options.Add(new HardwareEncoderOption
        {
            Type = HardwareEncoderType.Cpu,
            Name = "CPU x264 (Phần mềm - Dự phòng)",
            H264Codec = "libx264",
            HevcCodec = "libx265",
            IsHevcSupported = false // H.265 CPU not recommended for real-time 60fps
        });

        return options;
    }

    public static string GetGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                string name = item["Name"]?.ToString() ?? "";
                if (!name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }
        catch
        {
            // Ignore WMI errors
        }
        return "Unknown GPU";
    }
}
