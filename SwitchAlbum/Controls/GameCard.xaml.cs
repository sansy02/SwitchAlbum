using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Controls;

public partial class GameCard : UserControl
{
    public GameCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GameCardViewModel viewModel)
            {
                _ = viewModel.EnsureCoverAsync();
            }
        };
        Loaded += (_, _) =>
        {
            if (DataContext is GameCardViewModel viewModel)
            {
                _ = viewModel.EnsureCoverAsync();
            }
        };
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e) => AnimateScale(1.04);

    private void Root_MouseLeave(object sender, MouseEventArgs e) => AnimateScale(1.0);

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GameCardViewModel viewModel)
        {
            viewModel.OpenCommand.Execute(null);
        }
    }

    private void AnimateScale(double target)
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(target, duration) { EasingFunction = ease });
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(target, duration) { EasingFunction = ease });
    }
}
