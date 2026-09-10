using System.Text;

namespace SwitchAlbum.Services.Parsing;

/// <summary>
/// 清理用于文件名的文本（不含扩展名）：剔除 Windows 非法字符与控制字符，
/// 保留全角字符（如 ×），处理保留名（CON 等），限制长度。
/// </summary>
public static class FileNameSanitizer
{
    private static readonly HashSet<char> InvalidChars = new(Path.GetInvalidFileNameChars());

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Sanitize(string? name, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "未知";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(InvalidChars.Contains(c) || char.IsControl(c) ? ' ' : c);
        }

        var result = sb.ToString().Trim().TrimEnd(' ', '.');
        if (result.Length == 0)
        {
            return "未知";
        }

        if (result.Length > maxLength)
        {
            result = result[..maxLength].TrimEnd(' ', '.');
        }

        // 名称含点号时（如 "Mario vs. Donkey Kong"），检查首个点号前是否为保留名
        var firstPart = result.Split('.')[0];
        if (ReservedNames.Contains(firstPart))
        {
            result = "_" + result;
        }

        return result;
    }
}
