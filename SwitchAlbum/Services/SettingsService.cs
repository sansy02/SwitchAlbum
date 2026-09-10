using System.Text.Json;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>设置读写（%LocalAppData%\SwitchAlbum\settings.json），原子写入。</summary>
public sealed class SettingsService
{
    private readonly string _filePath;

    public SettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath));
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}
