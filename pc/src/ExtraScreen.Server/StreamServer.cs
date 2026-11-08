using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using ExtraScreen.Protocol;

namespace ExtraScreen.Server;

public class StreamServer : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    private TcpClient? _currentClient;
    private NetworkStream? _currentStream;
    private Channel<Packet>? _sendChannel;
    private Task? _sendTask;
    private Task? _receiveTask;
    private int _unackedFrames;
    private bool _droppedSinceLastKeyframe;

    public event Action<HelloMessage>? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action<StatsMessage>? StatsReceived;
    public event Action<TouchEvent>? TouchReceived;
    public event Action? KeyframeRequested;
    public event Action<long>? FrameAckReceived;
    public event Action<string>? LogMessage;
    public event Action<string>? ErrorOccurred;

    public bool IsClientConnected => _currentClient != null && _currentClient.Connected;
    public HelloMessage? ConnectedClientInfo { get; private set; }

    public StreamServer(int port = ProtocolConstants.DefaultPort)
    {
        _port = port;
    }

    public void Start()
    {
        if (_listener != null) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        LogMessage?.Invoke($"Server listening on 127.0.0.1:{_port}");
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        DisconnectCurrentClient();

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Ignored
        }
        finally
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void SendSessionConfig(SessionConfigMessage config)
    {
        EnqueuePacket(Packet.CreateJson(ProtocolConstants.TypeSessionConfig, config));
    }

    public void SendVideoConfig(byte[] configData)
    {
        EnqueuePacket(Packet.CreateVideoConfig(configData));
    }

    public void SendVideoFrame(byte[] nalUnits, bool isKeyframe, long timestampUs)
    {
        if (isKeyframe)
        {
            _droppedSinceLastKeyframe = false;
        }
        EnqueuePacket(Packet.CreateVideoFrame(nalUnits, isKeyframe, timestampUs));
    }

    public void SendAudioFrame(byte[] audioData, long timestampUs)
    {
        EnqueuePacket(Packet.CreateAudioFrame(audioData, timestampUs));
    }

    private void EnqueuePacket(Packet packet)
    {
        var channel = _sendChannel;
        if (channel != null && IsClientConnected)
        {
            // Kiểm soát luồng độ trễ thấp (học từ FLOW_CONTROL_ACK của Spacedesk):
            // Nếu hàng đợi tích tụ > 6 gói hoặc client chưa ACK > 12 frames (~200ms), bỏ P-frame và yêu cầu IDR mới
            if (packet.Type == ProtocolConstants.TypeVideoFrame)
            {
                bool isDeltaFrame = packet.Payload.Length > 0 && packet.Payload[0] == 0;
                int queuedCount = channel.Reader.Count;
                int inflight = Volatile.Read(ref _unackedFrames);

                if (isDeltaFrame && (queuedCount > 6 || inflight > 12 || _droppedSinceLastKeyframe))
                {
                    if (!_droppedSinceLastKeyframe)
                    {
                        _droppedSinceLastKeyframe = true;
                        KeyframeRequested?.Invoke();
                    }
                    return;
                }

                Interlocked.Increment(ref _unackedFrames);
            }
            channel.Writer.TryWrite(packet);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                client.NoDelay = true;
                client.SendBufferSize = 1024 * 1024;
                client.ReceiveBufferSize = 256 * 1024;

                LogMessage?.Invoke($"New client connection from {client.Client.RemoteEndPoint}");

                // Disconnect previous client if any
                DisconnectCurrentClient();

                _currentClient = client;
                _currentStream = client.GetStream();
                Interlocked.Exchange(ref _unackedFrames, 0);
                _droppedSinceLastKeyframe = false;
                _sendChannel = Channel.CreateBounded<Packet>(new BoundedChannelOptions(32)
                {
                    FullMode = BoundedChannelFullMode.DropOldest
                });

                _sendTask = Task.Run(() => SendLoopAsync(_currentStream, _sendChannel.Reader, token));
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_currentStream, token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }
    }

    private async Task SendLoopAsync(NetworkStream stream, ChannelReader<Packet> reader, CancellationToken token)
    {
        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var packet))
                {
                    await PacketSerializer.WritePacketAsync(stream, packet, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Send loop ended: {ex.Message}");
            DisconnectCurrentClient();
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var packet = await PacketSerializer.ReadPacketAsync(stream, token).ConfigureAwait(false);
                if (packet == null)
                {
                    LogMessage?.Invoke("Client disconnected (stream ended).");
                    break;
                }

                HandleIncomingPacket(packet);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Receive loop error: {ex.Message}");
        }
        finally
        {
            DisconnectCurrentClient();
        }
    }

    private void HandleIncomingPacket(Packet packet)
    {
        switch (packet.Type)
        {
            case ProtocolConstants.TypeHello:
                var hello = packet.DeserializeJson<HelloMessage>();
                if (hello != null)
                {
                    ConnectedClientInfo = hello;
                    LogMessage?.Invoke($"Received HELLO from {hello.Model} ({hello.Width}x{hello.Height}, DPI={hello.Dpi}, Codecs=[{string.Join(",", hello.Codecs)}])");
                    ClientConnected?.Invoke(hello);
                }
                break;

            case ProtocolConstants.TypeRequestKeyframe:
                LogMessage?.Invoke("Client requested IDR Keyframe.");
                KeyframeRequested?.Invoke();
                break;

            case ProtocolConstants.TypeFrameAck:
                int remaining = Interlocked.Decrement(ref _unackedFrames);
                if (remaining < 0)
                {
                    Interlocked.Exchange(ref _unackedFrames, 0);
                }
                FrameAckReceived?.Invoke(packet.TimestampUs);
                break;

            case ProtocolConstants.TypePing:
                long pingTs = packet.Payload.Length >= 8 
                    ? BinaryPrimitives.ReadInt64LittleEndian(packet.Payload) 
                    : packet.TimestampUs;
                EnqueuePacket(Packet.CreatePong(pingTs));
                break;

            case ProtocolConstants.TypeStats:
                var stats = packet.DeserializeJson<StatsMessage>();
                if (stats != null)
                {
                    StatsReceived?.Invoke(stats);
                }
                break;

            case ProtocolConstants.TypeTouch:
                try
                {
                    var touch = Packet.ParseTouch(packet.Payload);
                    TouchReceived?.Invoke(touch);
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Invalid touch event: {ex.Message}");
                }
                break;

            case ProtocolConstants.TypeBye:
                var bye = packet.DeserializeJson<ByeMessage>();
                LogMessage?.Invoke($"Client BYE: {bye?.Reason}");
                DisconnectCurrentClient();
                break;
        }
    }

    public void DisconnectCurrentClient()
    {
        var client = Interlocked.Exchange(ref _currentClient, null);
        if (client == null) return;

        try
        {
            _sendChannel?.Writer.TryComplete();
            _currentStream?.Dispose();
            client.Dispose();
        }
        catch
        {
            // Ignored
        }
        finally
        {
            _currentStream = null;
            _sendChannel = null;
            ConnectedClientInfo = null;
            ClientDisconnected?.Invoke();
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
