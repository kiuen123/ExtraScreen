using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ExtraScreen.Display;

[SupportedOSPlatform("windows")]
public class DisplayManager
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    public DisplayMode CurrentMode { get; set; } = DisplayMode.Extend;
    public int ScalingPercentage { get; set; } = 100;

    public List<DisplayOutputInfo> GetActiveDisplays()
    {
        var list = new List<DisplayOutputInfo>();
        var dev = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        uint devNum = 0;
        int outputIdx = 0;

        while (EnumDisplayDevices(null, devNum, ref dev, 0))
        {
            if ((dev.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                var devMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                int posX = 0;
                int posY = 0;
                int width = 1920;
                int height = 1080;
                int freq = 60;

                if (EnumDisplaySettings(dev.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode))
                {
                    posX = devMode.dmPositionX;
                    posY = devMode.dmPositionY;
                    width = devMode.dmPelsWidth;
                    height = devMode.dmPelsHeight;
                    freq = devMode.dmDisplayFrequency;
                }

                bool isPrimary = (dev.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                bool isVirtual = dev.DeviceString.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                 dev.DeviceString.Contains("Parsec", StringComparison.OrdinalIgnoreCase) ||
                                 dev.DeviceString.Contains("IddCx", StringComparison.OrdinalIgnoreCase) ||
                                 dev.DeviceString.Contains("USB", StringComparison.OrdinalIgnoreCase);

                list.Add(new DisplayOutputInfo
                {
                    Index = outputIdx,
                    DeviceName = dev.DeviceName,
                    MonitorFriendlyName = dev.DeviceString,
                    PositionX = posX,
                    PositionY = posY,
                    Width = width,
                    Height = height,
                    RefreshRate = freq,
                    IsPrimary = isPrimary,
                    IsVirtual = isVirtual
                });

                outputIdx++;
            }

            devNum++;
            dev = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }

        return list;
    }

    public int GetTargetDisplayIndex(DisplayMode mode)
    {
        var displays = GetActiveDisplays().Where(d => d.Width > 0 && d.Height > 0).ToList();
        if (displays.Count == 0) return 0;

        if (mode == DisplayMode.Mirror)
        {
            // In DXGI ddagrab, Primary display is index 0
            return 0;
        }

        // Extend mode: Use secondary display (output_idx 1) if available
        if (displays.Count > 1)
        {
            return 1;
        }

        return 0;
    }

    public bool IsDriverInstalled()
    {
        var displays = GetActiveDisplays();
        if (displays.Any(d => d.IsVirtual)) return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var ccdTargets = CcdDisplayConfig.QueryActiveTargets();
            return ccdTargets.Any(t =>
                t.FriendlyName.Contains("spacedesk", StringComparison.OrdinalIgnoreCase) ||
                t.FriendlyName.Contains("Parsec", StringComparison.OrdinalIgnoreCase) ||
                t.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                t.DevicePath.Contains("VID_DATRONICSOFT", StringComparison.OrdinalIgnoreCase) ||
                t.DevicePath.Contains("IddCx", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    /// <summary>
    /// Tự động áp dụng chế độ Extend hoặc Mirror trên Windows thông qua CCD API (học từ CCDWrapper của Spacedesk).
    /// </summary>
    public bool ApplyModeToWindows(DisplayMode mode)
    {
        CurrentMode = mode;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        var activeDisplays = GetActiveDisplays();
        if (activeDisplays.Count > 1 || IsDriverInstalled())
        {
            return CcdDisplayConfig.ApplyDisplayTopology(mode);
        }
        return false;
    }
}
