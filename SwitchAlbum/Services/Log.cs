namespace SwitchAlbum.Services;

/// <summary>
/// 简单文件日志（%LocalAppData%\SwitchAlbum\logs\app-日期.log），
/// 用于真机连接问题排查。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _directory;

    public static string DirectoryPath
    {
        get
        {
            if (_directory == null)
            {
                _directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SwitchAlbum", "logs");
            }

            return _directory;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception == null ? message : message + " | " + exception);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(
                    Path.Combine(DirectoryPath, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log"),
                    line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志写入失败不影响运行
        }
    }
}
