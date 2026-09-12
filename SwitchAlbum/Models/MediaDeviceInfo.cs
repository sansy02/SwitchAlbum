namespace SwitchAlbum.Models;

public sealed class MediaDeviceInfo
{
    public MediaDeviceInfo(string deviceId, string friendlyName, bool isSwitch, bool isPhoneCandidate = false)
    {
        DeviceId = deviceId;
        FriendlyName = friendlyName;
        IsSwitch = isSwitch;
        IsPhoneCandidate = isPhoneCandidate;
    }

    public string DeviceId { get; }
    public string FriendlyName { get; }
    public bool IsSwitch { get; }

    /// <summary>是否可作为「保存到手机」的目标（MTP 协议设备，如安卓手机；MSC 硬盘/U 盘不算）。</summary>
    public bool IsPhoneCandidate { get; }

    public override string ToString() => $"{FriendlyName} ({DeviceId})";
}
