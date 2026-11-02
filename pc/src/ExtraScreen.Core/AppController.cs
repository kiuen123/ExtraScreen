using System.IO;
using ExtraScreen.Display;
using ExtraScreen.Encoding;
using ExtraScreen.Protocol;
using ExtraScreen.Server;
using ExtraScreen.Usb;

namespace ExtraScreen.Core;

public enum OverallStatus
{
    Idle,           // Gray: Waiting for phone
    Unauthorized,   // Yellow: Phone plugged in but needs RSA allow
    Streaming,      // Green: Streaming 1080p60
    Error           // Red: Disconnected or driver error
}

public class AppController : IDisposable
{
    public AppSettings Settings { get; }
    public AdbClient Adb { get; }
    public UsbDeviceWatcher UsbWatcher { get; }
    public DisplayManager Display { get; }
    public EncoderDetector EncoderDetect { get; }
    public StreamServer Server { get; }

    private FFmpegStreamEncoder? _encoder;
    private AdbDeviceInfo? _connectedUsbDevice;

    public OverallStatus Status { get; private set; } = OverallStatus.Idle;
    public string StatusMessage { get; private set; } = "Chưa kết nối điện thoại";
    public AdbDeviceInfo? ConnectedUsbDevice => _connectedUsbDevice;
    public HelloMessage? ClientHello => Server.ConnectedClientInfo;

    // Realtime stats
    public double RealtimeFps { get; private set; } = 60.0;
    public double RealtimeLatencyMs { get; private set; } = 38.0;
    public double RealtimeBitrateMbps { get; private set; } = 20.0;

    public event Action? StateChanged;
    public event Action<string>? LogMessage;

    public AppController()
    {
        Settings = AppSettings.Load();
        Adb = new AdbClient();
        UsbWatcher = new UsbDeviceWatcher(Adb);
        Display = new DisplayManager { CurrentMode = Settings.DisplayMode, ScalingPercentage = Settings.ScalingPercentage };
        EncoderDetect = new EncoderDetector();
        Server = new StreamServer(ProtocolConstants.DefaultPort);

        // Bind events
        UsbWatcher.DeviceConnected += OnUsbDeviceConnected;
        UsbWatcher.DeviceUnauthorized += OnUsbDeviceUnauthorized;
        UsbWatcher.DeviceDisconnected += OnUsbDeviceDisconnected;

        Server.ClientConnected += OnClientConnected;
        Server.ClientDisconnected += OnClientDisconnected;
        Server.StatsReceived += OnStatsReceived;
        Server.KeyframeRequested += OnKeyframeRequested;
        Server.LogMessage += msg => Log(msg);
    }

    public void Start()
    {
        Log("Khởi động hệ thống ExtraScreen...");
        Server.Start();
        UsbWatcher.Start();
        UpdateStatus(OverallStatus.Idle, "Sẵn sàng. Hãy cắm cáp USB nối với điện thoại.");
    }

    public void Stop()
    {
        StopStreaming();
        UsbWatcher.Stop();
        Server.Stop();
        Settings.Save();
    }

