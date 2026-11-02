namespace ExtraScreen.Encoding;

public enum HardwareEncoderType
{
    Nvenc,
    QuickSync,
    Amf,
    Cpu
}

public class HardwareEncoderOption
{
    public HardwareEncoderType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string H264Codec { get; set; } = "h264_nvenc";
    public string HevcCodec { get; set; } = "hevc_nvenc";
    public bool IsHevcSupported { get; set; }

    public override string ToString() => Name;
}

public class VideoEncoderConfig
{
    public string Codec { get; set; } = "h264"; // "h264" or "h265"
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 60;
    public int BitrateBps { get; set; } = 20_000_000; // 20 Mbps default (12 Mbps for H.265)
    public int OutputIndex { get; set; } = 0;
    public HardwareEncoderOption EncoderOption { get; set; } = new HardwareEncoderOption
    {
        Type = HardwareEncoderType.Nvenc,
        Name = "NVIDIA NVENC (Tự động phát hiện)",
        H264Codec = "h264_nvenc",
        HevcCodec = "hevc_nvenc",
        IsHevcSupported = true
    };
}
