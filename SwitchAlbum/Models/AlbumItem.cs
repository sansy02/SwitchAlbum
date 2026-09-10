namespace SwitchAlbum.Models;

/// <summary>相册中的一张照片或一个视频。</summary>
public sealed class AlbumItem
{
    public required string GameTitle { get; init; }

    /// <summary>设备上的完整文件路径（下载源）。</summary>
    public required string DevicePath { get; init; }

    public required string FileName { get; init; }

    /// <summary>从文件名解析出的拍摄时间；解析失败为 DateTime.MinValue。</summary>
    public DateTime Timestamp { get; init; }

    public bool IsVideo { get; init; }

    public long Size { get; init; }

    public DateTime? LastModified { get; init; }

    public SavedState Saved { get; set; }

    /// <summary>重复检测标识。</summary>
    public string DedupId => GameTitle + "|" + FileName;
}
