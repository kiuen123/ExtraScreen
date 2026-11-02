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

    public event Action<HelloMessage>? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action<StatsMessage>? StatsReceived;
    public event Action? KeyframeRequested;
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
        EnqueuePacket(Packet.CreateVideoFrame(nalUnits, isKeyframe, timestampUs));
    }

    private void EnqueuePacket(Packet packet)
    {
        var channel = _sendChannel;
        if (channel != null && IsClientConnected)
        {
            // Drop delta frames if queue is backing up (>120 frames = ~2 seconds) to preserve low-latency glass-to-glass
            if (channel.Reader.Count > 120 && packet.Type == ProtocolConstants.TypeVideoFrame)
            {
                // Drop delta frame
                if (packet.Payload.Length > 0 && packet.Payload[0] == 0)
                {
                    return;
                }
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
                _sendChannel = Channel.CreateBounded<Packet>(new BoundedChannelOptions(200)
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

            case ProtocolConstants.TypeBye:
                var bye = packet.DeserializeJson<ByeMessage>();
                LogMessage?.Invoke($"Client BYE: {bye?.Reason}");
                DisconnectCurrentClient();
                break;
        }
    }

    public void DisconnectCurrentClient()
    {
        if (_currentClient == null) return;

        try
        {
            _sendChannel?.Writer.TryComplete();
            _currentStream?.Dispose();
            _currentClient?.Dispose();
        }
        catch
        {
            // Ignored
        }
        finally
        {
            _currentStream = null;
            _currentClient = null;
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
