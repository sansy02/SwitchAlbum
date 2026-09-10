namespace SwitchAlbum.Models;

/// <summary>MTP 设备上的一个文件或目录项。</summary>
public sealed class MediaEntry
{
    public MediaEntry(string name, string fullPath, bool isDirectory, long size, DateTime? lastModified)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Size = size;
        LastModified = lastModified;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long Size { get; }
    public DateTime? LastModified { get; }
}
