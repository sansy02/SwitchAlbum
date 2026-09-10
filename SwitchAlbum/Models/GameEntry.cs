namespace SwitchAlbum.Models;

/// <summary>相册中的一个游戏（对应 MTP 上的一个文件夹）。</summary>
public sealed class GameEntry
{
    public required string Title { get; init; }

    /// <summary>16 位十六进制 TitleId（仅旧式文件夹名可解析出，或经名称反查得到）。</summary>
    public string? TitleId { get; set; }

    /// <summary>设备上的文件夹路径。</summary>
    public required string DevicePath { get; init; }

    public int PhotoCount { get; set; }
    public int VideoCount { get; set; }
}
