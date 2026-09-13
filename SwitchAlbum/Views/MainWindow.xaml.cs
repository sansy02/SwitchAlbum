using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SwitchAlbum.Services;
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
        Width = Math.Min(settings.WindowWidth, SystemParameters.VirtualScreenWidth);
        Height = Math.Min(settings.WindowHeight, SystemParameters.VirtualScreenHeight);

        // 显示器布局可能已变化：保存的位置越出虚拟屏时不再恢复（交给 CenterScreen 居中），
        // 防止窗口跑到屏幕外——进程在后台、界面却看不见。
        if (WindowBounds.IsVisibleOnScreen(
                settings.WindowLeft, settings.WindowTop, Width, Height,
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        else if (!double.IsNaN(settings.WindowLeft) || !double.IsNaN(settings.WindowTop))
        {
            Log.Info($"保存的窗口位置越出当前屏幕（{settings.WindowLeft},{settings.WindowTop}），已重置为居中");
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
