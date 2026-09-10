namespace SwitchAlbum.Models;

public sealed class AppSettings
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwitchAlbum");

    /// <summary>保存到电脑的目录，默认 桌面\SwitchAlbum。</summary>
    public string SavePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SwitchAlbum");

    /// <summary>保存到手机的目标设备（MTP 设备 id）。</summary>
    public string? PhoneDeviceId { get; set; }
    public string? PhoneFriendlyName { get; set; }

    /// <summary>保存时按 日期_游戏名_序号 重命名。</summary>
    public bool AutoRename { get; set; } = true;

    /// <summary>System / Light / Dark。</summary>
    public string Theme { get; set; } = "System";

    /// <summary>SteamGridDB API 密钥（可选）。</summary>
    public string? SteamGridDbApiKey { get; set; }

    // 模拟模式（开发 / 无硬件预览）
    public bool MockModeEnabled { get; set; }
    public string MockAlbumPath { get; set; } = Path.Combine(AppDataDir, "MockAlbum");
    public string MockPhonePath { get; set; } = Path.Combine(AppDataDir, "MockPhone");

    // 窗口状态
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }
}
