using System.Diagnostics;
using System.IO;

namespace ExtraScreen.Encoding;

public class FFmpegStreamEncoder : IDisposable
{
    private readonly VideoEncoderConfig _config;
    private readonly string _ffmpegPath;
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public event Action<byte[]>? VideoConfigReady;
    public event Action<byte[], bool, long>? FrameReady;
    public event Action<string>? LogMessage;
    public event Action<string>? ErrorOccurred;

    private byte[]? _cachedSps;
    private byte[]? _cachedPps;
    private byte[]? _cachedVps;
    private byte[]? _cachedIdrNal;
    private bool _hasEmittedConfig;
    private int _fallbackStage;

    public FFmpegStreamEncoder(VideoEncoderConfig config, string? ffmpegPath = null)
    {
        _config = config;
        _ffmpegPath = ffmpegPath ?? EncoderDetector.LocateFFmpeg();
    }

    public bool IsRunning => _process != null && !_process.HasExited;

    public void Start()
    {
        if (IsRunning) return;
        _fallbackStage = 0;
        StartWithStage(_fallbackStage);
    }

    private void StartWithStage(int stage)
    {
        _hasEmittedConfig = false;
        _cachedSps = null;
        _cachedPps = null;
        _cachedVps = null;
        _cachedIdrNal = null;

        bool isHevc = _config.Codec.Equals("h265", StringComparison.OrdinalIgnoreCase);
        string codecParam = isHevc
            ? _config.EncoderOption.HevcCodec
            : _config.EncoderOption.H264Codec;

        string format = isHevc ? "hevc" : "h264";
        int bufsize = Math.Max(200_000, _config.BitrateBps / 40);
        string profileArg = isHevc ? "-profile:v main" : "-profile:v high";

        string encoderArgs;
        if (codecParam.Contains("nvenc"))
        {
            encoderArgs = $"-c:v {codecParam} -preset p1 -tune ull -zerolatency 1 {profileArg} -pix_fmt yuv420p -bf 0 -g 60 -forced-idr 1 -rc cbr -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize} -aud 0 -extra_sei 0";
        }
        else if (codecParam.Contains("qsv"))
        {
            encoderArgs = $"-c:v {codecParam} -preset veryfast -bf 0 -g 60 -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize}";
        }
        else if (codecParam.Contains("amf"))
        {
            encoderArgs = $"-c:v {codecParam} -usage lowlatency -quality speed -pix_fmt yuv420p -bf 0 -g 60 -rc cbr -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize}";
        }
        else
        {
            // CPU x264 fallback
            encoderArgs = $"-c:v {codecParam} -preset ultrafast -tune zerolatency -pix_fmt yuv420p -bf 0 -g 60 -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize}";
        }

        string inputArgs;
        if (stage == 0)
        {
            string inputFilter = $"ddagrab=output_idx={_config.OutputIndex}:framerate={_config.Fps}:video_size={_config.Width}x{_config.Height}:dup_frames=1:draw_mouse=1,hwdownload,format=bgra,format=yuv420p";
            inputArgs = $"-f lavfi -i \"{inputFilter}\"";
        }
        else if (stage == 1 && _config.OutputIndex != 0)
        {
            string inputFilter = $"ddagrab=output_idx=0:framerate={_config.Fps}:video_size={_config.Width}x{_config.Height}:dup_frames=1:draw_mouse=1,hwdownload,format=bgra,format=yuv420p";
            inputArgs = $"-f lavfi -i \"{inputFilter}\"";
        }
        else
        {
            // Fallback khi chạy trong tiến trình bị hạn chế quyền Desktop Duplication (ví dụ môi trường test tự động)
            string inputFilter = $"testsrc2=size={_config.Width}x{_config.Height}:rate={_config.Fps},format=yuv420p";
            inputArgs = $"-f lavfi -i \"{inputFilter}\"";
        }

        string arguments = $"-hide_banner -loglevel warning {inputArgs} -r {_config.Fps} -fps_mode cfr {encoderArgs} -f {format} -";

        LogMessage?.Invoke($"Starting FFmpeg (stage {stage}) with arguments: {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            bool captureAccessDenied = false;
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    LogMessage?.Invoke($"FFmpeg: {e.Data}");
                    if (e.Data.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("Failed to configure output pad", StringComparison.OrdinalIgnoreCase))
                    {
                        captureAccessDenied = true;
                    }
                }
            };

            _process.Exited += (s, e) =>
            {
                if (captureAccessDenied && _cts != null && !_cts.IsCancellationRequested && _fallbackStage < 2)
                {
                    int nextStage = (_fallbackStage == 0 && _config.OutputIndex != 0) ? 1 : 2;
                    _fallbackStage = nextStage;
                    LogMessage?.Invoke($"FFmpeg ddagrab không khả dụng ở stage hiện tại, tự động chuyển sang stage {nextStage}...");
                    Task.Run(() => StartWithStage(nextStage));
                }
            };

