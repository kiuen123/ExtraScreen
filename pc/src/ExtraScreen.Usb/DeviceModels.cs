namespace ExtraScreen.Usb;

public enum DeviceState
{
    Unknown,
    Device,
    Unauthorized,
    Offline
}

public class AdbDeviceInfo
{
    public string Serial { get; set; } = string.Empty;
    public DeviceState State { get; set; } = DeviceState.Unknown;
    public string Model { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;

    public string DisplayName => !string.IsNullOrWhiteSpace(Model) 
        ? Model.Replace('_', ' ') 
        : (!string.IsNullOrWhiteSpace(Serial) ? Serial : "Thiết bị Android");
}
