using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ExtraScreen.Encoding;

[SupportedOSPlatform("windows")]
public static class MmcssHelper
{
    [DllImport("Avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref int taskIndex);

    [DllImport("Avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

    [DllImport("Avrt.dll", SetLastError = true)]
    private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, int priority);

    private const int AvrtPriorityHigh = 2;

    /// <summary>
    /// Đăng ký luồng hiện tại vào Windows MMCSS (Multimedia Class Scheduler Service)
    /// Đảm bảo luồng capture/encoding/transmission không bị CPU starvation hoặc giật lag.
    /// </summary>
    public static IDisposable? EnableRealtimeMultimedia(string taskName = "DisplayPostProcessing")
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            int taskIndex = 0;
            IntPtr handle = AvSetMmThreadCharacteristics(taskName, ref taskIndex);
            if (handle != IntPtr.Zero)
            {
                AvSetMmThreadPriority(handle, AvrtPriorityHigh);
                return new MmcssScope(handle);
            }
        }
        catch
        {
            // Silently fallback if MMCSS service is unavailable
        }

        return null;
    }

    private sealed class MmcssScope : IDisposable
    {
        private IntPtr _handle;

        public MmcssScope(IntPtr handle)
        {
            _handle = handle;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                try
                {
                    AvRevertMmThreadCharacteristics(_handle);
                }
                catch
                {
                    // Ignored
                }
                _handle = IntPtr.Zero;
            }
        }
    }
}
