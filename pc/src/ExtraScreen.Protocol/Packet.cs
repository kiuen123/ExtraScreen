using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ExtraScreen.Protocol;

public sealed class Packet
{
    public byte Type { get; set; }
    public long TimestampUs { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public static Packet CreateJson<T>(byte type, T data, long timestampUs = 0)
    {
        string json = JsonSerializer.Serialize(data);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        return new Packet
        {
            Type = type,
            TimestampUs = timestampUs,
            Payload = payload
        };
    }

    public static Packet CreateVideoConfig(byte[] configData, long timestampUs = 0)
    {
        return new Packet
        {
            Type = ProtocolConstants.TypeVideoConfig,
            TimestampUs = timestampUs,
            Payload = configData
        };
    }

    public static Packet CreateVideoFrame(byte[] nalUnits, bool isKeyframe, long timestampUs = 0)
    {
        byte[] payload = new byte[1 + nalUnits.Length];
        payload[0] = (byte)(isKeyframe ? 1 : 0);
        Buffer.BlockCopy(nalUnits, 0, payload, 1, nalUnits.Length);

        return new Packet
        {
            Type = ProtocolConstants.TypeVideoFrame,
            TimestampUs = timestampUs,
            Payload = payload
        };
    }

    public static Packet CreatePing(long timestampUs)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(payload, timestampUs);
        return new Packet
        {
            Type = ProtocolConstants.TypePing,
            TimestampUs = timestampUs,
            Payload = payload
        };
    }

    public static Packet CreatePong(long timestampUs)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(payload, timestampUs);
        return new Packet
        {
            Type = ProtocolConstants.TypePong,
            TimestampUs = timestampUs,
            Payload = payload
        };
    }

    public static Packet CreateRequestKeyframe()
    {
        return new Packet
        {
            Type = ProtocolConstants.TypeRequestKeyframe,
            TimestampUs = 0,
            Payload = Array.Empty<byte>()
        };
    }

    public static Packet CreateFrameAck(long timestampUs = 0)
    {
        return new Packet
        {
            Type = ProtocolConstants.TypeFrameAck,
            TimestampUs = timestampUs,
            Payload = Array.Empty<byte>()
        };
    }

    public static Packet CreateTouch(byte action, int pointerId, float normX, float normY, float pressure = 1.0f, long timestampUs = 0)
    {
        byte[] payload = new byte[17];
        payload[0] = action;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), pointerId);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(5, 4), normX);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(9, 4), normY);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(13, 4), pressure);

        return new Packet
        {
            Type = ProtocolConstants.TypeTouch,
            TimestampUs = timestampUs,
            Payload = payload
        };
    }

    public static TouchEvent ParseTouch(byte[] payload)
    {
        if (payload.Length < 17)
        {
            throw new InvalidDataException("Invalid touch packet length: expected at least 17 bytes");
        }

        byte action = payload[0];
        int pointerId = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1, 4));
        float normX = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(5, 4));
        float normY = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(9, 4));
        float pressure = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(13, 4));

        return new TouchEvent(action, pointerId, normX, normY, pressure);
    }

    public static Packet CreateAudioFrame(byte[] audioData, long timestampUs = 0)
    {
        return new Packet
        {
            Type = ProtocolConstants.TypeAudioFrame,
            TimestampUs = timestampUs,
            Payload = audioData
        };
    }

    public T? DeserializeJson<T>()
    {
        if (Payload.Length == 0) return default;
        return JsonSerializer.Deserialize<T>(Payload);
    }
}

public readonly record struct TouchEvent(byte Action, int PointerId, float NormX, float NormY, float Pressure);

public static class PacketSerializer
{
    // Header format:
    // [0] : 1 byte type
    // [1..4] : 4 bytes payload length (LE)
    // [5..12] : 8 bytes timestamp_us (LE)
    public const int HeaderSize = 13;

    public static void WritePacket(Stream stream, Packet packet)
    {
        byte[] header = new byte[HeaderSize];
        header[0] = packet.Type;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1, 4), packet.Payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(5, 8), packet.TimestampUs);

        stream.Write(header, 0, HeaderSize);
        if (packet.Payload.Length > 0)
        {
            stream.Write(packet.Payload, 0, packet.Payload.Length);
        }
    }

    public static async ValueTask WritePacketAsync(Stream stream, Packet packet, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[HeaderSize];
        header[0] = packet.Type;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1, 4), packet.Payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(5, 8), packet.TimestampUs);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (packet.Payload.Length > 0)
        {
            await stream.WriteAsync(packet.Payload, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<Packet?> ReadPacketAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[HeaderSize];
        int totalRead = 0;
        while (totalRead < HeaderSize)
        {
            int read = await stream.ReadAsync(header.AsMemory(totalRead, HeaderSize - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null; // End of stream
            totalRead += read;
        }

        byte type = header[0];
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1, 4));
        long timestampUs = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(5, 8));

        if (length < 0 || length > 100 * 1024 * 1024)
        {
            throw new InvalidDataException($"Invalid packet length: {length}");
        }

        byte[] payload = new byte[length];
        totalRead = 0;
        while (totalRead < length)
        {
            int read = await stream.ReadAsync(payload.AsMemory(totalRead, length - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            totalRead += read;
        }

        return new Packet
        {
            Type = type,
            TimestampUs = timestampUs,
            Payload = payload
        };
    }
}