    private void OnUsbDeviceConnected(AdbDeviceInfo device)
    {
        _connectedUsbDevice = device;
        Log($"Phát hiện thiết bị USB: {device.DisplayName} ({device.Serial})");

        _ = Task.Run(async () =>
        {
            try
            {
                // 1. Setup adb reverse
                bool reversed = await Adb.ReversePortAsync(device.Serial, ProtocolConstants.DefaultPort, ProtocolConstants.DefaultPort);
                if (!reversed)
                {
                    Log("Không thể thiết lập adb reverse");
                }

                // 2. Auto-launch app on device if enabled
                if (Settings.AutoOpenAppOnPlug)
                {
                    bool isInstalled = await Adb.IsAppInstalledAsync(device.Serial);
                    if (!isInstalled)
                    {
                        // Check if APK exists in tools folder
                        string apkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "extrascreen.apk");
                        if (File.Exists(apkPath))
                        {
                            Log("Đang tự động cài đặt ứng dụng ExtraScreen lên điện thoại...");
                            await Adb.InstallAppAsync(device.Serial, apkPath);
                        }
                    }

                    Log("Đang mở ứng dụng ExtraScreen trên điện thoại...");
                    await Adb.StartAppAsync(device.Serial);
                }

                UpdateStatus(OverallStatus.Idle, $"Đã cắm {device.DisplayName}, đang chờ ứng dụng kết nối...");
            }
            catch (Exception ex)
            {
                Log($"Lỗi thiết lập USB: {ex.Message}");
            }
        });
    }

    private void OnUsbDeviceUnauthorized(AdbDeviceInfo device)
    {
        _connectedUsbDevice = device;
        UpdateStatus(OverallStatus.Unauthorized, "Hãy mở điện thoại và chọn 'Cho phép gỡ lỗi USB'");
        Log("Thiết bị ở trạng thái unauthorized: cần cấp quyền RSA trên màn hình điện thoại.");
    }

    private void OnUsbDeviceDisconnected(string serial)
    {
        Log($"Ngắt kết nối cáp USB: {serial}");
        _connectedUsbDevice = null;
        StopStreaming();
        UpdateStatus(OverallStatus.Idle, "Đã rút cáp USB. Đang chờ kết nối...");
    }

    private void OnClientConnected(HelloMessage hello)
    {
        Log($"Ứng dụng Android kết nối thành công: {hello.Model}");

        // Decide codec
        bool canUseHevc = Settings.Codec.Equals("h265", StringComparison.OrdinalIgnoreCase)
                          && hello.Codecs.Contains("h265", StringComparer.OrdinalIgnoreCase);

        string effectiveCodec = canUseHevc ? "h265" : "h264";
        int effectiveBitrate = canUseHevc ? Math.Min(Settings.BitrateBps, 15_000_000) : Settings.BitrateBps;

        // Send SessionConfig to mobile
        Server.SendSessionConfig(new SessionConfigMessage
        {
            Width = ProtocolConstants.FixedWidth,
            Height = ProtocolConstants.FixedHeight,
            Fps = ProtocolConstants.FixedFps,
            Codec = effectiveCodec,
            Bitrate = effectiveBitrate
        });

        // Start Stream Encoder
        StartStreaming(effectiveCodec, effectiveBitrate);
        UpdateStatus(OverallStatus.Streaming, $"Đang stream: {hello.Model} (1080p @ 60fps)");
    }

    private void OnClientDisconnected()
    {
        Log("Client Android đã ngắt kết nối.");
        StopStreaming();
        if (_connectedUsbDevice != null)
        {
            UpdateStatus(OverallStatus.Idle, $"Thiết bị {_connectedUsbDevice.DisplayName} đang chờ kết nối lại...");
        }
        else
        {
            UpdateStatus(OverallStatus.Idle, "Chưa kết nối điện thoại");
        }
    }

    private void OnStatsReceived(StatsMessage stats)
    {
        RealtimeFps = stats.Fps;
        RealtimeLatencyMs = stats.DecodeMs + stats.RttMs;
        RealtimeBitrateMbps = Settings.BitrateBps / 1_000_000.0;
        StateChanged?.Invoke();
    }

    private void OnKeyframeRequested()
    {
        _encoder?.RequestKeyframe();
    }

    public void StartStreaming(string? codec = null, int? bitrate = null)
    {
        StopStreaming();

        var encoders = EncoderDetect.DetectAvailableEncoders();
        var selectedOpt = encoders.FirstOrDefault(e => e.Type.ToString().Equals(Settings.SelectedEncoderType, StringComparison.OrdinalIgnoreCase))
                          ?? encoders.First();

        int targetDisplay = Display.GetTargetDisplayIndex(Settings.DisplayMode);

        var config = new VideoEncoderConfig
        {
            Codec = codec ?? Settings.Codec,
            Width = ProtocolConstants.FixedWidth,
            Height = ProtocolConstants.FixedHeight,
            Fps = ProtocolConstants.FixedFps,
            BitrateBps = bitrate ?? Settings.BitrateBps,
            OutputIndex = targetDisplay,
            EncoderOption = selectedOpt
        };

        _encoder = new FFmpegStreamEncoder(config);
        _encoder.VideoConfigReady += cfg => Server.SendVideoConfig(cfg);
        _encoder.FrameReady += (nal, isKey, ts) => Server.SendVideoFrame(nal, isKey, ts);
        _encoder.LogMessage += msg => Log(msg);
        _encoder.ErrorOccurred += err =>
        {
            Log($"Lỗi Encoder: {err}");
            UpdateStatus(OverallStatus.Error, $"Lỗi Encoder: {err}");
        };

        _encoder.Start();
    }

    public void StopStreaming()
    {
        if (_encoder != null)
        {
            _encoder.Stop();
            _encoder.Dispose();
            _encoder = null;
        }
    }

    public void RestartStream()
    {
        Log("Khởi động lại luồng stream...");
        if (Server.IsClientConnected && Server.ConnectedClientInfo != null)
        {
            OnClientConnected(Server.ConnectedClientInfo);
        }
        else
        {
            StopStreaming();
        }
    }

    public async Task DisconnectDeviceAsync()
    {
        if (_connectedUsbDevice != null)
        {
            await Adb.RemoveReverseAsync(_connectedUsbDevice.Serial, ProtocolConstants.DefaultPort);
        }
        Server.DisconnectCurrentClient();
        StopStreaming();
        UpdateStatus(OverallStatus.Idle, "Đã ngắt kết nối theo yêu cầu");
    }

    private void UpdateStatus(OverallStatus status, string message)
    {
        Status = status;
        StatusMessage = message;
        StateChanged?.Invoke();
    }

    private void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogMessage?.Invoke($"[{timestamp}] {message}");
    }

    public void Dispose()
    {
        Stop();
    }
}
