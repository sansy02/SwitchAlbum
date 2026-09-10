namespace SwitchAlbum.Models;

/// <summary>已保存记录（用于重复检测），Id = 游戏名 + "|" + 原文件名。</summary>
public sealed class SavedRecord
{
    public string Id { get; set; } = "";
    public string GameTitle { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? RenamedTo { get; set; }
    public bool SavedToPc { get; set; }
    public bool SavedToPhone { get; set; }
    public DateTime SavedAt { get; set; }
}
