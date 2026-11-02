namespace ExtraScreen.Usb;

public class UsbDeviceWatcher
{
    private readonly AdbClient _adbClient;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private readonly Dictionary<string, DeviceState> _lastKnownDevices = new();

    public event Action<AdbDeviceInfo>? DeviceConnected;
    public event Action<AdbDeviceInfo>? DeviceUnauthorized;
    public event Action<string>? DeviceDisconnected;

    public UsbDeviceWatcher(AdbClient adbClient)
    {
        _adbClient = adbClient;
    }

    public bool IsRunning => _monitorTask != null && !_monitorTask.IsCompleted;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _monitorTask = null;
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var currentDevices = await _adbClient.GetDevicesAsync(token).ConfigureAwait(false);
                var currentMap = currentDevices.ToDictionary(d => d.Serial, d => d);

                // Detect new or state-changed devices
                foreach (var device in currentDevices)
                {
                    if (!_lastKnownDevices.TryGetValue(device.Serial, out var lastState) || lastState != device.State)
                    {
                        _lastKnownDevices[device.Serial] = device.State;
                        if (device.State == DeviceState.Device)
                        {
                            DeviceConnected?.Invoke(device);
                        }
                        else if (device.State == DeviceState.Unauthorized)
                        {
                            DeviceUnauthorized?.Invoke(device);
                        }
                    }
                }

                // Detect disconnected devices
                var disconnected = _lastKnownDevices.Keys.Where(s => !currentMap.ContainsKey(s)).ToList();
                foreach (var serial in disconnected)
                {
                    _lastKnownDevices.Remove(serial);
                    DeviceDisconnected?.Invoke(serial);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // ADB server might be temporarily busy or restarting
            }

            try
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
