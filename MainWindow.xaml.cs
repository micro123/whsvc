using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using WallhavenService.Models;
using WallhavenService.Services;

namespace WallhavenService;

public partial class MainWindow : Window
{
    private readonly WallpaperOrchestrator _orchestrator;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _saveCurrentMenuItem;
    private readonly NotificationService _notificationService;
    private WallpaperItem? _displayedWallpaper;
    private bool _allowClose;

    public MainWindow(WallpaperOrchestrator orchestrator)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
        _orchestrator.StatusChanged += Orchestrator_OnStatusChanged;
        _orchestrator.SearchStarting += Orchestrator_OnSearchStarting;
        _orchestrator.NextSearchKeywordConsumed += Orchestrator_OnNextSearchKeywordConsumed;
        _orchestrator.WallpaperUpdated += Orchestrator_OnWallpaperUpdated;
        _orchestrator.WallpaperRestored += Orchestrator_OnWallpaperRestored;
        _orchestrator.SearchFailed += Orchestrator_OnSearchFailed;
        _orchestrator.AutoRotationDisabled += Orchestrator_OnAutoRotationDisabled;
        _notificationService = new NotificationService();
        _saveCurrentMenuItem = new Forms.ToolStripMenuItem("保存当前图片")
        {
            Enabled = _orchestrator.HasCurrentWallpaper
        };
        _saveCurrentMenuItem.Click += (_, _) => SaveCurrentWallpaper();

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Wallhaven 壁纸服务",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = CreateTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        LoadSettings();
        UpdateStatus("等待任务");
        Loaded += MainWindow_OnLoaded;
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowFromTray());
        menu.Items.Add("立即抓取", null, async (_, _) => await RunAsync());
        menu.Items.Add(_saveCurrentMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        return menu;
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
        AspectRatioBox.SelectedItem = AspectRatioBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, settings.AspectRatio, StringComparison.Ordinal))
            ?? AspectRatioBox.Items[0];
        ScheduleEnabledBox.IsChecked = settings.ScheduleEnabled;
        IntervalBox.Text = settings.IntervalMinutes.ToString();
        UpdateNsfwAvailability();
    }

    private async void RunButton_OnClick(object sender, RoutedEventArgs e) => await RunAsync();

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _orchestrator.StartAsync();
    }

    private async Task RunAsync()
    {
        if (!SaveSettings(false))
            return;

        RunButton.IsEnabled = false;
        try
        {
            await _orchestrator.RunNowAsync();
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e) => SaveSettings(true);

    private bool SaveSettings(bool showMessage)
    {
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 1)
        {
            System.Windows.MessageBox.Show("间隔必须是大于 0 的整数分钟。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (SfwBox.IsChecked != true && SketchyBox.IsChecked != true && NsfwBox.IsChecked != true)
        {
            System.Windows.MessageBox.Show("请至少选择一种内容纯度。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (GeneralBox.IsChecked != true && AnimeBox.IsChecked != true && PeopleBox.IsChecked != true)
        {
            System.Windows.MessageBox.Show("请至少选择一种壁纸分类。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (showMessage)
        {
            UpdateStatus("设置已保存，定时任务已应用");
        }
        return true;
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
                _trayIcon.ShowBalloonTip(2500, "Wallhaven 壁纸服务", $"已保存到图片目录：{Path.GetFileName(savedPath)}", Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            UpdateStatus($"保存失败：{ex.Message}");
            _trayIcon.ShowBalloonTip(3500, "Wallhaven 壁纸服务", $"保存失败：{ex.Message}", Forms.ToolTipIcon.Error);
        }
    }

    private void Orchestrator_OnStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateStatus(status);
            _saveCurrentMenuItem.Enabled = _orchestrator.HasCurrentWallpaper;
            if (status.StartsWith("抓取失败", StringComparison.Ordinal))
                _trayIcon.ShowBalloonTip(3500, "Wallhaven 壁纸服务", status, Forms.ToolTipIcon.Error);
        });
    }

    private void Orchestrator_OnSearchStarting(object? sender, string searchTag)
    {
        try
        {
            _notificationService.ShowSearchStarting(searchTag);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => UpdateStatus($"搜索通知发送失败：{ex.Message}"));
        }
    }

    private void Orchestrator_OnNextSearchKeywordConsumed(object? sender, EventArgs e) =>
        Dispatcher.Invoke(NextSearchKeywordBox.Clear);

    private void Orchestrator_OnWallpaperUpdated(object? sender, WallpaperItem wallpaper)
    {
        try
        {
            _notificationService.ShowSearchResult(wallpaper);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => UpdateStatus($"结果通知发送失败：{ex.Message}"));
        }

        Dispatcher.Invoke(() =>
        {
            try
            {
                ShowCurrentWallpaper(wallpaper);
            }
            catch (Exception ex)
            {
                UpdateStatus($"壁纸预览加载失败：{ex.Message}");
            }
        });
    }

    private void Orchestrator_OnWallpaperRestored(object? sender, WallpaperItem wallpaper) =>
        Dispatcher.Invoke(() =>
        {
            try
            {
                ShowCurrentWallpaper(wallpaper);
                _saveCurrentMenuItem.Enabled = true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"历史壁纸预览加载失败：{ex.Message}");
            }
        });

    private void Orchestrator_OnSearchFailed(object? sender, SearchFailure failure)
    {
        try
        {
            _notificationService.ShowSearchFailure(failure);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => UpdateStatus($"失败通知发送失败：{ex.Message}"));
        }
    }

    private void Orchestrator_OnAutoRotationDisabled(object? sender, EventArgs e) =>
        Dispatcher.Invoke(() => ScheduleEnabledBox.IsChecked = false);

    private void ShowCurrentWallpaper(WallpaperItem wallpaper)
    {
        _displayedWallpaper = wallpaper;
        CurrentTagText.Text = wallpaper.SearchTag;
        CurrentIdText.Text = wallpaper.Id;
        CurrentResolutionText.Text = wallpaper.Resolution;
        CurrentPurityText.Text = wallpaper.Purity.ToUpperInvariant();
        CurrentPurityText.Foreground = wallpaper.Purity.ToLowerInvariant() switch
        {
            "sfw" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 138, 75)),
            "sketchy" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 112, 0)),
            "nsfw" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(201, 48, 44)),
            _ => System.Windows.SystemColors.GrayTextBrush
        };
        CurrentPurityText.FontWeight = FontWeights.SemiBold;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(wallpaper.ThumbnailUrl);
        image.EndInit();
        CurrentWallpaperImage.Source = image;
        WallpaperEmptyText.Visibility = Visibility.Collapsed;
        CopyUrlButton.IsEnabled = true;
        OpenUrlButton.IsEnabled = true;
    }

    private void CopyUrlButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_displayedWallpaper is null)
            return;

        System.Windows.Clipboard.SetText(_displayedWallpaper.SourceUrl);
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
        LogTextBox.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {status}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _notificationService.Dispose();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(1500, "Wallhaven 壁纸服务", "程序已在系统托盘中继续运行。", Forms.ToolTipIcon.Info);
        }
        base.OnClosing(e);
    }
}
