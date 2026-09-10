using System.Drawing.Imaging;
using System.IO;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>模拟 MTP 设备：Switch 相册与安卓手机均映射到本地文件夹，结构与真机一致。</summary>
public sealed class MockMediaProvider : IMediaProvider
{
    public const string SwitchDeviceId = "mock-switch";
    public const string PhoneDeviceId = "mock-phone";

    private readonly string _albumPath;
    private readonly string _phonePath;

    public MockMediaProvider(string albumPath, string phonePath)
    {
        _albumPath = albumPath;
        _phonePath = phonePath;

        // 真实安卓手机 MTP 根目录下是存储卷（如 "Internal shared storage"），模拟同样的结构
        Directory.CreateDirectory(Path.Combine(_phonePath, "Internal shared storage"));
    }

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<MediaDeviceInfo>> GetDevicesAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<MediaDeviceInfo>>(new[]
        {
            new MediaDeviceInfo(SwitchDeviceId, "Switch（模拟）", isSwitch: true),
            new MediaDeviceInfo(PhoneDeviceId, "Android 手机（模拟）", isSwitch: false),
        });
    }

    public Task<IMediaDeviceSession> ConnectAsync(MediaDeviceInfo device, CancellationToken ct)
    {
        var root = device.DeviceId == SwitchDeviceId ? _albumPath : _phonePath;
        return Task.FromResult<IMediaDeviceSession>(new MockSession(root));
    }

    private sealed class MockSession : IMediaDeviceSession
    {
        private readonly string _root;

        public MockSession(string root) => _root = Path.GetFullPath(root);

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

        public async Task DownloadFileAsync(string sourcePath, string destFilePath, CancellationToken ct)
        {
            var local = Resolve(sourcePath);
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                File.Copy(local, destFilePath, overwrite: true);
            }, ct).ConfigureAwait(false);

            // 模拟 MTP 传输耗时，让进度可见
            await Task.Delay(60, ct).ConfigureAwait(false);
        }

        public Task UploadFileAsync(string sourceFilePath, string destPath, CancellationToken ct)
        {
            var local = Resolve(destPath);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            File.Copy(sourceFilePath, local, overwrite: true);
            return Task.Delay(60, ct);
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
}
