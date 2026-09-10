using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.ViewModels;

public sealed class PhoneOption
{
    public PhoneOption(string? id, string name)
    {
        Id = id;
        Name = name;
    }

    public string? Id { get; }
    public string Name { get; }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly IMediaProvider _provider;

    public SettingsViewModel(SettingsService settings, ThemeService theme, IMediaProvider provider)
    {
        _settings = settings;
        _theme = theme;
        _provider = provider;
        _savePath = settings.Current.SavePath;
        _autoRename = settings.Current.AutoRename;
        _themeOption = MapToOption(theme.Theme);
        _apiKey = settings.Current.SteamGridDbApiKey ?? "";
        _mockEnabled = settings.Current.MockModeEnabled;
        _ = LoadPhonesAsync();
    }

    [ObservableProperty] private string _savePath;
    [ObservableProperty] private bool _autoRename;
    [ObservableProperty] private string _themeOption;
    [ObservableProperty] private string _apiKey;
    [ObservableProperty] private bool _mockEnabled;
    [ObservableProperty] private PhoneOption? _selectedPhone;

    /// <summary>模拟模式开关是否在本次设置中发生变化（供主界面重建设备提供者）。</summary>
    public bool MockChanged { get; private set; }

    public ObservableCollection<PhoneOption> Phones { get; } = new();

    public string[] ThemeOptions { get; } =
    {
        Strings.Settings_Theme_System,
        Strings.Settings_Theme_Light,
        Strings.Settings_Theme_Dark,
    };

    private static string MapToOption(AppTheme theme) => theme switch
    {
        AppTheme.Light => Strings.Settings_Theme_Light,
        AppTheme.Dark => Strings.Settings_Theme_Dark,
        _ => Strings.Settings_Theme_System,
    };

    private static AppTheme MapToTheme(string option) => option switch
    {
        Strings.Settings_Theme_Light => AppTheme.Light,
        Strings.Settings_Theme_Dark => AppTheme.Dark,
        _ => AppTheme.System,
    };

    public void SetSavePath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            SavePath = path;
        }
    }

    public void Save()
    {
        _settings.Current.SavePath = SavePath;
        _settings.Current.AutoRename = AutoRename;
        _settings.Current.SteamGridDbApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();

        MockChanged = MockEnabled != _settings.Current.MockModeEnabled;
        _settings.Current.MockModeEnabled = MockEnabled;

        _settings.Current.PhoneDeviceId = SelectedPhone?.Id;
        _settings.Current.PhoneFriendlyName = SelectedPhone?.Name;
        _settings.Save();

        _theme.SetTheme(MapToTheme(ThemeOption));
    }

    [RelayCommand]
    private async Task RefreshPhonesAsync() => await LoadPhonesAsync();

    [RelayCommand]
    private void RegenMockData()
    {
        DemoAlbumGenerator.Generate(_settings.Current.MockAlbumPath);
    }

    private async Task LoadPhonesAsync()
    {
        Phones.Clear();
        Phones.Add(new PhoneOption(null, Strings.Settings_PhoneDevice_Auto));

        var devices = await _provider.GetDevicesAsync(CancellationToken.None);
        foreach (var device in devices.Where(d => !d.IsSwitch))
        {
            Phones.Add(new PhoneOption(device.DeviceId, device.FriendlyName));
        }

        SelectedPhone = Phones.FirstOrDefault(p => p.Id == _settings.Current.PhoneDeviceId) ?? Phones[0];
    }
}
