using System.Text.RegularExpressions;

namespace SwitchAlbum.Services.Parsing;

public readonly record struct FolderParseResult(string? Title, string? TitleId, DateTime? Timestamp);

/// <summary>
/// 解析相册中游戏文件夹的名字，兼容两种格式：
/// 1. 本地化游戏名（固件 11.0.0 起 USB 复制模式的默认格式），如 "塞尔达传说 王国之泪"；
/// 2. 旧式 "时间戳-16位TitleId"，如 "2024081512304500-0100F2C0115B6000"。
/// </summary>
public static class FolderNameParser
{
    private static readonly Regex LegacyPattern = new(
        @"^(?<ts>\d{14,16})-(?<tid>[0-9A-Fa-f]{16})$", RegexOptions.Compiled);

    public static FolderParseResult Parse(string? folderName)
    {
        var name = folderName?.Trim() ?? "";
        if (name.Length == 0)
        {
            return new FolderParseResult(null, null, null);
        }

        var match = LegacyPattern.Match(name);
        if (match.Success)
        {
            var tid = match.Groups["tid"].Value.ToUpperInvariant();
            DateTime? timestamp = null;
            if (TimestampParser.TryParse(match.Groups["ts"].Value, out var ts))
            {
                timestamp = ts;
            }

            return new FolderParseResult(null, tid, timestamp);
        }

        return new FolderParseResult(name, null, null);
    }
}
