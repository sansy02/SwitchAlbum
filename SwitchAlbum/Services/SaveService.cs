using SwitchAlbum.Models;
using SwitchAlbum.Services.Parsing;

namespace SwitchAlbum.Services;

public sealed record SaveProgress(int Total, int Done, int Skipped, string? CurrentFileName, long BytesDone = 0);

public sealed class SaveResult
{
    public int SavedCount { get; init; }
    public int SkippedCount { get; init; }
    public bool Cancelled { get; init; }
    public IReadOnlyList<(AlbumItem Item, string Error)> Failed { get; init; } = Array.Empty<(AlbumItem, string)>();
}

/// <summary>保存到电脑：去重 → 重命名规划 → 限流下载（.part 临时名）→ 校验 → 标记已保存。</summary>
public sealed class SaveService
{
    private readonly DuplicateTracker _tracker;
    private readonly DownloadQueue _queue;
    private readonly object _gate = new();

    public SaveService(DuplicateTracker tracker, DownloadQueue? queue = null)
    {
        _tracker = tracker;
        _queue = queue ?? new DownloadQueue(maxConcurrent: 2);
    }

    public async Task<SaveResult> SaveToPcAsync(
        IReadOnlyList<AlbumItem> items,
        IMediaDeviceSession session,
        string targetDir,
        bool autoRename,
        IProgress<SaveProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch
        {
            return new SaveResult
            {
                Failed = items.Select(i => (i, "目录不可用")).ToList(),
            };
        }

        var pending = items.Where(i => !HasSavedToPc(i)).ToList();
        var skipped = items.Count - pending.Count;

        // 按游戏分文件夹：保存位置\游戏名\文件，目录名经清理
        var dirOf = new Dictionary<AlbumItem, string>();
        var existingNamesByDir = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in pending.GroupBy(i => i.GameTitle))
        {
            var dir = Path.Combine(targetDir, FileNameSanitizer.Sanitize(group.Key));
            foreach (var item in group)
            {
                dirOf[item] = dir;
            }

            try
            {
                Directory.CreateDirectory(dir);
                existingNamesByDir[dir] = SafeEnumerateFileNames(dir);
            }
            catch
            {
                foreach (var item in group)
                {
                    dirOf.Remove(item);
                }
            }
        }

        var plan = new Dictionary<AlbumItem, string>();
        foreach (var group in pending.GroupBy(i => dirOf.TryGetValue(i, out var dir) ? dir : null))
        {
            if (group.Key == null)
            {
                continue;
            }

            existingNamesByDir.TryGetValue(group.Key, out var existing);
            foreach (var (item, name) in RenamePlanner.Plan(group.ToList(), existing, autoRename))
            {
                plan[item] = name;
            }
        }

        var failures = new List<(AlbumItem Item, string Error)>();
        var done = 0;
        var saved = 0;
        long bytesDone = 0;

        progress?.Report(new SaveProgress(items.Count, done, skipped, null, 0));

        var tasks = pending.Select(item => _queue.EnqueueAsync(async _ =>
        {
            ct.ThrowIfCancellationRequested();

            if (!dirOf.TryGetValue(item, out var dir) || !plan.TryGetValue(item, out var name))
            {
                lock (_gate)
                {
                    failures.Add((item, "目录不可用"));
                    done++;
                }

                progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName, bytesDone));
                return;
            }

            var finalPath = Path.Combine(dir, name);
            var tmpPath = finalPath + ".part";
            try
            {
                await session.DownloadFileAsync(item.DevicePath, tmpPath, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var bytes = new FileInfo(tmpPath).Length;
                File.Move(tmpPath, finalPath, overwrite: true);
                _tracker.MarkSaved(item.GameTitle, item.FileName, Path.GetFileName(finalPath), toPc: true, toPhone: false);

                lock (_gate)
                {
                    done++;
                    saved++;
                    bytesDone += bytes;
                }

                progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName, bytesDone));
            }
            catch (OperationCanceledException)
            {
                TryDelete(tmpPath);
                throw;
            }
            catch (Exception e)
            {
                TryDelete(tmpPath);
                lock (_gate)
                {
                    failures.Add((item, e.Message));
                    done++;
                }

                progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName, bytesDone));
            }
        }, ct)).ToList();

        var cancelled = false;
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        _tracker.Flush();
        return new SaveResult
        {
            SavedCount = saved,
            SkippedCount = skipped,
            Cancelled = cancelled,
            Failed = failures,
        };
    }

    private bool HasSavedToPc(AlbumItem item)
    {
        var state = _tracker.GetState(item.GameTitle, item.FileName);
        return state is SavedState.SavedToPc or SavedState.SavedBoth;
    }

    private static List<string> SafeEnumerateFileNames(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Cast<string>()
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略清理失败
        }
    }
}
