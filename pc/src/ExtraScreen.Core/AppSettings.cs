using System.IO;
using System.Text.Json;
using ExtraScreen.Display;
using ExtraScreen.Encoding;

namespace ExtraScreen.Core;

public class AppSettings
{
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Extend;
    public string Codec { get; set; } = "h264"; // "h264" or "h265"
    public int BitrateBps { get; set; } = 20_000_000; // 20 Mbps default
    public int ScalingPercentage { get; set; } = 100; // 100% or 125%
    public bool AutoOpenAppOnPlug { get; set; } = true;
    public bool AutoVirtualDisplay { get; set; } = true;
    public string SelectedEncoderType { get; set; } = "Nvenc";

    public static string GetSettingsPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "ExtraScreen");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        return Path.Combine(folder, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Fallback to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string path = GetSettingsPath();
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore write errors
        }
    }
}
