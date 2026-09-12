using System.Text;
using System.Text.Json;

// JaMerger：把 blawar/titledb 的 JP.ja.json 中的日文名合并进现有 titles.json（保留原有全部字段）。
// 用法: JaMerger <现有titles.json> <JP.ja.json> <输出titles.json>

if (args.Length != 3)
{
    Console.Error.WriteLine("用法: JaMerger <现有titles.json> <JP.ja.json> <输出titles.json>");
    return 1;
}

var jpNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
{
    var bytes = File.ReadAllBytes(args[1]);
    var reader = new Utf8JsonReader(bytes, isFinalBlock: true, default);
    var depth = 0;
    string? field = null;
    string? id = null;
    string? name = null;
    while (reader.Read())
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject when depth == 0:
                depth = 1;
                break;
            case JsonTokenType.StartObject when depth == 1:
                depth = 2;
                break;
            case JsonTokenType.PropertyName when depth == 2:
                field = reader.GetString();
                break;
            case JsonTokenType.String when depth == 2 && field == "id":
                id = reader.GetString();
                break;
            case JsonTokenType.String when depth == 2 && field == "name":
                name = reader.GetString();
                break;
            case JsonTokenType.EndObject when depth == 2:
                if (id != null && name != null && id.Length == 16 && id.All(Uri.IsHexDigit))
                {
                    jpNames[id.ToUpperInvariant()] = name;
                }
                depth = 1;
                field = null;
                id = null;
                name = null;
                break;
            case JsonTokenType.EndObject when depth == 1:
                depth = 0;
                break;
        }
    }

    Console.WriteLine($"JP.ja.json 解析完成：{jpNames.Count} 个日文名");
}

using var doc = JsonDocument.Parse(File.ReadAllBytes(args[0]));
var merged = 0;
var skipped = 0;
using (var output = File.Create(args[2]))
using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
{
    writer.WriteStartObject();
    foreach (var property in doc.RootElement.EnumerateObject())
    {
        var tid = property.Name;
        var value = property.Value;
        writer.WritePropertyName(tid);
        writer.WriteStartObject();
        foreach (var field in value.EnumerateObject())
        {
            if (field.Name == "ja")
            {
                continue;
            }

            field.WriteTo(writer);
        }

        if (!value.TryGetProperty("ja", out _) && jpNames.TryGetValue(tid, out var jaName))
        {
            writer.WriteString("ja", jaName);
            merged++;
        }
        else
        {
            skipped++;
        }

        writer.WriteEndObject();
    }

    writer.WriteEndObject();
}

Console.WriteLine($"合并完成：新增日文名 {merged} 个，跳过 {skipped} 个，输出 {(new FileInfo(args[2]).Length) / 1024 / 1024} MB");
return 0;
