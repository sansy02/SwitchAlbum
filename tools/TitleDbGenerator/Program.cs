using System.Text.Json;

// TitleDbGenerator：从 blawar/titledb 的 region 文件生成 SwitchAlbum 的紧凑 titles.json。
// 用法: TitleDbGenerator <输出文件> <zhHans文件(CN.zh.json)> <zhHant文件(HK.zh.json)> <en文件(US.en.json)>
// 输出格式: { "<TID>": { "zh": "...", "zht": "...", "en": "...", "icon": "...", "banner": "...", "box": "..." } }

if (args.Length != 4)
{
    Console.Error.WriteLine("用法: TitleDbGenerator <输出> <zhHans.json> <zhHant.json> <en.json>");
    return 1;
}

var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

Entry GetEntry(string tid)
{
    if (!entries.TryGetValue(tid, out var entry))
    {
        entry = new Entry();
        entries[tid] = entry;
    }

    return entry;
}

var start = DateTime.Now;

Console.WriteLine($"解析 {Path.GetFileName(args[1])}（简体中文）...");
ParseFile(args[1], (tid, name, icon, banner, box) =>
{
    var entry = GetEntry(tid);
    entry.Zh ??= name;
    entry.Icon ??= icon;
    entry.Banner ??= banner;
    entry.Box ??= box;
});

Console.WriteLine($"解析 {Path.GetFileName(args[2])}（繁体中文）...");
ParseFile(args[2], (tid, name, icon, banner, box) =>
{
    var entry = GetEntry(tid);
    entry.ZhHant ??= name;
    entry.Icon ??= icon;
    entry.Banner ??= banner;
    entry.Box ??= box;
});

Console.WriteLine($"解析 {Path.GetFileName(args[3])}（英文）...");
ParseFile(args[3], (tid, name, icon, banner, box) =>
{
    var entry = GetEntry(tid);
    entry.En ??= name;
    entry.Icon ??= icon;
    entry.Banner ??= banner;
    entry.Box ??= box;
});

Console.WriteLine($"共 {entries.Count} 个游戏，解析耗时 {(DateTime.Now - start).TotalSeconds:F1}s，写出中...");

using (var output = File.Create(args[0]))
using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
{
    writer.WriteStartObject();
    foreach (var (tid, entry) in entries.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        if (entry.IsEmpty)
        {
            continue;
        }

        writer.WritePropertyName(tid);
        writer.WriteStartObject();
        WriteString(writer, "zh", entry.Zh);
        WriteString(writer, "zht", entry.ZhHant);
        WriteString(writer, "en", entry.En);
        WriteString(writer, "icon", entry.Icon);
        WriteString(writer, "banner", entry.Banner);
        WriteString(writer, "box", entry.Box);
        writer.WriteEndObject();
    }

    writer.WriteEndObject();
}

Console.WriteLine("完成: " + new FileInfo(args[0]).Length / 1024 / 1024 + " MB");
return 0;

void ParseFile(string path, Action<string, string, string, string, string> apply)
{
    var bytes = File.ReadAllBytes(path);
    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, default);

    var depth = 0;
    string? outerKey = null;
    string? tid = null;
    string? field = null;
    string? name = null;
    string? icon = null;
    string? banner = null;
    string? box = null;

    while (reader.Read())
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject when depth == 0:
                depth = 1;
                break;
            case JsonTokenType.PropertyName when depth == 1:
                outerKey = reader.GetString();
                break;
            case JsonTokenType.StartObject when depth == 1:
                depth = 2;
                break;
            case JsonTokenType.PropertyName when depth == 2:
                field = reader.GetString();
                break;
            case JsonTokenType.String when depth == 2:
                switch (field)
                {
                    case "id":
                        tid = reader.GetString();
                        break;
                    case "name":
                        name = reader.GetString();
                        break;
                    case "iconUrl":
                        icon = reader.GetString();
                        break;
                    case "bannerUrl":
                        banner = reader.GetString();
                        break;
                    case "frontBoxArt":
                        box = reader.GetString();
                        break;
                }

                break;
            case JsonTokenType.EndObject when depth == 2:
                var key = tid ?? outerKey;
                if (key != null && key.Length == 16 && key.All(Uri.IsHexDigit) && name != null)
                {
                    apply(key.ToUpperInvariant(), name, icon ?? "", banner ?? "", box ?? "");
                }

                depth = 1;
                tid = null;
                field = null;
                name = null;
                icon = null;
                banner = null;
                box = null;
                break;
            case JsonTokenType.EndObject when depth == 1:
                depth = 0;
                break;
        }
    }
}

static void WriteString(Utf8JsonWriter writer, string key, string? value)
{
    if (!string.IsNullOrEmpty(value))
    {
        writer.WriteString(key, value);
    }
}

internal sealed class Entry
{
    public string? Zh { get; set; }
    public string? ZhHant { get; set; }
    public string? En { get; set; }
    public string? Icon { get; set; }
    public string? Banner { get; set; }
    public string? Box { get; set; }

    public bool IsEmpty => Zh == null && ZhHant == null && En == null;
}
