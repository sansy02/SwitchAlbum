namespace SwitchAlbum.ViewModels;

/// <summary>
/// 本地相册页的网格视图模型：行为与 Switch 网格一致，仅作为类型标记，
/// 让 MainWindow 的 DataTemplate 路由到 LocalGameGridView（工具栏按钮不同）。
/// </summary>
public sealed class LocalGameGridViewModel : GameGridViewModel
{
    public LocalGameGridViewModel(string title, IReadOnlyList<Models.AlbumItem> items, MainViewModel main)
        : base(title, items, main)
    {
    }
}
