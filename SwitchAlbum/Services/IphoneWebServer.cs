using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Web;

namespace SwitchAlbum.Services;

/// <summary>
/// iPhone 零安装网页方案：在局域网内托管保存目录的网页相册。
/// iPhone 扫码后在 Safari 浏览照片（长按单张存储）、下载分组/全部 ZIP（文件 App 解压后批量存入相册）。
/// 基于 TcpListener 的最小 HTTP 服务器（仅 GET），避免 HttpListener 的 URLACL 管理员权限要求。
/// 每次启动生成随机 token，所有路径必须携带 ?t=token 才响应。
/// </summary>
public sealed class IphoneWebServer : IDisposable
{
    private static readonly HashSet<string> MediaExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".mp4", ".mov" };

    private readonly string _rootDir;
    private readonly string _token;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public IphoneWebServer(string rootDir, string? ip = null, int startPort = 53771)
    {
        _rootDir = Path.GetFullPath(rootDir);
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        ip ??= LocalIpAddresses().FirstOrDefault() ?? "127.0.0.1";

        TcpListener? listener = null;
        for (var port = startPort; port < startPort + 50; port++)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Port = port;
                break;
            }
            catch
            {
                // 端口被占用，试下一个
            }
        }

        if (listener == null)
        {
            throw new IOException($"{startPort}-{startPort + 49} 端口均被占用，无法启动局域网服务");
        }

        _listener = listener;
        Url = $"http://{ip}:{Port}/?t={_token}";
    }

    public int Port { get; }
    public string Url { get; }
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // 停止失败忽略
        }

        IsRunning = false;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            stream.ReadTimeout = 10000;
            stream.WriteTimeout = 30000;
            try
            {
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request == null)
                {
                    return;
                }

                var (path, query) = request.Value;
                var queryParams = ParseQuery(query);

                if (!queryParams.TryGetValue("t", out var token)
                    || !token.Equals(_token, StringComparison.Ordinal))
                {
                    await WriteResponseAsync(stream, 403, "text/plain; charset=utf-8", "拒绝访问"u8.ToArray()).ConfigureAwait(false);
                    return;
                }

                switch (path)
                {
                    case "/":
                        await ServeHomeAsync(stream).ConfigureAwait(false);
                        break;
                    case "/zip":
                        await ServeZipAsync(stream, queryParams).ConfigureAwait(false);
                        break;
                    case "/img":
                        await ServeFileAsync(stream, queryParams).ConfigureAwait(false);
                        break;
                    default:
                        await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found"u8.ToArray()).ConfigureAwait(false);
                        break;
                }
            }
            catch
            {
                // 客户端断开或写入失败：忽略
            }
        }
    }

    private async Task ServeHomeAsync(NetworkStream stream)
    {
        var html = LoadEmbeddedPage();
        var sections = new StringBuilder();
        var total = 0;

        var gameDirs = Directory.Exists(_rootDir)
            ? Directory.GetDirectories(_rootDir).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList()
            : new List<string>();

        foreach (var dir in gameDirs)
        {
            var name = Path.GetFileName(dir);
            var files = MediaFilesIn(dir);
            if (files.Count == 0)
            {
                continue;
            }

            total += files.Count;
            sections.Append("<section><h2>").Append(HttpUtility.HtmlEncode(name))
                .Append(" <span>").Append(files.Count).Append(" 个</span>")
                .Append("<a class=\"zip\" href=\"/zip?game=").Append(Uri.EscapeDataString(name))
                .Append("&t=").Append(_token).Append("\">下载本组 ZIP</a></h2><div class=\"grid\">");

            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(_rootDir, file).Replace('\\', '/');
                var imgUrl = "/img?path=" + Uri.EscapeDataString(rel) + "&t=" + _token;
                if (IsImage(file))
                {
                    sections.Append("<a class=\"pic\" href=\"").Append(imgUrl).Append("\"><img loading=\"lazy\" src=\"")
                        .Append(imgUrl).Append("\"></a>");
                }
                else
                {
                    sections.Append("<a class=\"vid\" href=\"").Append(imgUrl).Append("\">▶ ")
                        .Append(HttpUtility.HtmlEncode(Path.GetFileName(file))).Append("</a>");
                }
            }

            sections.Append("</div></section>");
        }

        // 根目录散装媒体文件
        var loose = Directory.Exists(_rootDir)
            ? Directory.GetFiles(_rootDir).Where(IsMedia).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList()
            : new List<string>();
        if (loose.Count > 0)
        {
            total += loose.Count;
            sections.Append("<section><h2>其他 <span>").Append(loose.Count).Append(" 个</span></h2><div class=\"grid\">");
            foreach (var file in loose)
            {
                var rel = Path.GetFileName(file);
                var imgUrl = "/img?path=" + Uri.EscapeDataString(rel) + "&t=" + _token;
                sections.Append("<a class=\"pic\" href=\"").Append(imgUrl).Append("\"><img loading=\"lazy\" src=\"")
                    .Append(imgUrl).Append("\"></a>");
            }

            sections.Append("</div></section>");
        }

        html = html.Replace("__TOKEN__", _token)
            .Replace("__TOTAL__", total.ToString())
            .Replace("__SECTIONS__", sections.ToString());

        var bytes = Encoding.UTF8.GetBytes(html);
        await WriteResponseAsync(stream, 200, "text/html; charset=utf-8", bytes).ConfigureAwait(false);
    }

    private async Task ServeZipAsync(NetworkStream stream, IReadOnlyDictionary<string, string> query)
    {
        var game = query.TryGetValue("game", out var g) ? g : "";
        var files = new List<string>();
        string zipName;

        if (game == "__all__")
        {
            files = MediaFilesIn(_rootDir);
            zipName = "SwitchAlbum_all.zip";
        }
        else
        {
            var safeGame = Path.GetFileName(game);
            var dir = Path.Combine(_rootDir, safeGame);
            if (string.IsNullOrEmpty(safeGame) || !Directory.Exists(dir))
            {
                await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found"u8.ToArray()).ConfigureAwait(false);
                return;
            }

            files = MediaFilesIn(dir);
            zipName = safeGame + ".zip";
        }

        if (files.Count == 0)
        {
            await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "无文件"u8.ToArray()).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(stream, 200, "application/zip", Array.Empty<byte>(), new Dictionary<string, string>
        {
            ["Content-Disposition"] = $"attachment; filename=\"{Uri.EscapeDataString(zipName)}\"",
        }, omitContentLength: true).ConfigureAwait(false);

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entryName = Path.GetRelativePath(_rootDir, file).Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var fs = File.OpenRead(file);
                await fs.CopyToAsync(entryStream).ConfigureAwait(false);
            }
        }
    }

    private async Task ServeFileAsync(NetworkStream stream, IReadOnlyDictionary<string, string> query)
    {
        if (!query.TryGetValue("path", out var rel) || string.IsNullOrWhiteSpace(rel))
        {
            await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", "Bad Request"u8.ToArray()).ConfigureAwait(false);
            return;
        }

        var full = Path.GetFullPath(Path.Combine(_rootDir, rel.Replace('/', Path.DirectorySeparatorChar)));
        var insideRoot = full.StartsWith(_rootDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!insideRoot || !File.Exists(full))
        {
            await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found"u8.ToArray()).ConfigureAwait(false);
            return;
        }

        var contentType = ContentTypeOf(full);
        var length = new FileInfo(full).Length;
        await WriteResponseAsync(stream, 200, contentType, Array.Empty<byte>(), new Dictionary<string, string>
        {
            ["Content-Length"] = length.ToString(),
            ["Cache-Control"] = "no-store",
        }).ConfigureAwait(false);

        await using var fs = File.OpenRead(full);
        await fs.CopyToAsync(stream).ConfigureAwait(false);
    }

    private static List<string> MediaFilesIn(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return new List<string>();
        }

        return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(IsMedia)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsMedia(string path) => MediaExtensions.Contains(Path.GetExtension(path));

    private static bool IsImage(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        _ => "application/octet-stream",
    };

    private static string LoadEmbeddedPage()
    {
        var assembly = typeof(IphoneWebServer).Assembly;
        using var stream = assembly.GetManifestResourceStream("SwitchAlbum.Resources.iphone-page.html");
        if (stream == null)
        {
            return "<html><body>页面资源缺失</body></html>";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<(string Path, string Query)?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var received = 0;
        var headerEnd = -1;
        while (received < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(received, buffer.Length - received)).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            received += read;
            var text = Encoding.ASCII.GetString(buffer, 0, received);
            headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                break;
            }
        }

        if (headerEnd < 0)
        {
            return null;
        }

        var header = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = header.Split("\r\n");
        if (lines.Length == 0)
        {
            return null;
        }

        var parts = lines[0].Split(' ');
        if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pathAndQuery = parts[1];
        var qIndex = pathAndQuery.IndexOf('?');
        var path = qIndex >= 0 ? pathAndQuery[..qIndex] : pathAndQuery;
        var query = qIndex >= 0 ? pathAndQuery[(qIndex + 1)..] : "";
        return (path, query);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[pair] = "";
            }
            else
            {
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        return result;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream, int status, string contentType, byte[] body,
        IReadOnlyDictionary<string, string>? headers = null, bool omitContentLength = false)
    {
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            _ => "OK",
        };

        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Connection: close\r\n");

        // 自定义头里已有 Content-Length（如 /img 的真实文件长度）时不重复写；
        // 流式响应（ZIP）省略 Content-Length，由 Connection: close 界定实体。
        if (!omitContentLength && (headers == null || !headers.ContainsKey("Content-Length")))
        {
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        }

        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                sb.Append(key).Append(": ").Append(value).Append("\r\n");
            }
        }

        sb.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body).ConfigureAwait(false);
        }
    }

    /// <summary>枚举活动 IPv4 地址，优先常见私有网段（192.168.* / 10.* / 172.16-31.*）。</summary>
    private static IEnumerable<string> LocalIpAddresses()
    {
        var addresses = new List<(string Ip, int Priority)>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (ni.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
            {
                continue;
            }

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address))
                {
                    continue;
                }

                var ip = ua.Address.ToString();
                var priority = ip.StartsWith("192.168.", StringComparison.Ordinal) ? 0
                    : ip.StartsWith("10.", StringComparison.Ordinal) ? 1
                    : ip.StartsWith("172.", StringComparison.Ordinal) && IsPrivate172(ua.Address) ? 1
                    : 2;
                addresses.Add((ip, priority));
            }
        }

        return addresses.OrderBy(a => a.Priority).ThenBy(a => a.Ip, StringComparer.Ordinal).Select(a => a.Ip);
    }

    private static bool IsPrivate172(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }
}
