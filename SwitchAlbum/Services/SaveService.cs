using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

public sealed record SaveProgress(int Total, int Done, int Skipped, string? CurrentFileName);

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

        var existingNames = SafeEnumerateFileNames(targetDir);
        var plan = RenamePlanner.Plan(pending, existingNames, autoRename);

        var failures = new List<(AlbumItem Item, string Error)>();
        var done = 0;
        var saved = 0;

        progress?.Report(new SaveProgress(items.Count, done, skipped, null));

        var tasks = pending.Select(item => _queue.EnqueueAsync(async _ =>
        {
            ct.ThrowIfCancellationRequested();

            var finalPath = Path.Combine(targetDir, plan[item]);
            var tmpPath = finalPath + ".part";
            try
            {
                await session.DownloadFileAsync(item.DevicePath, tmpPath, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                File.Move(tmpPath, finalPath, overwrite: true);
                _tracker.MarkSaved(item.GameTitle, item.FileName, Path.GetFileName(finalPath), toPc: true, toPhone: false);

                lock (_gate)
                {
                    done++;
                    saved++;
                }

                progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName));
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

                progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName));
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
