using ExtraScreen.Protocol;

namespace ExtraScreen.Tests;

public class ProtocolTests
{
    [Fact]
    public void TestHelloPacketSerialization()
    {
        var hello = new HelloMessage
        {
            Model = "Samsung Galaxy S23 Ultra",
            Width = 2340,
            Height = 1080,
            Dpi = 480,
            Codecs = new[] { "h264", "h265" },
            Version = "1.0.0"
        };

        var packet = Packet.CreateJson(ProtocolConstants.TypeHello, hello);
        Assert.Equal(ProtocolConstants.TypeHello, packet.Type);

        var deserialized = packet.DeserializeJson<HelloMessage>();
        Assert.NotNull(deserialized);
        Assert.Equal("Samsung Galaxy S23 Ultra", deserialized.Model);
        Assert.Equal(2340, deserialized.Width);
        Assert.Equal(1080, deserialized.Height);
        Assert.Equal(480, deserialized.Dpi);
        Assert.Equal(2, deserialized.Codecs.Length);
        Assert.Contains("h265", deserialized.Codecs);
    }

    [Fact]
    public void TestSessionConfigPacket()
    {
        var config = new SessionConfigMessage
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            Codec = "h264",
            Bitrate = 20_000_000
        };

        var packet = Packet.CreateJson(ProtocolConstants.TypeSessionConfig, config);
        Assert.Equal(ProtocolConstants.TypeSessionConfig, packet.Type);

        var deserialized = packet.DeserializeJson<SessionConfigMessage>();
        Assert.NotNull(deserialized);
        Assert.Equal(1920, deserialized.Width);
        Assert.Equal(1080, deserialized.Height);
        Assert.Equal(60, deserialized.Fps);
        Assert.Equal("h264", deserialized.Codec);
        Assert.Equal(20_000_000, deserialized.Bitrate);
    }

    [Fact]
    public void TestVideoFramePacket()
    {
        byte[] fakeNal = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84 };
        var packet = Packet.CreateVideoFrame(fakeNal, isKeyframe: true, timestampUs: 123456789L);

        Assert.Equal(ProtocolConstants.TypeVideoFrame, packet.Type);
        Assert.Equal(123456789L, packet.TimestampUs);
        Assert.Equal(1 + fakeNal.Length, packet.Payload.Length);
        Assert.Equal(1, packet.Payload[0]); // Keyframe flag
        Assert.Equal(0x65, packet.Payload[5]); // NAL unit type 5
    }

    [Fact]
    public async Task TestStreamSerializationRoundtrip()
    {
        var ping = Packet.CreatePing(9876543210L);

        using var ms = new MemoryStream();
        await PacketSerializer.WritePacketAsync(ms, ping);

        ms.Position = 0;
        var readPacket = await PacketSerializer.ReadPacketAsync(ms);

        Assert.NotNull(readPacket);
        Assert.Equal(ProtocolConstants.TypePing, readPacket.Type);
        Assert.Equal(9876543210L, readPacket.TimestampUs);
        Assert.Equal(8, readPacket.Payload.Length);
    }
}
