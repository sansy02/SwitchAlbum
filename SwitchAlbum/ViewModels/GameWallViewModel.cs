using System.Collections.ObjectModel;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.ViewModels;

/// <summary>游戏卡片墙（Switch 相册风格：封面 + 游戏名 + 数量，首卡为「全部」）。</summary>
public sealed class GameWallViewModel
{
    public GameWallViewModel(ScanResult scan, MainViewModel main)
    {
        Cards.Add(new GameCardViewModel(
            Strings.Card_AllGames, devicePath: null, items: scan.AllItems,
            isAllCard: true, titleId: null, main));

        foreach (var game in scan.Games)
        {
            var items = scan.ItemsByGame.TryGetValue(game.DevicePath, out var list)
                ? list
                : Array.Empty<Models.AlbumItem>();
            Cards.Add(new GameCardViewModel(game.Title, game.DevicePath, items, isAllCard: false, game.TitleId, main));
        }
    }

    public ObservableCollection<GameCardViewModel> Cards { get; } = new();
}
