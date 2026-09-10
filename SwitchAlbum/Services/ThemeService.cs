using Microsoft.Win32;

namespace SwitchAlbum.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed class ThemeService
{
    private readonly SettingsService _settings;

    public ThemeService(SettingsService settings) => _settings = settings;

    public event Action? ThemeChanged;

    public AppTheme Theme => Enum.TryParse<AppTheme>(_settings.Current.Theme, out var t) ? t : AppTheme.System;

    public bool IsDark => Theme switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => SystemUsesDarkTheme(),
    };

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public void SetTheme(AppTheme theme)
    {
        _settings.Current.Theme = theme.ToString();
        _settings.Save();
        ThemeChanged?.Invoke();
    }
}
