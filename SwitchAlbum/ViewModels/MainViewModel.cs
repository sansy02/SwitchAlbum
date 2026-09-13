using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SwitchAlbum.Models;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.ViewModels;

/// <summary>顶栏三个紫色标签对应的页面。</summary>
public enum PageMode
{
    /// <summary>Switch 相册页（保存到此电脑）。</summary>
    Switch,

    /// <summary>本机相册页，保存到安卓手机（MTP）。</summary>
    LocalAndroid,

    /// <summary>本机相册页，iPhone 网页方案（扫码/局域网）。</summary>
    LocalIphone,
}

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly DuplicateTracker _tracker;
    private readonly SaveService _saveService;
    private readonly PhoneSaveService _phoneSaveService;
    private readonly ThumbnailService _thumbnailService;
    private readonly CoverService _coverService;
    private readonly ScanCacheService _scanCache;
    private readonly ITitleDb? _titleDb;
    private readonly SynchronizationContext? _ui;

    private IMediaProvider _provider;
    private IMediaDeviceSession? _session;
    private LocalFolderSession? _localSession;
    private ScanResult? _scan;
    private ScanResult? _localScan;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _transferCts;
    private Window? _owner;
    private string? _deviceKey;
    private int _toastSequence;
    private int _transferGeneration;

    public MainViewModel(
        SettingsService settings,
        ThemeService theme,
        DuplicateTracker tracker,
        SaveService saveService,
        PhoneSaveService phoneSaveService,
        ThumbnailService thumbnailService,
        CoverService coverService,
        ScanCacheService scanCache,
        ITitleDb? titleDb)
    {
        _settings = settings;
        _theme = theme;
        _tracker = tracker;
        _saveService = saveService;
        _phoneSaveService = phoneSaveService;
        _thumbnailService = thumbnailService;
        _coverService = coverService;
        _scanCache = scanCache;
        _titleDb = titleDb;
        _ui = SynchronizationContext.Current;

        SavePathText = settings.Current.SavePath;
        _provider = CreateProvider();
        _provider.DeviceChanged += OnDeviceChanged;
    }

    // ---------- 状态 ----------
    public AppSettings Settings => _settings.Current;

    /// <summary>内容区当前视图（Switch 墙/网格 或 本地相册墙/网格），视图切换触发过渡动画。</summary>
    public object? CurrentContent => PageMode == PageMode.Switch
        ? (ShowWall ? Wall : Grid)
        : (ShowLocalWall ? LocalWall : LocalGrid);

    /// <summary>本地相册页（安卓/苹果共用同一份本地扫描数据）。</summary>
    private bool IsLocalPage => PageMode != PageMode.Switch;

    /// <summary>供顶栏三标签的样式触发器使用。</summary>
    public bool IsSwitchPage => PageMode == PageMode.Switch;
    public bool IsAndroidPage => PageMode == PageMode.LocalAndroid;
    public bool IsIphonePage => PageMode == PageMode.LocalIphone;

    partial void OnPageModeChanged(PageMode value)
    {
        OnPropertyChanged(nameof(CurrentContent));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(HintVisible));
        OnPropertyChanged(nameof(IsSwitchPage));
        OnPropertyChanged(nameof(IsAndroidPage));
        OnPropertyChanged(nameof(IsIphonePage));
    }

    partial void OnShowLocalWallChanged(bool value) => OnPropertyChanged(nameof(CurrentContent));
    partial void OnShowWallChanged(bool value) => OnPropertyChanged(nameof(CurrentContent));
    partial void OnWallChanged(GameWallViewModel? value) => OnPropertyChanged(nameof(CurrentContent));
    partial void OnGridChanged(GameGridViewModel? value) => OnPropertyChanged(nameof(CurrentContent));
    partial void OnLocalWallChanged(GameWallViewModel? value) => OnPropertyChanged(nameof(CurrentContent));
    partial void OnLocalGridChanged(GameGridViewModel? value) => OnPropertyChanged(nameof(CurrentContent));

    private ScanResult? ActiveScan => IsLocalPage ? _localScan : _scan;
    private IMediaDeviceSession? ActiveSession => IsLocalPage ? _localSession : _session;
    private string? ActiveDeviceKey => IsLocalPage ? "local" : _deviceKey;
    private GameGridViewModel? ActiveGrid => IsLocalPage ? LocalGrid : Grid;
    private GameWallViewModel? ActiveWall => IsLocalPage ? LocalWall : Wall;

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
    [ObservableProperty] private PageMode _pageMode = PageMode.Switch;
    [ObservableProperty] private bool _showLocalWall = true;
    [ObservableProperty] private GameWallViewModel? _localWall;
    [ObservableProperty] private GameGridViewModel? _localGrid;
    [ObservableProperty] private string _iphoneUrl = "";
    [ObservableProperty] private bool _isIphoneServerRunning;
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
        await DisposeLocalSessionAsync();
        _iphoneServer?.Dispose();
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

    private async Task DisposeLocalSessionAsync()
    {
        var session = Interlocked.Exchange(ref _localSession, null);
        if (session != null)
        {
            await session.DisposeAsync();
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

        // 本地相册页浏览中不受 Switch 断开影响
        if (!IsLocalPage)
        {
            HintText = Strings.Hint_ConnectSwitch;
            HintVisible = true;
        }
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

            // 先显示本地扫描缓存（秒开），后台真实扫描刷新
            var cached = _scanCache.Load(switchDevice.DeviceId);
            if (cached != null)
            {
                ApplyScanResult(cached);
                Log.Info("已加载扫描缓存（" + cached.Games.Count + " 个游戏），后台刷新中");
            }

            DeviceStatusText = Strings.Status_Scanning;
            _scan = await AlbumScanner.ScanAsync(_session, _titleDb, ct);
            DeviceStatusText = switchDevice.FriendlyName;

            if (_scan != null)
            {
                ApplyScanResult(_scan);
                _scanCache.Save(switchDevice.DeviceId, _scan);
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

    /// <summary>把扫描结果应用到界面（缓存与真实扫描共用）。</summary>
    private void ApplyScanResult(ScanResult scan)
    {
        _scan = scan;
        foreach (var item in scan.AllItems)
        {
            item.Saved = _tracker.GetState(item.GameTitle, item.FileName);
        }

        Wall = new GameWallViewModel(scan, this);
        Grid = null;
        CloseLightbox();
        ShowWall = true;
        HintVisible = scan.Games.Count == 0;
        HintText = Strings.Hint_EmptyAlbum;
    }

    // ---------- 视图切换 ----------
    public void OpenGame(GameCardViewModel card)
    {
        var scan = ActiveScan;
        if (scan == null)
        {
            return;
        }

        IReadOnlyList<AlbumItem> items;
        if (card.IsAllCard || card.DevicePath == null)
        {
            items = scan.AllItems;
        }
        else
        {
            items = scan.ItemsByGame.TryGetValue(card.DevicePath, out var list)
                ? list
                : Array.Empty<AlbumItem>();
        }

        if (IsLocalPage)
        {
            LocalGrid = new LocalGameGridViewModel(card.Title, items, this);
            ShowLocalWall = false;
        }
        else
        {
            Grid = new GameGridViewModel(card.Title, items, this);
            ShowWall = false;
        }
    }

    [RelayCommand]
    private void BackToWall()
    {
        CloseLightbox();
        if (IsLocalPage)
        {
            LocalGrid = null;
            ShowLocalWall = true;
        }
        else
        {
            Grid = null;
            ShowWall = true;
        }
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
        var session = ActiveSession;
        var deviceKey = ActiveDeviceKey;
        if (session == null || deviceKey == null)
        {
            return Task.FromResult<string?>(null);
        }

        return _thumbnailService.GetThumbnailAsync(
            deviceKey, session, item.Item.DevicePath, item.Item.FileName,
            item.Item.LastModified, item.Item.Size, CancellationToken.None);
    }

    private Task<string?> LoadFullImageAsync(PhotoItemViewModel item)
    {
        var session = ActiveSession;
        var deviceKey = ActiveDeviceKey;
        if (session == null || deviceKey == null)
        {
            return Task.FromResult<string?>(null);
        }

        return _thumbnailService.GetFullImageAsync(
            deviceKey, session, item.Item.DevicePath, item.Item.FileName,
            item.Item.LastModified, item.Item.Size, CancellationToken.None);
    }

    public async Task OpenVideoAsync(PhotoItemViewModel item)
    {
        if (ActiveSession == null)
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
        var generation = ++_transferGeneration;
        ResetSpeed();
        IsTransferring = true;
        ProgressValue = 0;
        ProgressIndeterminate = false;

        var result = await _saveService.SaveToPcAsync(
            items, _session, targetDir, _settings.Current.AutoRename,
            new Progress<SaveProgress>(p => ReportSaveProgress(p, toPhone: false, generation)), ct);

        RefreshSavedStates();
        IsTransferring = false;
        _transferCts = null;
        SetSaveOutcomeText(result, toPhone: false, items.Count);

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
    }

    // ---------- 顶栏页面切换标签 ----------
    /// <summary>
    /// 「保存到此电脑」标签：切回 Switch 相册页。三个标签逻辑一致——
    /// 点击非当前页标签即切换页面，当前页标签保持激活态（紫底白字）。
    /// </summary>
    [RelayCommand]
    private void SaveToPc()
    {
        if (IsLocalPage)
        {
            BackToSwitch();
        }
    }

    // ---------- 本地相册页（保存到手机：安卓 / 苹果共用浏览数据） ----------
    [RelayCommand]
    private async Task OpenLocalAlbumAsync() => await OpenLocalPageAsync(PageMode.LocalAndroid);

    [RelayCommand]
    private async Task OpenIphonePageAsync() => await OpenLocalPageAsync(PageMode.LocalIphone);

    private async Task OpenLocalPageAsync(PageMode target)
    {
        var savePath = _settings.Current.SavePath;
        if (string.IsNullOrWhiteSpace(savePath) || !Directory.Exists(savePath))
        {
            ShowToast(Strings.Msg_PathInvalid);
            return;
        }

        await DisposeLocalSessionAsync();
        _localSession = new LocalFolderSession(savePath);

        IsScanning = true;
        ProgressIndeterminate = true;
        ProgressText = "";
        try
        {
            _localScan = await AlbumScanner.ScanAsync(_localSession, _titleDb, CancellationToken.None);
        }
        finally
        {
            IsScanning = false;
            ProgressIndeterminate = false;
        }

        if (target == PageMode.LocalIphone)
        {
            EnsureIphoneServer();
        }

        if (_localScan == null || _localScan.AllItems.Count == 0)
        {
            LocalWall = null;
            LocalGrid = null;
            CloseLightbox();
            ShowLocalWall = true;
            HintText = Strings.Local_EmptyHint;
            HintVisible = true;
            PageMode = target;
            return;
        }

        foreach (var item in _localScan.AllItems)
        {
            item.Saved = _tracker.GetState(item.GameTitle, item.FileName);
        }

        LocalWall = new GameWallViewModel(_localScan, this);
        LocalGrid = null;
        CloseLightbox();
        ShowLocalWall = true;
        HintVisible = false;
        PageMode = target;
    }

    /// <summary>返回 Switch 相册页（顶栏「保存到此电脑」标签触发）。</summary>
    private void BackToSwitch()
    {
        PageMode = PageMode.Switch;
        CloseLightbox();
        ShowWall = Wall != null;
        if (Wall == null)
        {
            HintText = Strings.Hint_ConnectSwitch;
            HintVisible = true;
        }
        else
        {
            HintVisible = false;
        }
    }

    // ---------- iPhone 网页方案（零安装：扫码 → 浏览器 → ZIP 批量） ----------
    private IphoneWebServer? _iphoneServer;

    private void EnsureIphoneServer()
    {
        if (_iphoneServer != null && _iphoneServer.IsRunning)
        {
            IphoneUrl = _iphoneServer.Url;
            IsIphoneServerRunning = true;
            return;
        }

        try
        {
            _iphoneServer?.Dispose();
            _iphoneServer = new IphoneWebServer(_settings.Current.SavePath);
            _iphoneServer.Start();
            IphoneUrl = _iphoneServer.Url;
            IsIphoneServerRunning = true;
            Log.Info($"iPhone 网页服务已启动: {_iphoneServer.Url}");
        }
        catch (Exception ex)
        {
            Log.Error("iPhone 网页服务启动失败", ex);
            IsIphoneServerRunning = false;
            ShowToast(Strings.Iphone_ServerFailed);
        }
    }

    [RelayCommand]
    private void OpenIphoneEntry()
    {
        if (_iphoneServer == null || !_iphoneServer.IsRunning)
        {
            EnsureIphoneServer();
            if (_iphoneServer == null || !_iphoneServer.IsRunning)
            {
                return;
            }
        }

        var dialog = new Views.IphoneDialog(_iphoneServer.Url) { Owner = _owner };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private async Task LocalSaveAllToPhoneAsync()
    {
        if (PageMode != PageMode.LocalAndroid)
        {
            return;
        }

        if (_localSession == null || _localScan == null || _localScan.AllItems.Count == 0)
        {
            ShowToast(Strings.Local_EmptyHint);
            return;
        }

        await SaveLocalToPhoneCoreAsync(_localScan.AllItems);
    }

    [RelayCommand]
    private async Task LocalSaveSelectedToPhoneAsync()
    {
        if (PageMode != PageMode.LocalAndroid)
        {
            return;
        }

        var selected = LocalGrid?.SelectedItems();
        if (selected == null || selected.Count == 0)
        {
            ShowToast(Strings.Status_NoSelection);
            return;
        }

        await SaveLocalToPhoneCoreAsync(selected);
    }

    private async Task SaveLocalToPhoneCoreAsync(IReadOnlyList<AlbumItem> items)
    {
        if (_localSession == null)
        {
            return;
        }

        // 先解析手机设备再进入传输状态：未连手机时只有提示，不闪进度条
        var phone = await ResolvePhoneSessionAsync(CancellationToken.None);
        if (phone == null)
        {
            ProgressText = Strings.Status_NoPhone;
            return;
        }

        _transferCts = new CancellationTokenSource();
        var ct = _transferCts.Token;
        var generation = ++_transferGeneration;
        ResetSpeed();
        IsTransferring = true;
        ProgressValue = 0;
        ProgressIndeterminate = false;

        await using (phone)
        {
            var result = await _phoneSaveService.SaveToPhoneAsync(
                items, _localSession, phone, _settings.Current.AutoRename,
                new Progress<SaveProgress>(p => ReportSaveProgress(p, toPhone: true, generation)), ct);

            RefreshSavedStates();
            IsTransferring = false;
            _transferCts = null;
            SetSaveOutcomeText(result, toPhone: true, items.Count);

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
        }
    }

    private static bool IsAppleDeviceName(string name)
        => name.Contains("iphone", StringComparison.OrdinalIgnoreCase)
           || name.Contains("ipad", StringComparison.OrdinalIgnoreCase)
           || name.Contains("apple", StringComparison.OrdinalIgnoreCase);

    private async Task<IMediaDeviceSession?> ResolvePhoneSessionAsync(CancellationToken ct)
    {
        // iPhone 走 PTP 协议、不会出现在 MTP 候选列表里，这里单独提示苹果平台限制
        var allDevices = await _provider.GetDevicesAsync(ct);
        if (allDevices.Any(d => !d.IsSwitch && IsAppleDeviceName(d.FriendlyName)))
        {
            ShowToast(Strings.Phone_AppleUnsupported);
            return null;
        }

        // 仅 MTP 协议设备（安卓手机）为候选；MSC 硬盘/U 盘被过滤
        var phones = (await _provider.GetPhoneCandidateDevicesAsync(ct)).ToList();
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
                ShowToast(Strings.Status_NoPhone);
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
    private void SelectAll() => ActiveGrid?.SetAllSelected(true);

    [RelayCommand]
    private void DeselectAll() => ActiveGrid?.SetAllSelected(false);

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
    private long _speedLastBytes;
    private long _speedLastTicks;
    private double _speedMbs;

    private void ResetSpeed()
    {
        _speedLastBytes = 0;
        _speedLastTicks = 0;
        _speedMbs = 0;
    }

    private void ReportSaveProgress(SaveProgress p, bool toPhone, int generation)
    {
        // 进度回调经 dispatcher 异步投递，可能在传输已结束后才到达；
        // 此时若仍写 ProgressText，会把完成文字刷回「正在保存 0/N」。按代次丢弃滞后回调。
        if (!IsTransferring || generation != _transferGeneration)
        {
            return;
        }

        var text = toPhone
            ? string.Format(Strings.Status_PhoneProgress, p.Done, p.Total, Path.GetFileName(p.CurrentFileName ?? ""))
            : string.Format(Strings.Status_Progress, p.Done, p.Total, Path.GetFileName(p.CurrentFileName ?? ""));

        // 约 1 秒滑动窗口计算传输速度
        var now = Stopwatch.GetTimestamp();
        if (_speedLastTicks != 0)
        {
            var elapsed = (double)(now - _speedLastTicks) / Stopwatch.Frequency;
            if (elapsed >= 1.0)
            {
                _speedMbs = (p.BytesDone - _speedLastBytes) / elapsed / (1024.0 * 1024.0);
                _speedLastBytes = p.BytesDone;
                _speedLastTicks = now;
            }
        }
        else
        {
            _speedLastBytes = p.BytesDone;
            _speedLastTicks = now;
        }

        if (_speedMbs > 0 && p.Done < p.Total)
        {
            text += string.Format(Strings.Status_SpeedSuffix, _speedMbs.ToString("F1"));
        }

        ProgressText = text;
        ProgressValue = p.Total == 0 ? 0 : p.Done * 100.0 / p.Total;
    }

    /// <summary>传输结束后，把结果写到状态栏进度文字所在位置（toast 自动消失，这里留下持久反馈）。</summary>
    private void SetSaveOutcomeText(SaveResult result, bool toPhone, int total)
    {
        if (result.Cancelled)
        {
            ProgressText = string.Format(Strings.Status_SavedPartial, result.SavedCount, total);
        }
        else if (result.SavedCount == 0 && result.Failed.Count > 0)
        {
            ProgressText = Strings.Status_SaveFailedShort;
        }
        else if (result.SavedCount == 0 && result.SkippedCount > 0)
        {
            ProgressText = Strings.Status_AllSkipped;
        }
        else
        {
            ProgressText = toPhone
                ? string.Format(Strings.Status_PhoneSuccessCount, result.SavedCount)
                : string.Format(Strings.Status_SaveSuccessCount, result.SavedCount);
        }

        ProgressValue = 100;
    }

    private void RefreshSavedStates()
    {
        if (ActiveWall != null)
        {
            foreach (var card in ActiveWall.Cards)
            {
                card.RefreshSaved();
            }
        }

        ActiveGrid?.RefreshSaved();
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
