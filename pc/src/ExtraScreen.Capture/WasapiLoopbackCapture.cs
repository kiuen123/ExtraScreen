using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ExtraScreen.Capture;

/// <summary>
/// Bộ thu âm thanh hệ thống PC (WASAPI Loopback) xuất ra luồng PCM 48kHz, 16-bit, Stereo (2 kênh).
/// Tương ứng với kiến trúc ALStreamFrameAccessorAudioCyclic trên PC và khớp tuyệt đối với
/// bộ phát H0 (AudioTrack 48000Hz, CHANNEL_OUT_STEREO=12, ENCODING_PCM_16BIT=2) của Spacedesk APK (qua REA).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiLoopbackCapture : IDisposable
{
    public const int TargetSampleRate = 48000;
    public const int TargetChannels = 2;
    public const int TargetBitsPerSample = 16;

    private const int CLSCTX_ALL = 23;
    private const int eRender = 0;
    private const int eConsole = 0;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    private const ushort WAVE_FORMAT_PCM = 1;

    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, ref WAVEFORMATEX pFormat, IntPtr audioSessionGuid);
        int GetBufferSize(out uint pNumBufferFrames);
        int GetStreamLatency(out long phnsLatency);
        int GetCurrentPadding(out uint pNumPaddingFrames);
        int IsFormatSupported(int shareMode, ref WAVEFORMATEX pFormat, out IntPtr ppClosestMatch);
        int GetMixFormat(out IntPtr ppDeviceFormat);
        int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr eventHandle);
        int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        int ReleaseBuffer(uint numFramesRead);
        int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    public event Action<byte[], long>? AudioFrameReady;
    public event Action<string>? LogMessage;

    public bool IsRunning => _captureTask != null && !_captureTask.IsCompleted;

    public void Start()
    {
        if (IsRunning || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _captureTask?.Wait(300);
        }
        catch
        {
            // Ignore
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _captureTask = null;
        }
    }

    private void CaptureLoop(CancellationToken token)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;

        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (enumeratorType == null) return;

            enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(enumeratorType);
            if (enumerator == null) return;

            if (enumerator.GetDefaultAudioEndpoint(eRender, eConsole, out device) != 0 || device == null)
            {
                return;
            }

            Guid iidAudioClient = IID_IAudioClient;
            if (device.Activate(ref iidAudioClient, CLSCTX_ALL, IntPtr.Zero, out object clientObj) != 0)
            {
                return;
            }
            audioClient = (IAudioClient)clientObj;

            ushort blockAlign = (ushort)(TargetChannels * (TargetBitsPerSample / 8));
            var waveFormat = new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_PCM,
                nChannels = TargetChannels,
                nSamplesPerSec = TargetSampleRate,
                wBitsPerSample = TargetBitsPerSample,
                nBlockAlign = blockAlign,
                nAvgBytesPerSec = (uint)(TargetSampleRate * blockAlign),
                cbSize = 0
            };

            // 20ms buffer duration (200,000 * 100ns) với AUTOCONVERTPCM về 48kHz 16-bit Stereo
            uint flags = AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            int hr = audioClient.Initialize(AUDCLNT_SHAREMODE_SHARED, flags, 200_000, 0, ref waveFormat, IntPtr.Zero);
            if (hr != 0)
            {
                LogMessage?.Invoke($"Không thể khởi tạo WASAPI Loopback (HRESULT: 0x{hr:X8})");
                return;
            }

            Guid iidCaptureClient = IID_IAudioCaptureClient;
            if (audioClient.GetService(ref iidCaptureClient, out object captureObj) != 0)
            {
                return;
            }
            captureClient = (IAudioCaptureClient)captureObj;

            if (audioClient.Start() != 0) return;

            LogMessage?.Invoke("Đã bật truyền âm thanh WASAPI Loopback (48kHz 16-bit Stereo).");

            const int bytesPerFrame = 4; // 2 channels * 2 bytes (16-bit)
            const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(5);

                while (captureClient.GetNextPacketSize(out uint packetLength) == 0 && packetLength > 0)
                {
                    if (captureClient.GetBuffer(out IntPtr dataPtr, out uint numFramesToRead, out uint dwFlags, out _, out _) != 0)
                    {
                        break;
                    }

                    try
                    {
                        int byteCount = (int)(numFramesToRead * bytesPerFrame);
                        if (byteCount > 0 && (dwFlags & AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                        {
                            byte[] pcmBytes = new byte[byteCount];
                            Marshal.Copy(dataPtr, pcmBytes, 0, byteCount);
                            long ts = Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;
                            AudioFrameReady?.Invoke(pcmBytes, ts);
                        }
                    }
                    finally
                    {
                        captureClient.ReleaseBuffer(numFramesToRead);
                    }
                }
            }

            audioClient.Stop();
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"WASAPI Loopback dừng: {ex.Message}");
        }
        finally
        {
            if (captureClient != null && Marshal.IsComObject(captureClient)) Marshal.ReleaseComObject(captureClient);
            if (audioClient != null && Marshal.IsComObject(audioClient)) Marshal.ReleaseComObject(audioClient);
            if (device != null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
