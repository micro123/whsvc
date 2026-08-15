using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Streams;
using WallhavenService.Models;
using WallhavenService.Services;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Microsoft.UI.Xaml.Input;
using System.Text;

namespace WallhavenService;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private readonly WallpaperOrchestrator _orchestrator;
    private readonly NotificationService _notificationService;
    private readonly TrayIconService _trayIcon;
    private readonly Func<Task> _shutdownAsync;
    private readonly HttpClient _previewClient = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _countdownTimer;
    private CancellationTokenSource? _previewCancellation;
    private ImagePreviewWindow? _imagePreviewWindow;
    private readonly StringBuilder _logBuffer = new();
    private LogWindow? _logWindow;
    private WallpaperItem? _displayedWallpaper;
    private bool _allowClose;
    private bool _started;

    public MainWindow(WallpaperOrchestrator orchestrator, Func<Task> shutdownAsync)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
        _shutdownAsync = shutdownAsync;
        _notificationService = new NotificationService(DispatcherQueue);
        _trayIcon = new TrayIconService();
        _countdownTimer = DispatcherQueue.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => UpdateRotationCountdown();
        _countdownTimer.Start();
        ConfigureWindow();
        SubscribeEvents();
        LoadSettings();
        UpdateStatus("等待任务");
        RootGrid.Loaded += MainWindow_OnLoaded;
    }

    private void ConfigureWindow()
    {
        Title = "Wallhaven 壁纸服务";
        SystemBackdrop = new MicaBackdrop();

        // AppWindow.Resize 使用物理像素，XAML 布局使用有效像素。
        // 按当前 DPI 缩放换算，避免高 DPI 下窗口看起来过小并裁剪布局。
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        var scale = dpi > 0 ? dpi / 96d : 1d;
        AppWindow.Resize(ToPhysicalSize(1440, 900, scale));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ToPhysicalPixels(1320, scale);
            presenter.PreferredMinimumHeight = ToPhysicalPixels(820, scale);
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        AppWindow.Closing += AppWindow_OnClosing;
        CenterWindow();
    }

    private static SizeInt32 ToPhysicalSize(double width, double height, double scale) =>
        new(ToPhysicalPixels(width, scale), ToPhysicalPixels(height, scale));

    private static int ToPhysicalPixels(double value, double scale) =>
        (int)Math.Round(value * scale);


    private void CenterWindow()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
            return;

        var workArea = displayArea.WorkArea;
        var x = workArea.X + Math.Max(0, (workArea.Width - AppWindow.Size.Width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - AppWindow.Size.Height) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void SubscribeEvents()
    {
        _orchestrator.StatusChanged += Orchestrator_OnStatusChanged;
        _orchestrator.SearchStarting += Orchestrator_OnSearchStarting;
        _orchestrator.NextSearchKeywordConsumed += Orchestrator_OnNextSearchKeywordConsumed;
        _orchestrator.WallpaperUpdated += Orchestrator_OnWallpaperUpdated;
        _orchestrator.WallpaperRestored += Orchestrator_OnWallpaperRestored;
        _orchestrator.SearchRetryScheduled += Orchestrator_OnSearchRetryScheduled;
        _orchestrator.SearchFailed += Orchestrator_OnSearchFailed;
        _orchestrator.AutoRotationDisabled += Orchestrator_OnAutoRotationDisabled;

        _trayIcon.OpenRequested += (_, _) => Enqueue(ShowFromTray);
        _trayIcon.RunNowRequested += (_, _) => Enqueue(async () => await RunAsync());
        _trayIcon.SaveCurrentRequested += (_, _) => Enqueue(SaveCurrentWallpaper);
        _trayIcon.ExitRequested += (_, _) => Enqueue(async () => await ExitApplicationAsync());
        _trayIcon.SetSaveEnabled(_orchestrator.HasCurrentWallpaper);
    }

    private void LoadSettings()
    {
        var settings = _orchestrator.Settings;
        ApiKeyBox.Password = settings.ApiKey;
        KeywordsBox.Text = string.Join(Environment.NewLine, settings.SearchKeywords);
        SfwBox.IsChecked = settings.IncludeSfw;
        SketchyBox.IsChecked = settings.IncludeSketchy;
        NsfwBox.IsChecked = settings.IncludeNsfw;
        GeneralBox.IsChecked = settings.IncludeGeneral;
        AnimeBox.IsChecked = settings.IncludeAnime;
        PeopleBox.IsChecked = settings.IncludePeople;
        MinimumResolutionBox.Text = settings.MinimumResolution;
        SelectAspectRatio(settings.AspectRatio);
        ScheduleEnabledBox.IsChecked = settings.ScheduleEnabled;
        IntervalBox.Text = settings.IntervalMinutes.ToString();
        UpdateNsfwAvailability();
        UpdateRotationCountdown();
    }

    private void SelectAspectRatio(string aspectRatio)
    {
        for (var index = 0; index < AspectRatioBox.Items.Count; index++)
        {
            if (AspectRatioBox.Items[index] is ComboBoxItem item &&
                string.Equals(item.Tag as string, aspectRatio, StringComparison.Ordinal))
            {
                AspectRatioBox.SelectedIndex = index;
                return;
            }
        }

        AspectRatioBox.SelectedIndex = 0;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
            return;

        _started = true;
        await Task.Run(() => _orchestrator.StartAsync());
        UpdateRotationCountdown();
    }

    private async void RunButton_OnClick(object sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync()
    {
        if (!await SaveSettingsAsync(false))
            return;

        RunButton.IsEnabled = false;
        try
        {
            await Task.Run(() => _orchestrator.RunNowAsync());
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e) => await SaveSettingsAsync(true);

    private async Task<bool> SaveSettingsAsync(bool showMessage)
    {
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 1)
        {
            await ShowValidationMessageAsync("间隔必须是大于 0 的整数分钟。");
            return false;
        }

        if (SfwBox.IsChecked != true && SketchyBox.IsChecked != true && NsfwBox.IsChecked != true)
        {
            await ShowValidationMessageAsync("请至少选择一种内容纯度。");
            return false;
        }

        if (GeneralBox.IsChecked != true && AnimeBox.IsChecked != true && PeopleBox.IsChecked != true)
        {
            await ShowValidationMessageAsync("请至少选择一种壁纸分类。");
            return false;
        }

        var settings = new AppSettings
        {
            ApiKey = ApiKeyBox.Password.Trim(),
            SearchKeywords = KeywordsBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IncludeSfw = SfwBox.IsChecked == true,
            IncludeSketchy = SketchyBox.IsChecked == true,
            IncludeNsfw = NsfwBox.IsChecked == true && !string.IsNullOrWhiteSpace(ApiKeyBox.Password),
            IncludeGeneral = GeneralBox.IsChecked == true,
            IncludeAnime = AnimeBox.IsChecked == true,
            IncludePeople = PeopleBox.IsChecked == true,
            MinimumResolution = MinimumResolutionBox.Text.Trim(),
            AspectRatio = (AspectRatioBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty,
            ScheduleEnabled = ScheduleEnabledBox.IsChecked == true,
            IntervalMinutes = interval
        };

        _orchestrator.UpdateSettings(settings);
        _orchestrator.SetNextSearchKeyword(NextSearchKeywordBox.Text);
        UpdateRotationCountdown();
        if (showMessage)
            UpdateStatus("设置已保存，定时任务已应用");
        return true;
    }

    private async Task ShowValidationMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "设置无效",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e) => UpdateNsfwAvailability();

    private void UpdateNsfwAvailability()
    {
        var hasApiKey = !string.IsNullOrWhiteSpace(ApiKeyBox.Password);
        NsfwBox.IsEnabled = hasApiKey;
        if (!hasApiKey)
            NsfwBox.IsChecked = false;
        NsfwHintText.Text = hasApiKey ? "API Key 已配置" : "无 API Key 时禁用 NSFW";
    }

    private void SaveCurrentWallpaper()
    {
        try
        {
            var savedPath = _orchestrator.SaveCurrentWallpaper();
            if (savedPath is not null)
                UpdateStatus($"已保存到图片目录：{Path.GetFileName(savedPath)}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"保存失败：{ex.Message}");
        }
    }

    private void Orchestrator_OnStatusChanged(object? sender, string status) =>
        Enqueue(() =>
        {
            UpdateStatus(status);
            var canSave = _orchestrator.HasCurrentWallpaper;
            _trayIcon.SetSaveEnabled(canSave);
            SaveImageButton.IsEnabled = canSave;
            _trayIcon.SetToolTip(status.StartsWith("抓取失败", StringComparison.Ordinal)
                ? "Wallhaven 壁纸服务 - 抓取失败"
                : "Wallhaven 壁纸服务");
        });

    private void Orchestrator_OnSearchStarting(object? sender, string searchTag)
    {
        try
        {
            _notificationService.ShowSearchStarting(searchTag);
        }
        catch (Exception ex)
        {
            Enqueue(() => UpdateStatus($"搜索通知发送失败：{ex.Message}"));
        }
    }

    private void Orchestrator_OnNextSearchKeywordConsumed(object? sender, EventArgs e) =>
        Enqueue(() => NextSearchKeywordBox.Text = string.Empty);

    private void Orchestrator_OnWallpaperUpdated(object? sender, WallpaperItem wallpaper)
    {
        try
        {
            _notificationService.ShowSearchResult(wallpaper);
        }
        catch (Exception ex)
        {
            Enqueue(() => UpdateStatus($"结果通知发送失败：{ex.Message}"));
        }

        Enqueue(() =>
        {
            UpdateWallpaperDetails(wallpaper);
            _trayIcon.SetSaveEnabled(true);
        });
        _ = LoadWallpaperPreviewAsync(wallpaper, "壁纸预览加载失败");
    }

    private void Orchestrator_OnWallpaperRestored(object? sender, WallpaperItem wallpaper)
    {
        Enqueue(() =>
        {
            UpdateWallpaperDetails(wallpaper);
            _trayIcon.SetSaveEnabled(true);
        });
        _ = LoadWallpaperPreviewAsync(wallpaper, "历史壁纸预览加载失败");
    }

    private void Orchestrator_OnSearchFailed(object? sender, SearchFailure failure)
    {
        try
        {
            _notificationService.ShowSearchFailure(failure);
        }
        catch (Exception ex)
        {
            Enqueue(() => UpdateStatus($"失败通知发送失败：{ex.Message}"));
        }
    }

    private void Orchestrator_OnSearchRetryScheduled(object? sender, SearchRetry retry)
    {
        try
        {
            _notificationService.ShowSearchRetry(retry);
        }
        catch (Exception ex)
        {
            Enqueue(() => UpdateStatus($"重试通知发送失败：{ex.Message}"));
        }
    }

    private void Orchestrator_OnAutoRotationDisabled(object? sender, EventArgs e) =>
        Enqueue(() =>
        {
            ScheduleEnabledBox.IsChecked = false;
            UpdateRotationCountdown();
        });

    private void UpdateRotationCountdown()
    {
        if (!_orchestrator.Settings.ScheduleEnabled)
        {
            RotationCountdownPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RotationCountdownPanel.Visibility = Visibility.Visible;
        var nextRotation = _orchestrator.NextRotationUtc;
        if (nextRotation is null)
        {
            RotationCountdownText.Text = "正在准备…";
            return;
        }

        var remaining = nextRotation.Value - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        RotationCountdownText.Text = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays} 天 {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private void UpdateWallpaperDetails(WallpaperItem wallpaper)
    {
        _displayedWallpaper = wallpaper;
        CurrentTagText.Text = wallpaper.SearchTag;
        CurrentIdText.Text = wallpaper.Id;
        CurrentResolutionText.Text = wallpaper.Resolution;
        CurrentPurityText.Text = wallpaper.Purity.ToUpperInvariant();
        CurrentPurityText.Foreground = wallpaper.Purity.ToLowerInvariant() switch
        {
            "sfw" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 138, 75)),
            "sketchy" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 190, 112, 0)),
            "nsfw" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 201, 48, 44)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 97, 97, 97))
        };
        CurrentPurityText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        CurrentWallpaperImage.Source = null;
        WallpaperEmptyText.Text = "正在加载预览…";
        WallpaperEmptyText.Visibility = Visibility.Visible;
        SaveImageButton.IsEnabled = _orchestrator.HasCurrentWallpaper;
        CopyUrlButton.IsEnabled = true;
        OpenUrlButton.IsEnabled = true;
    }

    private async Task LoadWallpaperPreviewAsync(WallpaperItem wallpaper, string failurePrefix)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _previewCancellation, cancellation);
        previous?.Cancel();

        try
        {
            var imageBytes = await _previewClient
                .GetByteArrayAsync(wallpaper.ThumbnailUrl, cancellation.Token)
                .ConfigureAwait(false);

            await EnqueueAsync(async () =>
            {
                if (cancellation.IsCancellationRequested || _displayedWallpaper?.Id != wallpaper.Id)
                    return;

                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteBytes(imageBytes);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }

                stream.Seek(0);
                var image = new BitmapImage
                {
                    DecodePixelWidth = 480
                };
                await image.SetSourceAsync(stream);

                if (cancellation.IsCancellationRequested || _displayedWallpaper?.Id != wallpaper.Id)
                    return;

                CurrentWallpaperImage.Source = image;
                WallpaperEmptyText.Visibility = Visibility.Collapsed;
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Enqueue(() =>
            {
                if (_displayedWallpaper?.Id == wallpaper.Id)
                {
                    WallpaperEmptyText.Text = "预览加载失败";
                    WallpaperEmptyText.Visibility = Visibility.Visible;
                }
                UpdateStatus($"{failurePrefix}：{ex.Message}");
            });
        }
        finally
        {
            Interlocked.CompareExchange(ref _previewCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void OpenLogButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_logWindow is not null)
        {
            _logWindow.Activate();
            return;
        }

        var logWindow = new LogWindow(this);
        _logWindow = logWindow;
        logWindow.SetLogText(_logBuffer.ToString());
        logWindow.Closed += (_, _) => _logWindow = null;
        logWindow.Activate();
    }

    private void PreviewBorder_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_displayedWallpaper is null || CurrentWallpaperImage.Source is null)
            return;

        if (_imagePreviewWindow is not null)
        {
            _imagePreviewWindow.Activate();
            return;
        }

        var previewWindow = new ImagePreviewWindow(_displayedWallpaper, this, _orchestrator.CurrentWallpaperPath);
        _imagePreviewWindow = previewWindow;
        previewWindow.Closed += (_, _) => _imagePreviewWindow = null;
        previewWindow.Activate();
    }

    private void SaveImageButton_OnClick(object sender, RoutedEventArgs e) => SaveCurrentWallpaper();

    private void CopyUrlButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_displayedWallpaper is null)
            return;

        var package = new DataPackage();
        package.SetText(_displayedWallpaper.SourceUrl);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        UpdateStatus("壁纸页面 URL 已复制");
    }

    private void OpenUrlButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_displayedWallpaper is null)
            return;

        Process.Start(new ProcessStartInfo(_displayedWallpaper.SourceUrl) { UseShellExecute = true });
    }

    private void UpdateStatus(string status)
    {
        StatusText.Text = status;
        _logBuffer.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {status}");
        _logWindow?.SetLogText(_logBuffer.ToString());
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Restore();
        Activate();
    }

    private void AppWindow_OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
            return;

        args.Cancel = true;
        AppWindow.Hide();
        UpdateStatus("窗口已隐藏，程序继续在系统托盘运行");
    }

    private async Task ExitApplicationAsync()
    {
        if (_allowClose)
            return;

        _allowClose = true;
        _countdownTimer.Stop();
        _previewCancellation?.Cancel();
        _imagePreviewWindow?.Close();
        _logWindow?.Close();
        _logWindow = null;
        _imagePreviewWindow = null;
        _previewClient.Dispose();
        _trayIcon.Dispose();
        _notificationService.Dispose();
        await _shutdownAsync();
        Close();
    }

    private void Enqueue(Action action)
    {
        DispatcherQueue.TryEnqueue(() => action());
    }

    private void Enqueue(Func<Task> action)
    {
        DispatcherQueue.TryEnqueue(async () => await action());
    }

    private Task EnqueueAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetCanceled();
        }

        return completion.Task;
    }

    public void ReportUnhandledException(Exception exception) =>
        Enqueue(() => UpdateStatus($"未处理错误：{exception.Message}"));
}
