using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

// CoverPackGenerator：生成 SwitchAlbum 的离线热门封面包 covers.zip（条目名 <TID>.jpg，256px JPEG）。
// 排名：维基百科 Switch 畅销榜（真实热门信号，+10 分）> 多语言名+官方图源齐全度评分。
// 用法: CoverPackGenerator <titles.json> <输出covers.zip> [数量=3000] [最大总字节MB=160]

if (args.Length < 2)
{
    Console.Error.WriteLine("用法: CoverPackGenerator <titles.json> <输出covers.zip> [数量] [最大总字节MB]");
    return 1;
}

var maxCount = args.Length > 2 && int.TryParse(args[2], out var c) ? c : 3000;
var maxTotalBytes = (args.Length > 3 && int.TryParse(args[3], out var mb) ? mb : 160) * 1024L * 1024L;

// 1. 读取 titles.json：打分 + 建立 英文名 → tid 索引（用于匹配畅销榜）
var candidates = new List<(string Tid, string Icon, string Banner, int Score)>();
var enIndex = new Dictionary<string, string>(StringComparer.Ordinal);
using (var doc = JsonDocument.Parse(File.ReadAllBytes(args[0])))
{
    foreach (var property in doc.RootElement.EnumerateObject())
    {
        var value = property.Value;
        string? icon = null;
        string? banner = null;
        var score = 0;

        if (value.TryGetProperty("zh", out var zh) && !string.IsNullOrEmpty(zh.GetString()))
        {
            score += 1;
        }

        if (value.TryGetProperty("zht", out var zht) && !string.IsNullOrEmpty(zht.GetString()))
        {
            score += 1;
        }

        if (value.TryGetProperty("en", out var en) && !string.IsNullOrEmpty(en.GetString()))
        {
            score += 1;
            enIndex.TryAdd(Normalize(en.GetString()!), property.Name);
        }

        if (value.TryGetProperty("icon", out var iconProp) && !string.IsNullOrEmpty(iconProp.GetString()))
        {
            icon = iconProp.GetString();
            score += 2;
        }

        if (value.TryGetProperty("banner", out var bannerProp) && !string.IsNullOrEmpty(bannerProp.GetString()))
        {
            banner = bannerProp.GetString();
            score += 1;
        }

        if (icon != null && score >= 5)
        {
            candidates.Add((property.Name, icon, banner ?? "", score));
        }
    }
}

// 2. 维基百科畅销榜 → 真实热门信号（失败则退化为纯评分排序）
var boosted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var wikiCount = await TryLoadWikiBestSellersAsync(boosted);
if (wikiCount > 0)
{
    Console.WriteLine($"畅销榜命中 {wikiCount} 款游戏，加权排序中");
}
else
{
    Console.WriteLine("畅销榜获取失败或未命中，使用纯评分排序");
}

var picked = candidates
    .OrderByDescending(x => x.Score + (boosted.Contains(x.Tid) ? 10 : 0))
    .ThenBy(x => x.Tid, StringComparer.Ordinal)
    .Take(maxCount)
    .ToList();
var boostedPicked = picked.Count(x => boosted.Contains(x.Tid));
Console.WriteLine($"候选 {candidates.Count} 款，选中 {picked.Count} 款（其中畅销榜 {boostedPicked} 款），开始下载...");

// 3. 并发下载 + 降采样（图标失败回退横幅，各重试 2 次）
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchAlbum/1.0");
var semaphore = new SemaphoreSlim(6);
var results = new List<(string Tid, byte[] Data)>();
var failed = 0;
var done = 0;

async Task<(string Tid, byte[]? Data)> FetchAsync((string Tid, string Icon, string Banner, int Score) item, CancellationToken ct)
{
    await semaphore.WaitAsync(ct);
    try
    {
        foreach (var url in new[] { item.Icon, item.Banner })
        {
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var response = await http.GetAsync(url, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    if (ms.Length > 4 * 1024 * 1024)
                    {
                        break; // 超大图片跳过该源
                    }

                    var downscaled = Downscale(ms.ToArray());
                    if (downscaled != null)
                    {
                        return (item.Tid, downscaled);
                    }
                }
                catch
                {
                    await Task.Delay(400, ct); // 限流时稍等再试
                }
            }
        }

        return (item.Tid, null);
    }
    finally
    {
        semaphore.Release();
    }
}

byte[]? Downscale(byte[] source)
{
    try
    {
        using var image = Image.FromStream(new MemoryStream(source));
        var scale = Math.Min(1.0, 256.0 / Math.Max(image.Width, image.Height));
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, width, height);
        }

        using var output = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        bitmap.Save(output, encoder, parameters);
        return output.ToArray();
    }
    catch
    {
        return null;
    }
}