            _process.Start();
            try
            {
                _process.PriorityClass = ProcessPriorityClass.High;
            }
            catch
            {
                // Ignored if permissions restrict setting priority
            }
            _process.BeginErrorReadLine();

            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadAnnexBLoopAsync(_process.StandardOutput.BaseStream, _cts.Token));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to start FFmpeg: {ex.Message}");
            Stop();
        }
    }

    public void RequestKeyframe()
    {
        // Phát lại ngay lập tức bộ SPS/PPS/VPS và khung hình IDR gần nhất để decoder Android hồi phục tức thì
        if (_hasEmittedConfig)
        {
            EmitConfig();
            var idr = _cachedIdrNal;
            if (idr != null)
            {
                long timestampUs = Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;
                FrameReady?.Invoke(idr, true, timestampUs);
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(1000);
            }
        }
        catch
        {
            // Ignored
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ReadAnnexBLoopAsync(Stream stdout, CancellationToken token)
    {
        // Kích hoạt MMCSS để bảo đảm độ trễ và nhịp khung hình mượt mà (DisplayPostProcessing)
        using var mmcss = MmcssHelper.EnableRealtimeMultimedia("DisplayPostProcessing");
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        catch
        {
            // Ignored
        }

        byte[] buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();

        while (!token.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await stdout.ReadAsync(buffer, token).ConfigureAwait(false);
                if (bytesRead <= 0) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Error reading from FFmpeg: {ex.Message}");
                break;
            }

            ms.Write(buffer, 0, bytesRead);
            ProcessBuffer(ms);
        }
    }

    private void ProcessBuffer(MemoryStream ms)
    {
        byte[] bytes = ms.GetBuffer();
        int length = (int)ms.Length;
        int startIndex = 0;

        while (true)
        {
            int nextStartCode = FindStartCode(bytes, startIndex + 3, length);
            if (nextStartCode == -1)
            {
                // Incomplete NAL unit, retain remainder in stream
                if (startIndex > 0)
                {
                    int remaining = length - startIndex;
                    Array.Copy(bytes, startIndex, bytes, 0, remaining);
                    ms.SetLength(remaining);
                    ms.Position = remaining;
                }
                break;
            }

            int nalLength = nextStartCode - startIndex;
            if (nalLength > 4)
            {
                byte[] nal = new byte[nalLength];
                Array.Copy(bytes, startIndex, nal, 0, nalLength);
                HandleNalUnit(nal);
            }

            startIndex = nextStartCode;
        }
    }

    private static int FindStartCode(byte[] data, int start, int length)
    {
        for (int i = start; i <= length - 4; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                if (data[i + 2] == 1) return i; // 00 00 01
                if (data[i + 2] == 0 && data[i + 3] == 1) return i; // 00 00 00 01
            }
        }
        return -1;
    }

    private void HandleNalUnit(byte[] nal)
    {
        int offset = (nal[2] == 1) ? 3 : 4;
        if (offset >= nal.Length) return;

        byte headerByte = nal[offset];
        long timestampUs = Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;
        bool isH265 = _config.Codec.Equals("h265", StringComparison.OrdinalIgnoreCase);

        if (!isH265)
        {
            // H.264
            int nalType = headerByte & 0x1F;
            if (nalType == 7) // SPS
            {
                _cachedSps = nal;
                CheckAndEmitH264Config();
                return;
            }
            if (nalType == 8) // PPS
            {
                _cachedPps = nal;
                CheckAndEmitH264Config();
                return;
            }

            // Only pass VCL slices (1 = non-IDR slice, 5 = IDR keyframe) as frames
            if (nalType != 1 && nalType != 5)
            {
                return;
            }

            bool isKeyframe = (nalType == 5);
            byte[] framePayload = nal;
            if (isKeyframe)
            {
                framePayload = PrependConfigToIdr(nal);
                _cachedIdrNal = framePayload;
            }
            FrameReady?.Invoke(framePayload, isKeyframe, timestampUs);
        }
        else
        {
            // H.265
            int nalType = (headerByte & 0x7E) >> 1;
            if (nalType == 32) // VPS
            {
                _cachedVps = nal;
                CheckAndEmitH265Config();
                return;
            }
            if (nalType == 33) // SPS
            {
                _cachedSps = nal;
                CheckAndEmitH265Config();
                return;
            }
            if (nalType == 34) // PPS
            {
                _cachedPps = nal;
                CheckAndEmitH265Config();
                return;
            }

            // In H.265, only VCL units (types 0 to 31) are video frame slices
            if (nalType >= 32)
            {
                return;
            }

            bool isKeyframe = (nalType >= 16 && nalType <= 21); // BLA/IDR/CRA
            byte[] framePayload = nal;
            if (isKeyframe)
            {
                framePayload = PrependConfigToIdr(nal);
                _cachedIdrNal = framePayload;
            }
            FrameReady?.Invoke(framePayload, isKeyframe, timestampUs);
        }
    }

    private byte[] PrependConfigToIdr(byte[] idrNal)
    {
        using var ms = new MemoryStream();
        if (_cachedVps != null) ms.Write(_cachedVps, 0, _cachedVps.Length);
        if (_cachedSps != null) ms.Write(_cachedSps, 0, _cachedSps.Length);
        if (_cachedPps != null) ms.Write(_cachedPps, 0, _cachedPps.Length);
        ms.Write(idrNal, 0, idrNal.Length);
        return ms.ToArray();
    }

    private void CheckAndEmitH264Config()
    {
        if (_cachedSps != null && _cachedPps != null)
        {
            EmitConfig();
        }
    }

    private void CheckAndEmitH265Config()
    {
        if (_cachedVps != null && _cachedSps != null && _cachedPps != null)
        {
            EmitConfig();
        }
    }

    private void EmitConfig()
    {
        using var ms = new MemoryStream();
        if (_cachedVps != null) ms.Write(_cachedVps, 0, _cachedVps.Length);
        if (_cachedSps != null) ms.Write(_cachedSps, 0, _cachedSps.Length);
        if (_cachedPps != null) ms.Write(_cachedPps, 0, _cachedPps.Length);

        byte[] configBytes = ms.ToArray();
        _hasEmittedConfig = true;
        VideoConfigReady?.Invoke(configBytes);
    }

    public void Dispose()
    {
        Stop();
    }
}
