using System.Drawing.Imaging;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>
/// 把本地文件夹映射为标准设备会话（本地相册页用）：枚举/下载/缩略图走文件系统，
/// 与 MTP 会话同接口，因此 AlbumScanner / PhoneSaveService / ThumbnailService 全部直接复用。
/// </summary>
public sealed class LocalFolderSession : IMediaDeviceSession
{
    private readonly string _root;

    public LocalFolderSession(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public bool IsConnected => true;

    public Task<IReadOnlyList<MediaEntry>> EnumerateAsync(string path, CancellationToken ct)
    {
        var local = Resolve(path);
        var result = new List<MediaEntry>();
        if (!Directory.Exists(local))
        {
            return Task.FromResult<IReadOnlyList<MediaEntry>>(result);
        }

        foreach (var fsPath in Directory.EnumerateFileSystemEntries(local))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(fsPath);
            var isDirectory = Directory.Exists(fsPath);
            long size = 0;
            DateTime? lastModified = null;
            if (!isDirectory)
            {
                var info = new FileInfo(fsPath);
                size = info.Length;
                lastModified = info.LastWriteTime;
            }

            result.Add(new MediaEntry(name, ToVirtual(fsPath), isDirectory, size, lastModified));
        }

        return Task.FromResult<IReadOnlyList<MediaEntry>>(result);
    }

    public Task DownloadFileAsync(string sourcePath, string destFilePath, CancellationToken ct)
    {
        var local = Resolve(sourcePath);
        ct.ThrowIfCancellationRequested();
        File.Copy(local, destFilePath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task UploadFileAsync(string sourceFilePath, string destPath, CancellationToken ct)
    {
        var local = Resolve(destPath);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.Copy(sourceFilePath, local, overwrite: true);
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Resolve(path));
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var local = Resolve(path);
        if (File.Exists(local))
        {
            File.Delete(local);
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> GetThumbnailAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var local = Resolve(path);
        var ext = Path.GetExtension(local).ToLowerInvariant();
        if (!File.Exists(local) || ext is not (".jpg" or ".jpeg" or ".png"))
        {
            return Task.FromResult<Stream?>(null);
        }

        using var image = System.Drawing.Image.FromFile(local);
        using var thumb = ImageUtil.Downscale(image, 480);
        var stream = new MemoryStream();
        thumb.Save(stream, ImageFormat.Jpeg);
        stream.Position = 0;
        return Task.FromResult<Stream?>(stream);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private string Resolve(string virtualPath)
    {
        var relative = virtualPath.TrimStart('\\').Replace('/', Path.DirectorySeparatorChar);
        var full = string.IsNullOrEmpty(relative) ? _root : Path.Combine(_root, relative);
        var normalized = Path.GetFullPath(full);
        if (!normalized.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("路径越界");
        }

        return normalized;
    }

    private string ToVirtual(string local)
    {
        var relative = Path.GetRelativePath(_root, local);
        return "\\" + relative.Replace('/', '\\');
    }
}