var cts = new CancellationTokenSource();
var tasks = picked.Select(async item =>
{
    var (tid, data) = await FetchAsync(item, cts.Token);
    var current = Interlocked.Increment(ref done);
    if (data == null)
    {
        Interlocked.Increment(ref failed);
    }
    else
    {
        lock (results)
        {
            results.Add((tid, data));
        }
    }

    if (current % 200 == 0)
    {
        Console.WriteLine($"  进度 {current}/{picked.Count}，成功 {results.Count}，失败 {failed}");
    }
}).ToList();

await Task.WhenAll(tasks);
Console.WriteLine($"下载完成：成功 {results.Count}，失败 {failed}");

// 4. 写入 zip（按总量预算截断）
long total = 0;
var written = 0;
using (var output = File.Create(args[1]))
using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
{
    foreach (var (tid, data) in results.OrderBy(x => x.Tid, StringComparer.Ordinal))
    {
        if (total + data.Length > maxTotalBytes)
        {
            Console.WriteLine($"达到体积预算 {maxTotalBytes / 1024 / 1024}MB，截断于 {written} 个");
            break;
        }

        var entry = archive.CreateEntry(tid + ".jpg", CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(data);
        total += data.Length;
        written++;
    }
}

Console.WriteLine($"完成: {args[1]} 共 {written} 个封面，{(double)total / 1024 / 1024:F1} MB");
return 0;

// ---- 畅销榜 ----
async Task<int> TryLoadWikiBestSellersAsync(HashSet<string> boosted)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchAlbum/1.0");
        var api = "https://en.wikipedia.org/w/api.php?action=parse&page=List_of_best-selling_Nintendo_Switch_video_games&prop=wikitext&format=json&formatversion=2";
        var json = await client.GetStringAsync(api);
        using var parsed = JsonDocument.Parse(json);
        var wikitext = parsed.RootElement.GetProperty("parse").GetProperty("wikitext").GetString() ?? "";

        var count = 0;
        foreach (var rawLine in wikitext.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0
                || line.StartsWith("|-")
                || line.StartsWith("|+")
                || line.StartsWith("|}")
                || line.StartsWith("|{"))
            {
                continue;
            }

            // `! scope="row" | 游戏名`：去掉前导 ! 段后剩下的即游戏名
            if (line.StartsWith('!'))
            {
                var pipe = line.IndexOf('|');
                if (pipe < 0)
                {
                    continue;
                }

                var name = CleanWikiName(line[(pipe + 1)..]);
                if (name.Length > 0 && enIndex.TryGetValue(Normalize(name), out var tid1))
                {
                    boosted.Add(tid1);
                    count++;
                }

                continue;
            }

            // `| 排名 || 游戏名 || 销量 ...`
            if (!line.StartsWith('|') || line.StartsWith("|!"))
            {
                continue;
            }

            var cells = line.Split("||");
            if (cells.Length < 2)
            {
                continue;
            }

            var cellName = CleanWikiName(cells[1]);
            if (cellName.Length > 0 && enIndex.TryGetValue(Normalize(cellName), out var tid2))
            {
                boosted.Add(tid2);
                count++;
            }
        }

        return count;
    }
    catch
    {
        return 0;
    }
}

string CleanWikiName(string cell)
{
    // 去掉 [[链接]]、{{模板}}、<ref>、斜体等 wiki 标记
    var sb = new StringBuilder(cell);
    RemoveBraces(sb, "{{", "}}");
    RemoveBraces(sb, "[[", "]]", keepInner: true);
    RemoveBraces(sb, "<ref", "</ref>");
    var text = sb.ToString()
        .Replace("''", "")
        .Replace("|", " ")
        .Trim();
    return text;
}

void RemoveBraces(StringBuilder sb, string open, string close, bool keepInner = false)
{
    while (true)
    {
        var start = sb.ToString().IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            return;
        }

        var end = sb.ToString().IndexOf(close, start + open.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            return;
        }

        var inner = sb.ToString(start + open.Length, end - start - open.Length);
        sb.Remove(start, end + close.Length - start);
        if (keepInner)
        {
            sb.Insert(start, inner);
        }
    }
}

string Normalize(string? name)
{
    if (string.IsNullOrEmpty(name))
    {
        return "";
    }

    var sb = new StringBuilder(name.Length);
    foreach (var c in name)
    {
        var ch = c switch
        {
            '　' => ' ',
            >= '！' and <= '～' => (char)(c - 0xFEE0),
            _ => c,
        };

        if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch) || char.IsSeparator(ch))
        {
            continue;
        }

        sb.Append(char.ToLowerInvariant(ch));
    }

    return sb.ToString();
}
