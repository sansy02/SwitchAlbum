using System.Windows;
using System.Windows.Controls;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Views;

public partial class GameGridView : UserControl
{
    public GameGridView()
    {
        InitializeComponent();
    }

    private void GridList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not GameGridViewModel viewModel)
        {
            return;
        }

        // 卡片 212 + 边距 8 = 220
        var columns = Math.Max(2, (int)((GridList.ActualWidth - 28) / 220));
        viewModel.SetColumns(columns);
    }
}
