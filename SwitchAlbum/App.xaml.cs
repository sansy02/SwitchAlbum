using System.Windows;
using SwitchAlbum.Services;
using SwitchAlbum.ViewModels;
using SwitchAlbum.Views;

namespace SwitchAlbum;

public partial class App : Application
{
    private SettingsService? _settings;
    private ThemeService? _theme;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            // 未处理异常不崩溃：状态栏提示由各命令自行处理，这里兜底记录
            System.Diagnostics.Debug.WriteLine("Unhandled: " + args.Exception);
            args.Handled = true;
        };

        if (e.Args.Contains("--smoke-test"))
        {
            SmokeTest.RunAsync();
            return;
        }

        _settings = new SettingsService();
        _theme = new ThemeService(_settings);
        ApplyTheme();
        _theme.ThemeChanged += ApplyTheme;

        var tracker = new DuplicateTracker();
        var saveService = new SaveService(tracker);
        var phoneSaveService = new PhoneSaveService(tracker, _settings);
        var thumbnailService = new ThumbnailService();
        var coverService = new CoverService(_settings);

        var mainViewModel = new MainViewModel(
            _settings, _theme, tracker, saveService, phoneSaveService,
            thumbnailService, coverService, new ScanCacheService(), TitleDbService.LoadEmbedded());

        var window = new MainWindow { DataContext = mainViewModel };
        mainViewModel.AttachWindow(window);
        MainWindow = window;
        window.Show();
    }

    private void ApplyTheme()
    {
        var merged = Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(new ResourceDictionary
        {
            Source = new Uri(
                _theme!.IsDark ? "Resources/Themes/Colors.Dark.xaml" : "Resources/Themes/Colors.Light.xaml",
                UriKind.Relative),
        });
        merged.Add(new ResourceDictionary
        {
            Source = new Uri("Resources/Themes/Styles.xaml", UriKind.Relative),
        });
    }
}
