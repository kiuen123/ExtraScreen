using System.Runtime.InteropServices;

namespace ExtraScreen.Display;

public enum DisplayMode
{
    Extend,
    Mirror
}

public class DisplayOutputInfo
{
    public int Index { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string MonitorFriendlyName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsVirtual { get; set; }

    public string DisplayTitle => $"{Index}: {(!string.IsNullOrEmpty(MonitorFriendlyName) ? MonitorFriendlyName : DeviceName)} ({Width}x{Height} @ {RefreshRate}Hz){(IsPrimary ? " [Chính]" : "")}{(IsVirtual ? " [Ảo]" : "")}";
}
