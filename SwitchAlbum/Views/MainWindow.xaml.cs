using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _main;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_main != null)
        {
            _main.PropertyChanged -= OnMainPropertyChanged;
        }

        _main = e.NewValue as MainViewModel;
        if (_main != null)
        {
            _main.PropertyChanged += OnMainPropertyChanged;
        }
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Lightbox))
        {
            return;
        }

        // 灯箱打开/切换时订阅全图加载事件，做 crossfade
        if (_main?.Lightbox is { } lightbox)
        {
            lightbox.PropertyChanged -= OnLightboxPropertyChanged;
            lightbox.PropertyChanged += OnLightboxPropertyChanged;
        }
    }

    private void OnLightboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LightboxViewModel.FullPath))
        {
            return;
        }

        LightboxImage.BeginAnimation(OpacityProperty, null);
        LightboxImage.Opacity = 0;
        LightboxImage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_main == null)
        {
            return;
        }

        var settings = _main.Settings;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _ = _main.StartAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_main == null)
        {
            return;
        }

        var bounds = RestoreBounds;
        _main.SaveWindowBounds(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            WindowState == WindowState.Maximized);
        _ = _main.ShutdownAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var lightbox = _main?.Lightbox;
        if (lightbox == null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                lightbox.CloseCommand.Execute(null);
                break;
            case Key.Left:
                lightbox.PrevCommand.Execute(null);
                break;
            case Key.Right:
                lightbox.NextCommand.Execute(null);
                break;
        }
    }
}
