using System.Text.Json.Serialization;

namespace ExtraScreen.Protocol;

public class HelloMessage
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("dpi")]
    public int Dpi { get; set; }

    [JsonPropertyName("codecs")]
    public string[] Codecs { get; set; } = Array.Empty<string>();

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
}

public class SessionConfigMessage
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = ProtocolConstants.FixedWidth;

    [JsonPropertyName("height")]
    public int Height { get; set; } = ProtocolConstants.FixedHeight;

    [JsonPropertyName("fps")]
    public int Fps { get; set; } = ProtocolConstants.FixedFps;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = ProtocolConstants.CodecH264;

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; } = 20_000_000;
}

public class StatsMessage
{
    [JsonPropertyName("fps")]
    public double Fps { get; set; }

    [JsonPropertyName("decodeMs")]
    public double DecodeMs { get; set; }

    [JsonPropertyName("rttMs")]
    public double RttMs { get; set; }
}

public class ByeMessage
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
