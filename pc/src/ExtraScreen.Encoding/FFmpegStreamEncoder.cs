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
    private bool _hasEmittedConfig;

    public FFmpegStreamEncoder(VideoEncoderConfig config, string? ffmpegPath = null)
    {
        _config = config;
        _ffmpegPath = ffmpegPath ?? EncoderDetector.LocateFFmpeg();
    }

    public bool IsRunning => _process != null && !_process.HasExited;

    public void Start()
    {
        if (IsRunning) return;

        _hasEmittedConfig = false;
        _cachedSps = null;
        _cachedPps = null;
        _cachedVps = null;

        string codecParam = _config.Codec.Equals("h265", StringComparison.OrdinalIgnoreCase) 
            ? _config.EncoderOption.HevcCodec 
            : _config.EncoderOption.H264Codec;

        string format = _config.Codec.Equals("h265", StringComparison.OrdinalIgnoreCase) ? "hevc" : "h264";
        int bufsize = Math.Max(200_000, _config.BitrateBps / 40);

        string encoderArgs;
        if (codecParam.Contains("nvenc"))
        {
            encoderArgs = $"-c:v {codecParam} -preset p1 -tune ull -zerolatency 1 -bf 0 -g 120 -rc cbr -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize}";
        }
        else if (codecParam.Contains("qsv"))
        {
            encoderArgs = $"-c:v {codecParam} -preset veryfast -bf 0 -g 120 -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize}";
        }
        else if (codecParam.Contains("amf"))
        {
            encoderArgs = $"-c:v {codecParam} -usage lowlatency -quality speed -bf 0 -g 120 -rc cbr -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize}";
        }
        else
        {
            // CPU x264 fallback
            encoderArgs = $"-c:v {codecParam} -preset ultrafast -tune zerolatency -bf 0 -g 120 -b:v {_config.BitrateBps} -maxrate {_config.BitrateBps} -bufsize {bufsize}";
        }

        string inputFilter = $"ddagrab=output_idx={_config.OutputIndex}:framerate={_config.Fps}:video_size={_config.Width}x{_config.Height}:dup_frames=1:draw_mouse=1";
        string arguments = $"-hide_banner -loglevel warning -f lavfi -i {inputFilter} {encoderArgs} -f {format} -";

        LogMessage?.Invoke($"Starting FFmpeg with arguments: {arguments}");

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
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    LogMessage?.Invoke($"FFmpeg: {e.Data}");
                }
            };

            _process.Start();
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
        // For real-time pipeline, if client requests keyframe and we have cached config,
        // re-emit video config and inform pipeline
        if (_hasEmittedConfig)
        {
            EmitConfig();
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

            bool isKeyframe = (nalType == 5);
            FrameReady?.Invoke(nal, isKeyframe, timestampUs);
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

            bool isKeyframe = (nalType >= 16 && nalType <= 21); // BLA/IDR/CRA
            FrameReady?.Invoke(nal, isKeyframe, timestampUs);
        }
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
