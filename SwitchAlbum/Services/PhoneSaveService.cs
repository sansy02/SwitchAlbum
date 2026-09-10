using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>
/// 保存到安卓手机：定位存储根 → 按拍摄日期建 DCIM\SwitchAlbum\YYYY-MM-DD →
/// 从 Switch 下载到本地临时文件 → 上传手机 → 校验 → 标记已保存。
/// </summary>
public sealed class PhoneSaveService
{
    private readonly DuplicateTracker _tracker;
    private readonly SettingsService _settings;
    private readonly DownloadQueue _queue = new(maxConcurrent: 2);
    private readonly object _gate = new();

    public PhoneSaveService(DuplicateTracker tracker, SettingsService settings)
    {
        _tracker = tracker;
        _settings = settings;
    }

    public async Task<SaveResult> SaveToPhoneAsync(
        IReadOnlyList<AlbumItem> items,
        IMediaDeviceSession switchSession,
        IMediaDeviceSession phoneSession,
        bool autoRename,
        IProgress<SaveProgress>? progress,
        CancellationToken ct)
    {
        string storageRoot;
        try
        {
            storageRoot = await LocateStorageRootAsync(phoneSession, ct).ConfigureAwait(false);
        }
        catch
        {
            return new SaveResult
            {
                Failed = items.Select(i => (i, "无法访问手机存储")).ToList(),
            };
        }

        var pending = items.Where(i => !HasSavedToPhone(i)).ToList();
        var skipped = items.Count - pending.Count;
        var failures = new List<(AlbumItem Item, string Error)>();
        var done = 0;
        var saved = 0;

        progress?.Report(new SaveProgress(items.Count, done, skipped, null));

        foreach (var group in pending.GroupBy(DateKeyOf).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var targetDir = storageRoot + "\\DCIM\\SwitchAlbum\\" + group.Key;
            try
            {
                await EnsureDirectoryAsync(phoneSession, storageRoot + "\\DCIM", ct).ConfigureAwait(false);
                await EnsureDirectoryAsync(phoneSession, storageRoot + "\\DCIM\\SwitchAlbum", ct).ConfigureAwait(false);
                await EnsureDirectoryAsync(phoneSession, targetDir, ct).ConfigureAwait(false);
            }
            catch
            {
                lock (_gate)
                {
                    failures.AddRange(group.Select(i => (i, "无法在手机创建目录")));
                    done += group.Count();
                }

                continue;
            }

            IReadOnlyList<MediaEntry> entries;
            try
            {
                entries = await phoneSession.EnumerateAsync(targetDir, ct).ConfigureAwait(false);
            }
            catch
            {
                entries = Array.Empty<MediaEntry>();
            }

            var existingNames = entries.Where(e => !e.IsDirectory).Select(e => e.Name);
            var plan = RenamePlanner.Plan(group.ToList(), existingNames, autoRename);

            var tasks = group.Select(item => _queue.EnqueueAsync(async _ =>
            {
                ct.ThrowIfCancellationRequested();
                var tmp = Path.Combine(Path.GetTempPath(), "sab-upload-" + Guid.NewGuid().ToString("N") + Path.GetExtension(item.FileName));
                try
                {
                    await switchSession.DownloadFileAsync(item.DevicePath, tmp, ct).ConfigureAwait(false);
                    await phoneSession.UploadFileAsync(tmp, targetDir + "\\" + plan[item], ct).ConfigureAwait(false);
                    _tracker.MarkSaved(item.GameTitle, item.FileName, plan[item], toPc: false, toPhone: true);

                    lock (_gate)
                    {
                        done++;
                        saved++;
                    }

                    progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    lock (_gate)
                    {
                        failures.Add((item, e.Message));
                        done++;
                    }

                    progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName));
                }
                finally
                {
                    TryDelete(tmp);
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

            if (cancelled)
            {
                _tracker.Flush();
                return new SaveResult
                {
                    SavedCount = saved,
                    SkippedCount = skipped,
                    Cancelled = true,
                    Failed = failures,
                };
            }
        }

        _tracker.Flush();
        return new SaveResult
        {
            SavedCount = saved,
            SkippedCount = skipped,
            Failed = failures,
        };
    }

    private static async Task<string> LocateStorageRootAsync(IMediaDeviceSession phoneSession, CancellationToken ct)
    {
        var rootEntries = await phoneSession.EnumerateAsync("\\", ct).ConfigureAwait(false);
        var dirs = rootEntries.Where(e => e.IsDirectory).ToList();
        var storage = dirs.FirstOrDefault(d =>
                          d.Name.Contains("storage", StringComparison.OrdinalIgnoreCase)
                          || d.Name.Contains("sd", StringComparison.OrdinalIgnoreCase))
                      ?? dirs.FirstOrDefault();
        return storage?.FullPath ?? "\\";
    }

    private static async Task EnsureDirectoryAsync(IMediaDeviceSession session, string path, CancellationToken ct)
    {
        try
        {
            await session.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
            return;
        }
        catch
        {
            // 部分 MTP 实现重复创建会报错，改为枚举父目录确认存在
        }

        var parent = path[..path.LastIndexOf('\\')];
        var name = path[(path.LastIndexOf('\\') + 1)..];
        IReadOnlyList<MediaEntry> entries;
        try
        {
            entries = await session.EnumerateAsync(parent, ct).ConfigureAwait(false);
        }
        catch
        {
            entries = Array.Empty<MediaEntry>();
        }

        if (entries.All(e => !e.IsDirectory || !e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("无法创建目录 " + path);
        }
    }

    private bool HasSavedToPhone(AlbumItem item)
    {
        var state = _tracker.GetState(item.GameTitle, item.FileName);
        return state is SavedState.SavedToPhone or SavedState.SavedBoth;
    }

    private static string DateKeyOf(AlbumItem item)
        => item.Timestamp == DateTime.MinValue ? "未知日期" : item.Timestamp.ToString("yyyy-MM-dd");

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
