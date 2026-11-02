using System.Drawing;
using System.Windows;
using System.Windows.Media;
using ExtraScreen.Core;
using ExtraScreen.Display;
using ExtraScreen.Encoding;
using Forms = System.Windows.Forms;

namespace ExtraScreen.App;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly LogWindow _logWindow;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();

        _controller = new AppController();
        _logWindow = new LogWindow { Owner = this };

        // Setup NotifyIcon (Tray)
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "ExtraScreen - Màn hình phụ USB"
        };
        _notifyIcon.DoubleClick += (s, e) => ShowAndRestore();
        BuildTrayContextMenu();

        // Bind controller events
        _controller.StateChanged += () => Dispatcher.Invoke(UpdateUiState);
        _controller.LogMessage += msg => _logWindow.AppendLog(msg);

        // Populate Hardware Encoders
        PopulateEncoders();

        // Load UI initial state from settings
        LoadSettingsToUi();

        // Start controller
        _controller.Start();
        UpdateUiState();
    }

    private void BuildTrayContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        var headerItem = new Forms.ToolStripMenuItem("🖥️ ExtraScreen v1.0.0") { Enabled = false };
        var statusItem = new Forms.ToolStripMenuItem("⚪ Chưa kết nối điện thoại") { Enabled = false, Name = "StatusItem" };

        var extendItem = new Forms.ToolStripMenuItem("Chế độ mở rộng (Extend Display)", null, (s, e) =>
        {
            _controller.Settings.DisplayMode = DisplayMode.Extend;
            _controller.Display.CurrentMode = DisplayMode.Extend;
            RbExtend.IsChecked = true;
            BuildTrayContextMenu();
        }) { Checked = _controller.Settings.DisplayMode == DisplayMode.Extend };

        var mirrorItem = new Forms.ToolStripMenuItem("Chế độ phản chiếu (Mirror Display)", null, (s, e) =>
        {
            _controller.Settings.DisplayMode = DisplayMode.Mirror;
            _controller.Display.CurrentMode = DisplayMode.Mirror;
            RbMirror.IsChecked = true;
            BuildTrayContextMenu();
        }) { Checked = _controller.Settings.DisplayMode == DisplayMode.Mirror };

        var settingsItem = new Forms.ToolStripMenuItem("⚙️ Cài đặt chi tiết...", null, (s, e) => ShowAndRestore());
        var restartItem = new Forms.ToolStripMenuItem("🔄 Khởi động lại luồng stream", null, (s, e) => _controller.RestartStream());
        var disconnectItem = new Forms.ToolStripMenuItem("🔌 Ngắt kết nối thiết bị", null, async (s, e) => await _controller.DisconnectDeviceAsync());
        var exitItem = new Forms.ToolStripMenuItem("❌ Thoát ứng dụng", null, (s, e) => ExitApplication());

        menu.Items.Add(headerItem);
        menu.Items.Add(statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(extendItem);
        menu.Items.Add(mirrorItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(restartItem);
        menu.Items.Add(disconnectItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
    }

    private void PopulateEncoders()
    {
        var encoders = _controller.EncoderDetect.DetectAvailableEncoders();
        CmbEncoders.ItemsSource = encoders;

        var selected = encoders.FirstOrDefault(e => e.Type.ToString().Equals(_controller.Settings.SelectedEncoderType, StringComparison.OrdinalIgnoreCase))
                       ?? encoders.FirstOrDefault();
        if (selected != null)
        {
            CmbEncoders.SelectedItem = selected;
        }
    }

    private void LoadSettingsToUi()
    {
        if (_controller.Settings.DisplayMode == DisplayMode.Extend)
            RbExtend.IsChecked = true;
        else
            RbMirror.IsChecked = true;

        if (_controller.Settings.Codec.Equals("h265", StringComparison.OrdinalIgnoreCase))
            RbH265.IsChecked = true;
        else
            RbH264.IsChecked = true;

        if (_controller.Settings.ScalingPercentage == 125)
            RbScale125.IsChecked = true;
        else
            RbScale100.IsChecked = true;

        int bitrateMbps = _controller.Settings.BitrateBps / 1_000_000;
        SliderBitrate.Value = Math.Clamp(bitrateMbps, 10, 30);
        TxtBitrateValue.Text = $"{SliderBitrate.Value} Mbps";

        ChkAutoOpenApp.IsChecked = _controller.Settings.AutoOpenAppOnPlug;
        ChkAutoVirtualDisplay.IsChecked = _controller.Settings.AutoVirtualDisplay;
    }

    private void UpdateUiState()
    {
        // 1. Device Info
        if (_controller.ConnectedUsbDevice != null)
        {
            TxtDeviceName.Text = _controller.ConnectedUsbDevice.DisplayName;
            TxtDeviceDetails.Text = $"Mã thiết bị: {_controller.ConnectedUsbDevice.Serial} • Cổng nội bộ: 127.0.0.1:7070 (adb reverse)";
        }
        else
        {
            TxtDeviceName.Text = "Chưa kết nối điện thoại";
            TxtDeviceDetails.Text = "Cổng: 127.0.0.1:7070 (adb reverse) • Cắm cáp USB để kích hoạt";
        }

        // 2. Status Badge & Tray Text
        switch (_controller.Status)
        {
            case OverallStatus.Streaming:
                BadgeStatus.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // #10B981
                TxtBadgeStatus.Text = "🟢 ĐÃ KẾT NỐI (USB)";
                TxtBadgeStatus.Foreground = System.Windows.Media.Brushes.White;
                _notifyIcon.Text = $"ExtraScreen: Đang stream {_controller.ConnectedUsbDevice?.DisplayName}";
                UpdateTrayStatusText($"🟢 Đang stream: {_controller.ConnectedUsbDevice?.DisplayName} (1080p @ 60fps)");
                break;

            case OverallStatus.Unauthorized:
                BadgeStatus.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)); // #F59E0B
                TxtBadgeStatus.Text = "🟡 CHỜ CẤP QUYỀN";
                TxtBadgeStatus.Foreground = System.Windows.Media.Brushes.Black;
                _notifyIcon.Text = "ExtraScreen: Chờ bấm Cho phép gỡ lỗi USB";
                UpdateTrayStatusText("🟡 Chờ xác nhận USB Debugging trên máy...");
                break;

            case OverallStatus.Error:
                BadgeStatus.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // #EF4444
                TxtBadgeStatus.Text = "🔴 LỖI KẾT NỐI";
                TxtBadgeStatus.Foreground = System.Windows.Media.Brushes.White;
                _notifyIcon.Text = "ExtraScreen: Lỗi kết nối";
                UpdateTrayStatusText("🔴 Mất kết nối hoặc lỗi encoder");
                break;

            case OverallStatus.Idle:
            default:
                BadgeStatus.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)); // #334155
                TxtBadgeStatus.Text = "⚪ CHỜ THIẾT BỊ";
                TxtBadgeStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)); // #94A3B8
                _notifyIcon.Text = "ExtraScreen: Chờ cắm cáp USB";
                UpdateTrayStatusText("⚪ Chờ cắm cáp USB...");
                break;
        }

        // 3. Realtime Telemetry
        TxtStatFps.Text = $"{_controller.RealtimeFps:F1} / 60";
        TxtStatLatency.Text = $"~{_controller.RealtimeLatencyMs:F0} ms";
        TxtStatBitrate.Text = $"{_controller.RealtimeBitrateMbps:F1} Mbps";

        // 4. Driver Status
        bool hasVirtual = _controller.Display.IsDriverInstalled();
        TxtDriverStatus.Text = hasVirtual ? "✅ Sẵn sàng (Parsec / IddCx VDD)" : "⚠️ Chưa tìm thấy driver ảo (Dùng Mirror)";
        TxtDriverStatus.Foreground = hasVirtual 
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
    }

    private void UpdateTrayStatusText(string text)
    {
        if (_notifyIcon.ContextMenuStrip != null)
        {
            var item = _notifyIcon.ContextMenuStrip.Items["StatusItem"];
            if (item != null) item.Text = text;
        }
    }

    private void BtnRestartStream_Click(object sender, RoutedEventArgs e)
    {
        _controller.RestartStream();
    }

    private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        await _controller.DisconnectDeviceAsync();
    }

    private void BtnLogs_Click(object sender, RoutedEventArgs e)
    {
        _logWindow.Show();
        _logWindow.Activate();
    }

    private void BtnDriverGuide_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "ExtraScreen hỗ trợ 2 chế độ hiển thị:\n\n" +
            "1. Màn hình mở rộng (Extend Mode):\n" +
            "   Yêu cầu driver màn hình ảo (như Parsec VDD hoặc IddCx). Driver đã được cấu hình tự động trên máy.\n\n" +
            "2. Màn hình phản chiếu (Mirror Mode):\n" +
            "   Không cần cài thêm driver bất kỳ, phản chiếu trực tiếp màn hình máy tính đang có sang điện thoại.",
            "Hướng dẫn Driver Màn hình",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DisplayMode_Changed(object sender, RoutedEventArgs e)
    {
        if (RbExtend == null || _controller == null) return;
        _controller.Settings.DisplayMode = (RbExtend.IsChecked == true) ? DisplayMode.Extend : DisplayMode.Mirror;
        _controller.Display.CurrentMode = _controller.Settings.DisplayMode;
        _controller.Settings.Save();
        BuildTrayContextMenu();
    }

    private void Scaling_Changed(object sender, RoutedEventArgs e)
    {
        if (RbScale125 == null || _controller == null) return;
        _controller.Settings.ScalingPercentage = (RbScale125.IsChecked == true) ? 125 : 100;
        _controller.Display.ScalingPercentage = _controller.Settings.ScalingPercentage;
        _controller.Settings.Save();
    }

    private void Codec_Changed(object sender, RoutedEventArgs e)
    {
        if (RbH265 == null || _controller == null) return;
        _controller.Settings.Codec = (RbH265.IsChecked == true) ? "h265" : "h264";
        _controller.Settings.Save();
    }

    private void CmbEncoders_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CmbEncoders.SelectedItem is HardwareEncoderOption opt && _controller != null)
        {
            _controller.Settings.SelectedEncoderType = opt.Type.ToString();
            _controller.Settings.Save();
        }
    }

    private void SliderBitrate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtBitrateValue == null || _controller == null) return;
        int mbps = (int)SliderBitrate.Value;
        TxtBitrateValue.Text = $"{mbps} Mbps";
        _controller.Settings.BitrateBps = mbps * 1_000_000;
        _controller.Settings.Save();
    }

    private void AutoSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        _controller.Settings.AutoOpenAppOnPlug = ChkAutoOpenApp.IsChecked == true;
        _controller.Settings.AutoVirtualDisplay = ChkAutoVirtualDisplay.IsChecked == true;
        _controller.Settings.Save();
    }

    private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _notifyIcon.ShowBalloonTip(1500, "ExtraScreen", "Ứng dụng đang chạy nền trong khay hệ thống.", Forms.ToolTipIcon.Info);
    }

    private void ShowAndRestore()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExplicitExit = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _controller.Stop();
        _controller.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            e.Cancel = true;
            Hide();
            _notifyIcon.ShowBalloonTip(1000, "ExtraScreen", "Đang chạy ẩn trong khay hệ thống.", Forms.ToolTipIcon.Info);
        }
    }
}
