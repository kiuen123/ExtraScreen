using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ExtraScreen.Usb;

public class AdbClient
{
    private readonly string _adbPath;

    public AdbClient(string? customAdbPath = null)
    {
        _adbPath = LocateAdb(customAdbPath);
    }

    public string AdbPath => _adbPath;

    public bool IsAvailable => File.Exists(_adbPath);

    public static string LocateAdb(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return customPath;
        }

        // 1. Look in application base directory / tools / adb
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string bundledAdb = Path.Combine(baseDir, "tools", "adb", "adb.exe");
        if (File.Exists(bundledAdb)) return bundledAdb;

        // Relative path from development root
        string projectRelative = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "tools", "adb", "adb.exe"));
        if (File.Exists(projectRelative)) return projectRelative;

        // 2. Look in Android SDK platform-tools
        string? androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME") 
                              ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            string sdkAdb = Path.Combine(androidHome, "platform-tools", "adb.exe");
            if (File.Exists(sdkAdb)) return sdkAdb;
        }

        string defaultSdkAdb = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android", "Sdk", "platform-tools", "adb.exe");
        if (File.Exists(defaultSdkAdb)) return defaultSdkAdb;

        // 3. Fallback to "adb" in PATH
        return "adb";
    }

    public async Task<List<AdbDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunCommandAsync("devices -l", cancellationToken).ConfigureAwait(false);
        var devices = new List<AdbDeviceInfo>();

        var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("List of devices") || string.IsNullOrWhiteSpace(line))
                continue;

            // Format: <serial> <state> product:<p> model:<m> device:<d>
            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 2) continue;

            var device = new AdbDeviceInfo
            {
                Serial = parts[0],
                State = parts[1].ToLowerInvariant() switch
                {
                    "device" => DeviceState.Device,
                    "unauthorized" => DeviceState.Unauthorized,
                    "offline" => DeviceState.Offline,
                    _ => DeviceState.Unknown
                }
            };

            for (int i = 2; i < parts.Length; i++)
            {
                var kv = parts[i].Split(':');
                if (kv.Length == 2)
                {
                    if (kv[0].Equals("model", StringComparison.OrdinalIgnoreCase))
                        device.Model = kv[1];
                    else if (kv[0].Equals("product", StringComparison.OrdinalIgnoreCase))
                        device.Product = kv[1];
                    else if (kv[0].Equals("device", StringComparison.OrdinalIgnoreCase))
                        device.Device = kv[1];
                }
            }

            devices.Add(device);
        }

        return devices;
    }

    public async Task<bool> ReversePortAsync(string serial, int remotePort = 7070, int localPort = 7070, CancellationToken cancellationToken = default)
    {
        var result = await RunCommandAsync($"-s {serial} reverse tcp:{remotePort} tcp:{localPort}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public async Task<bool> RemoveReverseAsync(string serial, int remotePort = 7070, CancellationToken cancellationToken = default)
    {
        var result = await RunCommandAsync($"-s {serial} reverse --remove tcp:{remotePort}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public async Task<bool> IsAppInstalledAsync(string serial, string packageName = "com.extrascreen", CancellationToken cancellationToken = default)
    {
        var result = await RunCommandAsync($"-s {serial} shell pm list packages {packageName}", cancellationToken).ConfigureAwait(false);
        return result.Output.Contains(packageName);
    }

    public async Task<bool> InstallAppAsync(string serial, string apkPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(apkPath)) return false;
        var result = await RunCommandAsync($"-s {serial} install -r \"{apkPath}\"", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && result.Output.Contains("Success");
    }

    public async Task<bool> StartAppAsync(string serial, string component = "com.extrascreen/.stream.StreamActivity", CancellationToken cancellationToken = default)
    {
        var result = await RunCommandAsync($"-s {serial} shell am start -n {component}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
}
