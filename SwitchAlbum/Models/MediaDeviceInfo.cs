namespace SwitchAlbum.Models;

public sealed class MediaDeviceInfo
{
    public MediaDeviceInfo(string deviceId, string friendlyName, bool isSwitch)
    {
        DeviceId = deviceId;
        FriendlyName = friendlyName;
        IsSwitch = isSwitch;
    }

    public string DeviceId { get; }
    public string FriendlyName { get; }
    public bool IsSwitch { get; }

    public override string ToString() => $"{FriendlyName} ({DeviceId})";
}
