using System.Text.Json;

namespace SwitchAlbum.Services;

/// <summary>
/// 游戏名 ↔ TitleId 映射库，加载打包的 titles.json：
/// { "TID": { "zh": "...", "zht": "...", "en": "...", "icon": "https://..." } }。
/// 名称注册时同时登记繁→简（OpenCC TSCharacters）变体，兼容简体系统主机的文件夹名。
/// </summary>
public sealed class TitleDbService : ITitleDb
{
    /// <summary>
    /// 机械繁→简与官方简体名不一致的品牌名例外（简体 → 官方简体）。
    /// 例如 薩爾達傳說 机械转换为「萨尔达传说」，但官方简中为「塞尔达传说」。
    /// </summary>
    private static readonly (string From, string To)[] Aliases =
    {
        ("萨尔达", "塞尔达"),
        ("玛利欧", "马力欧"),
        ("玛莉欧", "马力欧"),
        ("瓦利欧", "瓦力欧"),
        ("马力欧赛车", "马力欧卡丁车"),
        ("玛利欧赛车", "马力欧卡丁车"),
        ("大金刚", "咚奇刚"),
        ("路易吉洋楼", "路易吉洋馆"),
    };

    private readonly Dictionary<string, TitleNames> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _coverUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<char, string> _t2sChar = new();
    private readonly List<(string Key, string Value)> _t2sMulti = new();

    public static TitleDbService? LoadEmbedded()
    {
        var assembly = typeof(TitleDbService).Assembly;
        var jsonStream = assembly.GetManifestResourceStream("SwitchAlbum.Resources.titles.json");
        if (jsonStream == null)
        {
            return null;
        }

        var t2sStream = assembly.GetManifestResourceStream("SwitchAlbum.Resources.TSCharacters.txt");
        using (jsonStream)
        using (t2sStream)
        {
            return new TitleDbService(jsonStream, t2sStream);
        }
    }

    public TitleDbService(Stream json, Stream? t2s = null)
    {
        LoadT2S(t2s);

        using var doc = JsonDocument.Parse(json);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var tid = property.Name;
            var value = property.Value;
            var names = new TitleNames(GetString(value, "zh"), GetString(value, "zht"), GetString(value, "en"));
            if (names.Best == null)
            {
                continue;
            }

            _byId[tid] = names;

            var urls = new[] { GetString(value, "icon"), GetString(value, "banner"), GetString(value, "box") }
                .Where(u => !string.IsNullOrEmpty(u))
                .Cast<string>()
                .ToArray();
            if (urls.Length > 0)
            {
                _coverUrls[tid] = urls;
            }

            RegisterName(names.Zh, tid);
            RegisterName(names.ZhHant, tid);
            RegisterName(names.En, tid);
        }
    }

    public string? GetNameByTitleId(string titleId)
        => _byId.TryGetValue(titleId, out var names) ? names.Best : null;

    public string? GetTitleIdByName(string titleName)
        => _byName.TryGetValue(Normalize(titleName), out var tid) ? tid : null;

    /// <summary>TitleId → 封面候选图直链（按 图标→横幅→盒装 顺序），无则空数组。</summary>
    public IReadOnlyList<string> GetCoverUrls(string titleId)
        => _coverUrls.TryGetValue(titleId, out var urls) ? urls : Array.Empty<string>();

    public string? GetIconUrl(string titleId)
        => GetCoverUrls(titleId).FirstOrDefault();

    /// <summary>
    /// 规范化游戏名：小写、全角转半角、剔除空白与标点符号。
    /// 例如 "集合啦！动物森友会" → "集合啦动物森友会"。
    /// </summary>
    public static string Normalize(string? name)
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

    private void RegisterName(string? name, string tid)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _byName.TryAdd(Normalize(name), tid);

        var simplified = ToSimplified(name);
        if (!string.Equals(simplified, name, StringComparison.Ordinal))
        {
            _byName.TryAdd(Normalize(simplified), tid);
        }

        var aliased = ApplyAliases(simplified);
        if (!string.Equals(aliased, simplified, StringComparison.Ordinal))
        {
            _byName.TryAdd(Normalize(aliased), tid);
        }
    }

    private static string ApplyAliases(string name)
    {
        var result = name;
        foreach (var (from, to) in Aliases)
        {
            if (result.Contains(from, StringComparison.Ordinal))
            {
                result = result.Replace(from, to);
            }
        }

        return result;
    }

    private string ToSimplified(string name)
    {
        if (_t2sChar.Count == 0 && _t2sMulti.Count == 0)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(_t2sChar.TryGetValue(c, out var simplified) ? simplified : c.ToString());
        }

        var result = sb.ToString();
        foreach (var (key, value) in _t2sMulti)
        {
            result = result.Replace(key, value);
        }

        return result;
    }

    private void LoadT2S(Stream? stream)
    {
        if (stream == null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var key = line[..tab];
            var values = line[(tab + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0)
            {
                continue;
            }

            if (key.Length == 1)
            {
                _t2sChar[key[0]] = values[0];
            }
            else
            {
                _t2sMulti.Add((key, values[0]));
            }
        }

        _t2sMulti.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
    }

    private static string? GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var prop) ? prop.GetString() : null;

    private sealed record TitleNames(string? Zh, string? ZhHant, string? En)
    {
        public string? Best => Zh ?? ZhHant ?? En;
    }
}
