using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Controls;

public partial class PhotoCard : UserControl
{
    public PhotoCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PhotoItemViewModel viewModel)
            {
                _ = viewModel.LoadThumbAsync();
            }
        };
        Loaded += (_, _) =>
        {
            if (DataContext is PhotoItemViewModel viewModel)
            {
                _ = viewModel.LoadThumbAsync();
            }
        };
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e) => AnimateScale(1.02);

    private void Root_MouseLeave(object sender, MouseEventArgs e) => AnimateScale(1.0);

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || DataContext is not PhotoItemViewModel viewModel || SelectBox.IsMouseOver)
        {
            return;
        }

        var window = Window.GetWindow(this);
        if (window?.DataContext is MainViewModel main)
        {
            main.Grid?.OpenLightbox(viewModel);
            e.Handled = true;
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
