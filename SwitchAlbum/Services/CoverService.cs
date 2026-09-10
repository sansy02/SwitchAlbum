using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwitchAlbum.Services;

/// <summary>
/// 游戏封面服务：本地缓存 → tinfoil.media（免密钥，按 titleId）→
/// SteamGridDB（可选，用户自填 API 密钥）→ 失败返回 null（UI 显示占位）。
/// 每次会话内失败过的 titleId 不再重复请求。
/// </summary>
public sealed class CoverService
{
    private const string TinfoilBase = "https://tinfoil.media/ti/{0}/512/512";
    private const string SteamGridSearch = "https://www.steamgriddb.com/api/v2/search/autocomplete/{0}";
    private const string SteamGridGrids = "https://www.steamgriddb.com/api/v2/grids/game/{0}?dimensions=600x900";

    private readonly string _cacheDir;
    private readonly SettingsService _settings;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _semaphore = new(4);
    private readonly object _failLock = new();
    private readonly HashSet<string> _failedThisSession = new(StringComparer.OrdinalIgnoreCase);

    public CoverService(SettingsService settings, string? cacheDir = null)
    {
        _settings = settings;
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "covers");
        Directory.CreateDirectory(_cacheDir);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchAlbum/1.0");
    }

    public string? GetCachedPath(string key)
    {
        var path = CachePathOf(key);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 候选链：本地缓存 → 任天堂官方图标直链（titles.json 内嵌）→
    /// tinfoil.media（按 titleId）→ SteamGridDB（按名称，需用户密钥）。
    /// 无 titleId 时跳过 tinfoil，按名称尝试 SteamGridDB。
    /// </summary>
    public async Task<string?> ResolveAsync(string? titleId, string title, string? iconUrl, CancellationToken ct)
    {
        var key = !string.IsNullOrEmpty(titleId) ? titleId : NameKey(title);
        var cached = GetCachedPath(key);
        if (cached != null)
        {
            return cached;
        }

        lock (_failLock)
        {
            if (_failedThisSession.Contains(key))
            {
                return null;
            }
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = CachePathOf(key);

            if (!string.IsNullOrEmpty(iconUrl)
                && await TryDownloadAsync(iconUrl, path, ct).ConfigureAwait(false))
            {
                return path;
            }

            if (!string.IsNullOrEmpty(titleId)
                && await TryDownloadAsync(string.Format(TinfoilBase, titleId), path, ct).ConfigureAwait(false))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(_settings.Current.SteamGridDbApiKey)
                && await TrySteamGridAsync(title, path, ct).ConfigureAwait(false))
            {
                return path;
            }

            lock (_failLock)
            {
                _failedThisSession.Add(key);
            }

            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string CachePathOf(string key) => Path.Combine(_cacheDir, key + ".jpg");

    private static string NameKey(string title)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(TitleDbService.Normalize(title)));
        return "name-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private async Task<bool> TryDownloadAsync(string url, string dest, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                await using var fs = File.Create(dest);
                await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            // 校验必须在文件流关闭之后（File.Create 默认独占共享）
            using var image = System.Drawing.Image.FromFile(dest);
            return image.Width > 0;
        }
        catch
        {
            TryDelete(dest);
            return false;
        }
    }

    private async Task<bool> TrySteamGridAsync(string title, string dest, CancellationToken ct)
    {
        try
        {
            var key = _settings.Current.SteamGridDbApiKey!;
            var searchUrl = string.Format(SteamGridSearch, Uri.EscapeDataString(title));

            using var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var searchResponse = await _http.SendAsync(searchRequest, ct).ConfigureAwait(false);
            if (!searchResponse.IsSuccessStatusCode)
            {
                return false;
            }

            var searchJson = await searchResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var searchDoc = JsonDocument.Parse(searchJson);
            var data = searchDoc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0)
            {
                return false;
            }

            var gameId = data[0].GetProperty("id").GetInt32();

            using var gridsRequest = new HttpRequestMessage(HttpMethod.Get, string.Format(SteamGridGrids, gameId));
            gridsRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var gridsResponse = await _http.SendAsync(gridsRequest, ct).ConfigureAwait(false);
            if (!gridsResponse.IsSuccessStatusCode)
            {
                return false;
            }

            var gridsJson = await gridsResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var gridsDoc = JsonDocument.Parse(gridsJson);
            var grids = gridsDoc.RootElement.GetProperty("data");
            if (grids.GetArrayLength() == 0)
            {
                return false;
            }

            var imageUrl = grids[0].GetProperty("url").GetString();
            if (string.IsNullOrEmpty(imageUrl))
            {
                return false;
            }

            return await TryDownloadAsync(imageUrl, dest, ct).ConfigureAwait(false);
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
