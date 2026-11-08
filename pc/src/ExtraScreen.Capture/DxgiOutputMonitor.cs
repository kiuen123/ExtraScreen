using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ExtraScreen.Display;

namespace ExtraScreen.Capture;

/// <summary>
/// Theo dõi trạng thái màn hình DXGI và vị trí/trạng thái con trỏ chuột phần cứng.
/// Bổ sung cho project ExtraScreen.Capture dựa trên kiến trúc ALThreadMonitorIddcx / SLIDxgiManager
/// trích xuất từ spacedeskService.exe (qua REA).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DxgiOutputMonitor : IDisposable
{
    private const int CURSOR_SHOWING = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    private readonly DisplayManager _displayManager;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    public int LastCursorX { get; private set; }
    public int LastCursorY { get; private set; }
    public bool IsCursorVisible { get; private set; }
    public bool IsCursorOnTargetDisplay { get; private set; }

    public event Action<DisplayOutputInfo>? TargetDisplayGeometryChanged;

    public DxgiOutputMonitor(DisplayManager displayManager)
    {
        _displayManager = displayManager;
    }

    public void Start(int targetOutputIndex)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(targetOutputIndex, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _monitorTask?.Wait(200);
        }
        catch
        {
            // Ignore
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _monitorTask = null;
        }
    }

    private async Task MonitorLoopAsync(int targetOutputIndex, CancellationToken token)
    {
        int lastWidth = 0;
        int lastHeight = 0;
        int lastPosX = int.MinValue;
        int lastPosY = int.MinValue;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var displays = _displayManager.GetActiveDisplays();
                var target = displays.FirstOrDefault(d => d.Index == targetOutputIndex) ?? displays.FirstOrDefault();

                if (target != null)
                {
                    if (lastWidth != 0 &&
                        (target.Width != lastWidth || target.Height != lastHeight ||
                         target.PositionX != lastPosX || target.PositionY != lastPosY))
                    {
                        TargetDisplayGeometryChanged?.Invoke(target);
                    }

                    lastWidth = target.Width;
                    lastHeight = target.Height;
                    lastPosX = target.PositionX;
                    lastPosY = target.PositionY;

                    var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                    if (GetCursorInfo(ref ci))
                    {
                        IsCursorVisible = (ci.flags & CURSOR_SHOWING) != 0;
                        LastCursorX = ci.ptScreenPos.X;
                        LastCursorY = ci.ptScreenPos.Y;
                        IsCursorOnTargetDisplay =
                            ci.ptScreenPos.X >= target.PositionX &&
                            ci.ptScreenPos.X < target.PositionX + target.Width &&
                            ci.ptScreenPos.Y >= target.PositionY &&
                            ci.ptScreenPos.Y < target.PositionY + target.Height;
                    }
                }

                await Task.Delay(250, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient errors
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
