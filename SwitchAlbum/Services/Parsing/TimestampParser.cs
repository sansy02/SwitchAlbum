using System.Globalization;

namespace SwitchAlbum.Services.Parsing;

/// <summary>
/// 解析 Switch 文件名中的时间戳。文件名形如 "2025031118460900_s.jpg"（前 14 位为
/// yyyyMMddHHmmss，后 2 位为厘秒），兼容 14 位与 16 位两种形式。
/// </summary>
public static class TimestampParser
{
    public static bool TryParse(string? input, out DateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        int i = 0;
        while (i < input.Length && char.IsDigit(input[i]))
        {
            i++;
        }

        var digits = input[..i];
        if (digits.Length < 14)
        {
            return false;
        }

        var core = digits[..14];
        return DateTime.TryParseExact(
            core, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
