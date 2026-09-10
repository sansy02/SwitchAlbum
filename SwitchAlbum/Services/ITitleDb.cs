namespace SwitchAlbum.Services;

/// <summary>游戏名 ↔ TitleId 映射库（由打包的 titles.json 实现）。</summary>
public interface ITitleDb
{
    /// <summary>TitleId → 最佳中文名（无中文则英文）。</summary>
    string? GetNameByTitleId(string titleId);

    /// <summary>规范化游戏名 → TitleId（用于封面图源，按 titleId 取图）。</summary>
    string? GetTitleIdByName(string titleName);

    /// <summary>TitleId → 任天堂 eShop 官方图标直链（可能为 null）。</summary>
    string? GetIconUrl(string titleId);
}
