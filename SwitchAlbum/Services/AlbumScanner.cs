using SwitchAlbum.Models;
using SwitchAlbum.Services.Parsing;

namespace SwitchAlbum.Services;

public sealed class ScanResult
{
    public required IReadOnlyList<GameEntry> Games { get; init; }
    public required IReadOnlyList<AlbumItem> AllItems { get; init; }

    /// <summary>按游戏文件夹路径（DevicePath）索引的项目。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<AlbumItem>> ItemsByGame { get; init; }
}

/// <summary>枚举相册：根目录 → Album → 游戏文件夹 → 文件列表。只读元数据，不传输字节。</summary>
public static class AlbumScanner
{
    private static readonly HashSet<string> PhotoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov" };

    public static async Task<ScanResult?> ScanAsync(
        IMediaDeviceSession session,
        ITitleDb? titleDb,
        CancellationToken ct)
    {
        IReadOnlyList<MediaEntry> rootEntries;
        try
        {
            rootEntries = await session.EnumerateAsync("\\", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        var albumRoot = "\\";
        var albumDir = rootEntries.FirstOrDefault(
            e => e.IsDirectory && e.Name.Equals("Album", StringComparison.OrdinalIgnoreCase));
        if (albumDir != null)
        {
            albumRoot = albumDir.FullPath;
        }

        IReadOnlyList<MediaEntry> entries;
        try
        {
            entries = await session.EnumerateAsync(albumRoot, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        var games = new List<GameEntry>();
        var allItems = new List<AlbumItem>();
        var itemsByGame = new Dictionary<string, IReadOnlyList<AlbumItem>>(StringComparer.OrdinalIgnoreCase);

        var gameDirs = entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        foreach (var dir in gameDirs)
        {
            ct.ThrowIfCancellationRequested();

            var parsed = FolderNameParser.Parse(dir.Name);
            string title;
            string? titleId = parsed.TitleId;

            if (parsed.Title != null)
            {
                title = parsed.Title;
                titleId = parsed.TitleId ?? titleDb?.GetTitleIdByName(parsed.Title);
            }
            else if (parsed.TitleId != null)
            {
                title = titleDb?.GetNameByTitleId(parsed.TitleId)
                        ?? $"未知游戏 ({parsed.TitleId[..8]}…)";
            }
            else
            {
                title = dir.Name;
            }

            var items = await ScanGameAsync(session, dir.FullPath, title, ct).ConfigureAwait(false);
            if (items.Count == 0)
            {
                continue;
            }

            var game = new GameEntry { Title = title, TitleId = titleId, DevicePath = dir.FullPath };
            game.PhotoCount = items.Count(i => !i.IsVideo);
            game.VideoCount = items.Count(i => i.IsVideo);
            games.Add(game);
            allItems.AddRange(items);
            itemsByGame[dir.FullPath] = items;
        }

        // 直接位于相册根目录的散装文件归入「其他」
        var looseFiles = entries.Where(e => !e.IsDirectory && IsMedia(e.Name)).ToList();
        if (looseFiles.Count > 0)
        {
            const string otherTitle = "其他";
            var items = looseFiles.Select(e => CreateItem(e, otherTitle)).ToList();
            var game = new GameEntry { Title = otherTitle, DevicePath = albumRoot };
            game.PhotoCount = items.Count(i => !i.IsVideo);
            game.VideoCount = items.Count(i => i.IsVideo);
            games.Insert(0, game);
            allItems.InsertRange(0, items);
            itemsByGame[albumRoot] = items;
        }

        return new ScanResult
        {
            Games = games,
            AllItems = allItems,
            ItemsByGame = itemsByGame,
        };
    }

    private static async Task<List<AlbumItem>> ScanGameAsync(
        IMediaDeviceSession session, string gamePath, string title, CancellationToken ct)
    {
        IReadOnlyList<MediaEntry> files;
        try
        {
            files = await session.EnumerateAsync(gamePath, ct).ConfigureAwait(false);
        }
        catch
        {
            return new List<AlbumItem>();
        }

        return files
            .Where(f => !f.IsDirectory && IsMedia(f.Name))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => CreateItem(f, title))
            .ToList();
    }

    private static AlbumItem CreateItem(MediaEntry entry, string title)
    {
        TimestampParser.TryParse(entry.Name, out var timestamp);
        var ext = Path.GetExtension(entry.Name);
        return new AlbumItem
        {
            GameTitle = title,
            DevicePath = entry.FullPath,
            FileName = entry.Name,
            Timestamp = timestamp,
            IsVideo = VideoExtensions.Contains(ext),
            Size = entry.Size,
            LastModified = entry.LastModified,
        };
    }

    private static bool IsMedia(string name)
    {
        var ext = Path.GetExtension(name);
        return PhotoExtensions.Contains(ext) || VideoExtensions.Contains(ext);
    }
}
