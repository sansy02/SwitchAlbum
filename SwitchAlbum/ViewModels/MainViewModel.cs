using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SwitchAlbum.Models;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly DuplicateTracker _tracker;
    private readonly SaveService _saveService;
    private readonly PhoneSaveService _phoneSaveService;
    private readonly ThumbnailService _thumbnailService;
    private readonly CoverService _coverService;
    private readonly ITitleDb? _titleDb;
    private readonly SynchronizationContext? _ui;

    private IMediaProvider _provider;
    private IMediaDeviceSession? _session;
    private ScanResult? _scan;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _transferCts;
    private Window? _owner;
    private string? _deviceKey;
    private int _toastSequence;

    public MainViewModel(
        SettingsService settings,
        ThemeService theme,
        DuplicateTracker tracker,
        SaveService saveService,
        PhoneSaveService phoneSaveService,
        ThumbnailService thumbnailService,
        CoverService coverService,
        ITitleDb? titleDb)
    {
        _settings = settings;
        _theme = theme;
        _tracker = tracker;
        _saveService = saveService;
        _phoneSaveService = phoneSaveService;
        _thumbnailService = thumbnailService;
        _coverService = coverService;
        _titleDb = titleDb;
        _ui = SynchronizationContext.Current;

        SavePathText = settings.Current.SavePath;
        _provider = CreateProvider();
        _provider.DeviceChanged += OnDeviceChanged;
    }

    // ---------- 状态 ----------
    public AppSettings Settings => _settings.Current;

    /// <summary>内容区当前视图（卡片墙或照片网格），视图切换触发过渡动画。</summary>
    public object? CurrentContent => ShowWall ? Wall : Grid;

    partial void OnShowWallChanged(bool value) => OnPropertyChanged(nameof(CurrentContent));
    partial void OnWallChanged(GameWallViewModel? value) => OnPropertyChanged(nameof(CurrentContent));
    partial void OnGridChanged(GameGridViewModel? value) => OnPropertyChanged(nameof(CurrentContent));

    [ObservableProperty] private string _deviceStatusText = Strings.Status_NotConnected;
    [ObservableProperty] private bool _isDeviceConnected;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isTransferring;
    [ObservableProperty] private bool _progressIndeterminate;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _showWall = true;
    [ObservableProperty] private GameWallViewModel? _wall;
    [ObservableProperty] private GameGridViewModel? _grid;
    [ObservableProperty] private LightboxViewModel? _lightbox;
    [ObservableProperty] private string _hintText = "";
    [ObservableProperty] private bool _hintVisible;
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _toastVisible;
    [ObservableProperty] private string _savePathText;

    // ---------- 生命周期 ----------
    public void AttachWindow(Window window) => _owner = window;

    public Task StartAsync() => RescanAsync();

    public void SaveWindowBounds(double left, double top, double width, double height, bool maximized)
    {
        _settings.Current.WindowLeft = left;
        _settings.Current.WindowTop = top;
        _settings.Current.WindowWidth = width;
        _settings.Current.WindowHeight = height;
        _settings.Current.WindowMaximized = maximized;
        _settings.Save();
    }

    public async Task ShutdownAsync()
    {
        _tracker.Flush();
        await DisposeSessionAsync();
    }

    // ---------- 设备 ----------
    private static IMediaProvider CreateProvider() => new MtpMediaProvider();

    private async Task DisposeSessionAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session != null)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // 设备已物理断开时 Dispose 可能失败
            }
        }
    }

    private void OnDeviceChanged(object? sender, DeviceChangedEventArgs e)
    {
        _ui?.Post(_ => _ = OnDeviceChangedUiAsync(e), null);
    }

    private async Task OnDeviceChangedUiAsync(DeviceChangedEventArgs e)
    {
        if (e.Connected)
        {
            if (IsScanning)
            {
                return;
            }

            await Task.Delay(600); // 防抖：MTP 常连续上报多个事件
            if (!IsDeviceConnected)
            {
                await RescanAsync();
            }
        }
        else if (IsDeviceConnected)
        {
            await ShowDisconnectedAsync();
        }
    }

    private async Task ShowDisconnectedAsync()
    {
        IsDeviceConnected = false;
        DeviceStatusText = Strings.Status_NotConnected;
        await DisposeSessionAsync();
        _scan = null;
        Wall = null;
        Grid = null;
        CloseLightbox();
        ShowWall = true;
        HintText = Strings.Hint_ConnectSwitch;
        HintVisible = true;
    }

    // ---------- 扫描 ----------
    [RelayCommand]
    private async Task RescanAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        IsScanning = true;
        ProgressIndeterminate = true;
        ProgressText = "";
        DeviceStatusText = Strings.Status_Connecting;
        HintVisible = false;

        try
        {
            var devices = await _provider.GetDevicesAsync(ct);
            var switchDevice = devices.FirstOrDefault(d => d.IsSwitch);
            if (switchDevice == null)
            {
                // Switch 2 等非标准名称设备兜底：根目录含 Album 文件夹即视为 Switch
                foreach (var device in devices.Where(d => !d.IsSwitch))
                {
                    try
                    {
                        var probeSession = await _provider.ConnectAsync(device, ct);
                        await using (probeSession)
                        {
                            var root = await probeSession.EnumerateAsync("\\", ct);
                            if (root.Any(e => e.IsDirectory && e.Name.Equals("Album", StringComparison.OrdinalIgnoreCase)))
                            {
                                switchDevice = device;
                                Log.Info($"按 Album 目录兜底识别 Switch: {device.FriendlyName} ({device.DeviceId})");
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"兜底探测 {device.FriendlyName} 失败: {ex.Message}");
                    }
                }
            }

            if (switchDevice == null)
            {
                Log.Info("未找到 Switch 设备（含兜底探测），进入未连接状态");
                await DisposeSessionAsync();
                _scan = null;
                await ShowDisconnectedAsync();
                return;
            }

            await DisposeSessionAsync();
            _session = await _provider.ConnectAsync(switchDevice, ct);
            _deviceKey = switchDevice.DeviceId;
            IsDeviceConnected = true;

            DeviceStatusText = Strings.Status_Scanning;
            _scan = await AlbumScanner.ScanAsync(_session, _titleDb, ct);
            DeviceStatusText = switchDevice.FriendlyName;

            if (_scan != null)
            {
                foreach (var item in _scan.AllItems)
                {
                    item.Saved = _tracker.GetState(item.GameTitle, item.FileName);
                }

                Wall = new GameWallViewModel(_scan, this);
                Grid = null;
                CloseLightbox();
                ShowWall = true;
                HintVisible = _scan.Games.Count == 0;
                HintText = Strings.Hint_EmptyAlbum;
            }
        }
        catch (OperationCanceledException)
        {
            // 重新扫描打断，静默
        }
        catch (Exception ex)
        {
            Log.Error("扫描失败", ex);
            await DisposeSessionAsync();
            _scan = null;
            await ShowDisconnectedAsync();
        }
        finally
        {
            IsScanning = false;
            ProgressIndeterminate = false;
        }
    }

    // ---------- 视图切换 ----------
    public void OpenGame(GameCardViewModel card)
    {
        if (_scan == null)
        {
            return;
        }

        IReadOnlyList<AlbumItem> items;
        if (card.IsAllCard || card.DevicePath == null)
        {
            items = _scan.AllItems;
        }
        else
        {
            items = _scan.ItemsByGame.TryGetValue(card.DevicePath, out var list)
                ? list
                : Array.Empty<AlbumItem>();
        }

        Grid = new GameGridViewModel(card.Title, items, this);
        ShowWall = false;
    }

    [RelayCommand]
    private void BackToWall()
    {
        Grid = null;
        CloseLightbox();
        ShowWall = true;
    }

    public void OpenLightbox(IReadOnlyList<PhotoItemViewModel> items, PhotoItemViewModel current)
    {
        var lightbox = new LightboxViewModel(items, current, LoadFullImageAsync, item => _ = OpenVideoAsync(item));
        lightbox.Closed += CloseLightbox;
        Lightbox = lightbox;
    }

    private void CloseLightbox()
    {
        Lightbox?.Cancel();
        Lightbox = null;
    }

    // ---------- 图片 / 视频 ----------
    public Task<string?> LoadThumbnailAsync(PhotoItemViewModel item)
    {
        if (_session == null || _deviceKey == null)
        {
            return Task.FromResult<string?>(null);
        }

        return _thumbnailService.GetThumbnailAsync(
            _deviceKey, _session, item.Item.DevicePath, item.Item.FileName,
            item.Item.LastModified, item.Item.Size, CancellationToken.None);
    }

    private Task<string?> LoadFullImageAsync(PhotoItemViewModel item)
    {
        if (_session == null || _deviceKey == null)
        {
            return Task.FromResult<string?>(null);
        }

        return _thumbnailService.GetFullImageAsync(
            _deviceKey, _session, item.Item.DevicePath, item.Item.FileName,
            item.Item.LastModified, item.Item.Size, CancellationToken.None);
    }

    public async Task OpenVideoAsync(PhotoItemViewModel item)
    {
        if (_session == null)
        {
            return;
        }

        var path = await LoadFullImageAsync(item);
        if (path == null)
        {
            ShowToast(Strings.Msg_SaveFailed);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            ShowToast(Strings.Lightbox_NoPreview);
        }
    }

    public Task<string?> ResolveCoverAsync(string? titleId, string title)
    {
        var urls = titleId != null ? _titleDb?.GetCoverUrls(titleId) : null;
        return _coverService.ResolveAsync(titleId, title, urls, CancellationToken.None);
    }

    // ---------- 保存到电脑 ----------
    [RelayCommand]
    private async Task SaveAllAsync()
    {
        if (_session == null || _scan == null || _scan.AllItems.Count == 0)
        {
            ShowToast(Strings.Hint_EmptyAlbum);
            return;
        }

        await SaveToPcCoreAsync(_scan.AllItems);
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        var selected = Grid?.SelectedItems();
        if (selected == null || selected.Count == 0)
        {
            ShowToast(Strings.Status_NoSelection);
            return;
        }

        await SaveToPcCoreAsync(selected);
    }

    private async Task SaveToPcCoreAsync(IReadOnlyList<AlbumItem> items)
    {
        if (_session == null)
        {
            return;
        }

        var targetDir = _settings.Current.SavePath;
        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch
        {
            ShowToast(Strings.Msg_PathInvalid);
            return;
        }

        _transferCts = new CancellationTokenSource();
        var ct = _transferCts.Token;
        IsTransferring = true;
        ProgressValue = 0;
        ProgressIndeterminate = false;

        var result = await _saveService.SaveToPcAsync(
            items, _session, targetDir, _settings.Current.AutoRename,
            new Progress<SaveProgress>(p => ReportSaveProgress(p, toPhone: false)), ct);

        if (result.Cancelled)
        {
            ShowToast(string.Format(Strings.Status_SavedPartial, result.SavedCount, items.Count));
        }
        else if (result.Failed.Count > 0 && result.SavedCount == 0)
        {
            ShowToast(string.Format(Strings.Msg_SaveFailed, result.Failed[0].Error));
        }
        else
        {
            ShowToast(string.Format(Strings.Status_SavedDone, result.SavedCount, result.SkippedCount, result.Failed.Count));
        }

        RefreshSavedStates();
        IsTransferring = false;
        _transferCts = null;
    }

    // ---------- 保存到手机 ----------
    [RelayCommand]
    private async Task SaveAllToPhoneAsync()
    {
        if (_session == null || _scan == null || _scan.AllItems.Count == 0)
        {
            ShowToast(Strings.Hint_EmptyAlbum);
            return;
        }

        await SaveToPhoneCoreAsync(_scan.AllItems);
    }

    [RelayCommand]
    private async Task SaveSelectedToPhoneAsync()
    {
        var selected = Grid?.SelectedItems();
        if (selected == null || selected.Count == 0)
        {
            ShowToast(Strings.Status_NoSelection);
            return;
        }

        await SaveToPhoneCoreAsync(selected);
    }

    private async Task SaveToPhoneCoreAsync(IReadOnlyList<AlbumItem> items)
    {
        if (_session == null)
        {
            return;
        }

        _transferCts = new CancellationTokenSource();
        var ct = _transferCts.Token;
        IsTransferring = true;
        ProgressValue = 0;
        ProgressIndeterminate = false;

        var phone = await ResolvePhoneSessionAsync(ct);
        if (phone == null)
        {
            IsTransferring = false;
            _transferCts = null;
            return;
        }

        await using (phone)
        {
            var result = await _phoneSaveService.SaveToPhoneAsync(
                items, _session, phone, _settings.Current.AutoRename,
                new Progress<SaveProgress>(p => ReportSaveProgress(p, toPhone: true)), ct);

            if (result.Cancelled)
            {
                ShowToast(string.Format(Strings.Status_SavedPartial, result.SavedCount, items.Count));
            }
            else if (result.Failed.Count > 0 && result.SavedCount == 0)
            {
                ShowToast(string.Format(Strings.Msg_SaveFailed, result.Failed[0].Error));
            }
            else
            {
                ShowToast(string.Format(Strings.Status_PhoneDone, result.SavedCount, result.SkippedCount));
            }

            RefreshSavedStates();
        }

        IsTransferring = false;
        _transferCts = null;
    }

    private async Task<IMediaDeviceSession?> ResolvePhoneSessionAsync(CancellationToken ct)
    {
        var devices = await _provider.GetDevicesAsync(ct);
        var phones = devices.Where(d => !d.IsSwitch).ToList();
        if (phones.Count == 0)
        {
            ShowToast(Strings.Status_NoPhone);
            return null;
        }

        MediaDeviceInfo? phone = null;
        var storedId = _settings.Current.PhoneDeviceId;
        if (!string.IsNullOrEmpty(storedId))
        {
            phone = phones.FirstOrDefault(p => p.DeviceId == storedId);
        }

        if (phone == null && phones.Count == 1)
        {
            phone = phones[0];
        }

        if (phone == null)
        {
            var dialog = new Views.DevicePickerDialog(phones) { Owner = _owner };
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            phone = dialog.SelectedDevice;
            if (phone != null)
            {
                _settings.Current.PhoneDeviceId = phone.DeviceId;
                _settings.Current.PhoneFriendlyName = phone.FriendlyName;
                _settings.Save();
            }
        }

        if (phone == null)
        {
            return null;
        }

        try
        {
            return await _provider.ConnectAsync(phone, ct);
        }
        catch
        {
            ShowToast(Strings.Status_NoPhone);
            return null;
        }
    }

    // ---------- 选择 ----------
    [RelayCommand]
    private void SelectAll() => Grid?.SetAllSelected(true);

    [RelayCommand]
    private void DeselectAll() => Grid?.SetAllSelected(false);

    // ---------- 设置 / 其他 ----------
    [RelayCommand]
    private void ChangeSavePath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Settings_PickFolder,
            InitialDirectory = Directory.Exists(_settings.Current.SavePath)
                ? _settings.Current.SavePath
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (_owner == null || dialog.ShowDialog(_owner) != true)
        {
            return;
        }

        _settings.Current.SavePath = dialog.FolderName;
        _settings.Save();
        SavePathText = _settings.Current.SavePath;
    }

    [RelayCommand]
    private void OpenSettingsAsync()
    {
        var viewModel = new SettingsViewModel(_settings, _theme, _provider);
        var dialog = new Views.SettingsDialog(viewModel) { Owner = _owner };
        dialog.ShowDialog();
        SavePathText = _settings.Current.SavePath;
    }

    [RelayCommand]
    private void CancelTransfer() => _transferCts?.Cancel();

    // ---------- 内部 ----------
    private void ReportSaveProgress(SaveProgress p, bool toPhone)
    {
        ProgressText = toPhone
            ? string.Format(Strings.Status_PhoneProgress, p.Done, p.Total, Path.GetFileName(p.CurrentFileName ?? ""))
            : string.Format(Strings.Status_Progress, p.Done, p.Total, Path.GetFileName(p.CurrentFileName ?? ""));
        ProgressValue = p.Total == 0 ? 0 : p.Done * 100.0 / p.Total;
    }

    private void RefreshSavedStates()
    {
        if (Wall != null)
        {
            foreach (var card in Wall.Cards)
            {
                card.RefreshSaved();
            }
        }

        Grid?.RefreshSaved();
    }

    public void ShowToast(string text)
    {
        ToastText = text;
        ToastVisible = true;
        var sequence = ++_toastSequence;
        Task.Run(async () =>
        {
            await Task.Delay(4200);
            _ui?.Post(_ =>
            {
                if (sequence == _toastSequence)
                {
                    ToastVisible = false;
                }
            }, null);
        });
    }
}
