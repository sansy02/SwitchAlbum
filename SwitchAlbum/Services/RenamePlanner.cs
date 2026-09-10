using SwitchAlbum.Models;
using SwitchAlbum.Services.Parsing;

namespace SwitchAlbum.Services;

/// <summary>
/// 重命名规划（纯逻辑，不落盘）：为一批待保存项目分配互不冲突的目标文件名。
/// 自动重命名时格式为 日期_游戏名_序号.扩展名；否则保留原名，冲突时追加 _N。
/// </summary>
public static class RenamePlanner
{
    public static IReadOnlyDictionary<AlbumItem, string> Plan(
        IReadOnlyList<AlbumItem> items,
        IEnumerable<string>? existingNames,
        bool autoRename)
    {
        // Windows 文件名不区分大小写
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingNames != null)
        {
            foreach (var name in existingNames)
            {
                used.Add(name);
            }
        }

        var result = new Dictionary<AlbumItem, string>();
        var sequences = new Dictionary<(string Game, DateTime Date), int>();

        var ordered = items
            .OrderBy(i => i.GameTitle, StringComparer.Ordinal)
            .ThenBy(i => i.Timestamp)
            .ThenBy(i => i.FileName, StringComparer.Ordinal);

        foreach (var item in ordered)
        {
            var ext = Path.GetExtension(item.FileName).ToLowerInvariant();
            if (ext.Length == 0)
            {
                ext = item.IsVideo ? ".mp4" : ".jpg";
            }

            string name;
            if (autoRename)
            {
                var key = (item.GameTitle, item.Timestamp.Date);
                sequences.TryGetValue(key, out var seq);
                var date = item.Timestamp == DateTime.MinValue
                    ? "未知日期"
                    : item.Timestamp.ToString("yyyyMMdd");
                var stem = $"{date}_{FileNameSanitizer.Sanitize(item.GameTitle)}";
                do
                {
                    seq++;
                    name = $"{stem}_{seq:000}{ext}";
                }
                while (!used.Add(name));
                sequences[key] = seq;
            }
            else
            {
                var clean = FileNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(item.FileName));
                name = clean + ext;
                if (!used.Add(name))
                {
                    var n = 1;
                    while (!used.Add($"{clean}_{n}{ext}"))
                    {
                        n++;
                    }

                    name = $"{clean}_{n}{ext}";
                }
            }

            result[item] = name;
        }

        return result;
    }
}
