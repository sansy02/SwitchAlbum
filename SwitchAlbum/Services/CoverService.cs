using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwitchAlbum.Services;

/// <summary>
/// 游戏封面服务，候选链（每个源失败自动试下一个，单个源内部重试 1 次）：
/// 任天堂官方直链（图标/横幅/盒装，来自 titles.json）→ tinfoil.media（按 titleId）→
/// SteamGridDB（按名称，需用户密钥）→ 失败返回 null（UI 显示占位）。
/// 成功结果按 key（titleId 或名称哈希）缓存到本地。
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

    public CoverService(SettingsService settings, string? cacheDir = null)
    {
        _settings = settings;
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "covers");
        Directory.CreateDirectory(_cacheDir);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchAlbum/1.0");
    }

    public string? GetCachedPath(string key)
    {
        var path = CachePathOf(key);
        return File.Exists(path) ? path : null;
    }

    public async Task<string?> ResolveAsync(
        string? titleId, string title, IReadOnlyList<string>? coverUrls, CancellationToken ct)
    {
        var key = !string.IsNullOrEmpty(titleId) ? titleId : NameKey(title);
        var cached = GetCachedPath(key);
        if (cached != null)
        {
            return cached;
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = CachePathOf(key);

            if (coverUrls != null)
            {
                foreach (var url in coverUrls)
                {
                    if (await TryDownloadAsync(url, path, ct).ConfigureAwait(false))
                    {
                        return path;
                    }
                }
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

            Log.Info($"封面获取失败: {title} (tid={titleId ?? "无"})，显示占位图");
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
        // 每个源尝试 2 次（网络抖动时第二次常成功）
        for (var attempt = 1; attempt <= 2; attempt++)
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
                if (image.Width > 0)
                {
                    return true;
                }

                TryDelete(dest);
                return false;
            }
            catch (Exception ex) when (attempt == 1)
            {
                Log.Info($"封面下载失败（第 {attempt} 次，将重试）: {url} — {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Info($"封面下载失败: {url} — {ex.Message}");
                TryDelete(dest);
                return false;
            }
        }

        return false;
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
