using System.Security.Cryptography;
using System.Drawing.Imaging;

namespace SwitchAlbum.Services;

/// <summary>
/// 缩略图服务：优先取设备缩略图，失败则下载整图降采样；
/// 结果按 设备id+路径+mtime+size 缓存到本地，同一次会话内重复请求不重复下载。
/// </summary>
public sealed class ThumbnailService
{
    private readonly string _cacheDir;
    private readonly object _gate = new();
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public ThumbnailService(string? cacheDir = null)
    {
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "thumbs");
        Directory.CreateDirectory(_cacheDir);
    }

    public string? GetCachedPath(string deviceId, string path, DateTime? lastModified, long size)
    {
        var cachePath = CachePathOf(ComputeKey(deviceId, path, lastModified, size));
        return File.Exists(cachePath) ? cachePath : null;
    }

    public async Task<string?> GetThumbnailAsync(
        string deviceId, IMediaDeviceSession session, string devicePath, string fileName,
        DateTime? lastModified, long size, CancellationToken ct)
    {
        var key = ComputeKey(deviceId, devicePath, lastModified, size);
        var cachePath = CachePathOf(key);
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        lock (_gate)
        {
            if (!_inFlight.Add(key))
            {
                return cachePath; // 已有同 key 请求在途，返回占位（图片稍后由在途请求填好缓存）
            }
        }

        try
        {
            // 优先设备缩略图
            try
            {
                var stream = await session.GetThumbnailAsync(devicePath, ct).ConfigureAwait(false);
                if (stream != null)
                {
                    await using (stream)
                    {
                        await using var fs = File.Create(cachePath);
                        await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
                    }

                    if (IsValidImage(cachePath))
                    {
                        return cachePath;
                    }

                    TryDelete(cachePath);
                }
            }
            catch
            {
                // 降级到整图下载
            }

            var tmp = Path.Combine(Path.GetTempPath(), "sab-thumb-" + Guid.NewGuid().ToString("N") + Path.GetExtension(fileName));
            try
            {
                await session.DownloadFileAsync(devicePath, tmp, ct).ConfigureAwait(false);
                var ok = await Task.Run(() => TryDownscale(tmp, cachePath), ct).ConfigureAwait(false);
                return ok ? cachePath : null;
            }
            finally
            {
                TryDelete(tmp);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(key);
            }
        }
    }

    /// <summary>获取原图（供大图预览），按同样 key 缓存。</summary>
    public async Task<string?> GetFullImageAsync(
        string deviceId, IMediaDeviceSession session, string devicePath, string fileName,
        DateTime? lastModified, long size, CancellationToken ct)
    {
        var key = ComputeKey(deviceId, devicePath, lastModified, size);
        var cachePath = Path.Combine(_cacheDir, "full-" + key + Path.GetExtension(fileName));
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        var tmp = cachePath + ".part";
        try
        {
            await session.DownloadFileAsync(devicePath, tmp, ct).ConfigureAwait(false);
            File.Move(tmp, cachePath, overwrite: true);
            return cachePath;
        }
        catch
        {
            TryDelete(tmp);
            return null;
        }
    }

    private string CachePathOf(string key) => Path.Combine(_cacheDir, key + ".jpg");

    private static string ComputeKey(string deviceId, string path, DateTime? lastModified, long size)
    {
        var raw = $"{deviceId}|{path}|{lastModified?.Ticks ?? 0}|{size}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsValidImage(string path)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(path);
            return image.Width > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDownscale(string source, string dest)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(source);
            using var thumb = ImageUtil.Downscale(image, 480);
            thumb.Save(dest, ImageFormat.Jpeg);
            return true;
        }
        catch
        {
            return false;
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
