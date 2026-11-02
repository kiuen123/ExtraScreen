namespace ExtraScreen.Protocol;

public static class ProtocolConstants
{
    public const int DefaultPort = 7070;
    public const int FixedWidth = 1920;
    public const int FixedHeight = 1080;
    public const int FixedFps = 60;

    public const byte TypeHello = 0x01;
    public const byte TypeSessionConfig = 0x02;
    public const byte TypeVideoConfig = 0x10;
    public const byte TypeVideoFrame = 0x11;
    public const byte TypeRequestKeyframe = 0x12;
    public const byte TypePing = 0x30;
    public const byte TypePong = 0x31;
    public const byte TypeStats = 0x40;
    public const byte TypeBye = 0xFF;

    public const string CodecH264 = "h264";
    public const string CodecH265 = "h265";
}
