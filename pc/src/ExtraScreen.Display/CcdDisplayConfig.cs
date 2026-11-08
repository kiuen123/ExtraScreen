using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ExtraScreen.Display;

/// <summary>
/// Bộ wrapper Windows Connecting and Configuring Displays (CCD) API.
/// Trích xuất cấu trúc P/Invoke, Struct và Enum từ namespace CCD.CCDWrapper / CCD.Struct / CCD.Enum
/// trong spacedeskConsole.exe thông qua REA (inspect-managed-native-boundaries & inspect-managed-members).
/// </summary>
[SupportedOSPlatform("windows")]
public static class CcdDisplayConfig
{
    public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    [Flags]
    public enum QueryDisplayFlags : uint
    {
        Zero = 0x0,
        AllPaths = 0x00000001,
        OnlyActivePaths = 0x00000002,
        DatabaseCurrent = 0x00000004,
        VirtualModeAware = 0x00000010,
        IncludeHmd = 0x00000020,
        VirtualRefreshRateAware = 0x00000040
    }

    [Flags]
    public enum SdcFlags : uint
    {
        Zero = 0x0,
        TopologyInternal = 0x00000001,
        TopologyClone = 0x00000002,
        TopologyExtend = 0x00000004,
        TopologyExternal = 0x00000008,
        TopologySupplied = 0x00000010,
        UseSuppliedDisplayConfig = 0x00000020,
        Validate = 0x00000040,
        Apply = 0x00000080,
        NoOptimization = 0x00000100,
        SaveToDatabase = 0x00000200,
        AllowChanges = 0x00000400,
        PathPersistIfRequired = 0x00000800,
        ForceModeEnumeration = 0x00001000,
        AllowPathOrderChanges = 0x00002000,
        UseDatabaseCurrent = TopologyInternal | TopologyClone | TopologyExtend | TopologyExternal
    }

    public enum DisplayConfigDeviceInfoType : int
    {
        GetSourceName = 1,
        GetTargetName = 2,
        GetTargetPreferredMode = 3,
        GetAdapterName = 4,
        SetTargetPersistence = 5
    }

    public enum DisplayConfigModeInfoType : uint
    {
        Zero = 0,
        Source = 1,
        Target = 2,
        DesktopImage = 3
    }

    public enum DisplayConfigPixelFormat : uint
    {
        Zero = 0,
        Pixelformat8Bpp = 1,
        Pixelformat16Bpp = 2,
        Pixelformat24Bpp = 3,
        Pixelformat32Bpp = 4,
        PixelformatNongdi = 5
    }

    public enum DisplayConfigRotation : uint
    {
        Zero = 0,
        Identity = 1,
        Rotate90 = 2,
        Rotate180 = 3,
        Rotate270 = 4
    }

    public enum DisplayConfigScaling : uint
    {
        Zero = 0,
        Identity = 1,
        Centered = 2,
        Stretched = 3,
        Aspectratiocenteredmax = 4,
        Custom = 5,
        Preferred = 128
    }

    [Flags]
    public enum DisplayConfigFlags : uint
    {
        Zero = 0x0,
        PathActive = 0x00000001
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfig2DRegion
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigRational
    {
        public uint numerator;
        public uint denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigPathSourceInfo
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigPathTargetInfo
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public DisplayConfigRotation rotation;
        public DisplayConfigScaling scaling;
        public DisplayConfigRational refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo sourceInfo;
        public DisplayConfigPathTargetInfo targetInfo;
        public DisplayConfigFlags flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigSourceMode
    {
        public uint width;
        public uint height;
        public DisplayConfigPixelFormat pixelFormat;
        public PointL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigVideoSignalInfo
    {
        public ulong pixelRate;
        public DisplayConfigRational hSyncFreq;
        public DisplayConfigRational vSyncFreq;
        public DisplayConfig2DRegion activeSize;
        public DisplayConfig2DRegion totalSize;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct DisplayConfigModeInfo
    {
        [FieldOffset(0)]
        public DisplayConfigModeInfoType infoType;

        [FieldOffset(4)]
        public uint id;

        [FieldOffset(8)]
        public LUID adapterId;

        [FieldOffset(16)]
        public DisplayConfigTargetMode targetMode;

        [FieldOffset(16)]
        public DisplayConfigSourceMode sourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigDeviceInfoHeader
    {
        public DisplayConfigDeviceInfoType type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigSetTargetPersistence
    {
        public DisplayConfigDeviceInfoHeader header;
        public uint bootPersistenceOn;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        QueryDisplayFlags flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        QueryDisplayFlags flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[]? pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[]? modeInfoArray,
        SdcFlags flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetTargetPersistence requestPacket);

    /// <summary>
    /// Chuyển đổi nhanh chế độ hiển thị của Windows (Extend hoặc Clone/Mirror) bằng CCD API.
    /// </summary>
    public static bool ApplyDisplayTopology(DisplayMode mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        SdcFlags topologyFlag = mode == DisplayMode.Extend
            ? (SdcFlags.TopologyExtend | SdcFlags.Apply)
            : (SdcFlags.TopologyClone | SdcFlags.Apply);

        int result = SetDisplayConfig(0, null, 0, null, topologyFlag);
        return result == 0;
    }

    /// <summary>
    /// Lấy danh sách tên thân thiện (EDID Friendly Name) và đường dẫn thiết bị của tất cả màn hình đang hoạt động qua CCD API.
    /// </summary>
    public static List<(string FriendlyName, string DevicePath, uint TargetId, LUID AdapterId)> QueryActiveTargets()
    {
        var targets = new List<(string, string, uint, LUID)>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return targets;

        int err = GetDisplayConfigBufferSizes(QueryDisplayFlags.OnlyActivePaths, out uint pathCount, out uint modeCount);
        if (err != 0 || pathCount == 0) return targets;

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];

        err = QueryDisplayConfig(QueryDisplayFlags.OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (err != 0) return targets;

        for (int i = 0; i < pathCount; i++)
        {
            var targetName = new DisplayConfigTargetDeviceName
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DisplayConfigDeviceInfoType.GetTargetName,
                    size = Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                    adapterId = paths[i].targetInfo.adapterId,
                    id = paths[i].targetInfo.id
                }
            };

            if (DisplayConfigGetDeviceInfo(ref targetName) == 0)
            {
                targets.Add((
                    targetName.monitorFriendlyDeviceName ?? string.Empty,
                    targetName.monitorDevicePath ?? string.Empty,
                    paths[i].targetInfo.id,
                    paths[i].targetInfo.adapterId));
            }
        }

        return targets;
    }
}
