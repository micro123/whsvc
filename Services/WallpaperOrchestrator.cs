using System.Threading;
using System.IO;
using WallhavenService.Models;

namespace WallhavenService.Services;

public sealed class WallpaperOrchestrator : IAsyncDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly WallhavenClient _client = new();
    private readonly DesktopWallpaperService _wallpaperService = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource _schedulerCancellation = new();
    private Task? _schedulerTask;
    private AppSettings _settings;
    private WallpaperItem? _currentWallpaper;
    private string? _currentWallpaperPath;

    public event EventHandler<string>? StatusChanged;

    public AppSettings Settings => _settings;
    public bool HasCurrentWallpaper =>
        _currentWallpaper is not null &&
        _currentWallpaperPath is not null &&
        File.Exists(_currentWallpaperPath);

    public WallpaperOrchestrator()
    {
        _settings = _settingsStore.Load();
        RestartScheduler();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _settingsStore.Save(settings);
        RestartScheduler();
        Report("设置已保存");
    }

    public async Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            Report("已有抓取任务正在运行");
            return;
        }

        try
        {
            Report("开始抓取壁纸");
            var item = await _client.FindWallpaperAsync(_settings, cancellationToken);
            Report($"已找到壁纸 {item.Id}，正在下载");
            var tempDirectory = Path.Combine(Path.GetTempPath(), "WallhavenService");
            var imagePath = await _client.DownloadCurrentAsync(item, tempDirectory, cancellationToken);
            _wallpaperService.SetWallpaper(imagePath);
            _currentWallpaper = item;
            _currentWallpaperPath = imagePath;
            Report($"壁纸更新成功：{Path.GetFileName(imagePath)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report("抓取已取消");
        }
        catch (Exception ex)
        {
            Report($"抓取失败：{ex.Message}");
        }
        finally
        {
            _runLock.Release();
        }
    }

    public string? SaveCurrentWallpaper()
    {
        if (!HasCurrentWallpaper)
        {
            Report("当前没有可保存的壁纸");
            return null;
        }

        var picturesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Directory.CreateDirectory(picturesDirectory);
        var extension = Path.GetExtension(_currentWallpaperPath!);
        var destination = Path.Combine(picturesDirectory, $"wallhaven-{_currentWallpaper!.Id}{extension}");
        if (File.Exists(destination))
        {
            Report($"当前图片已经保存：{destination}");
            return destination;
        }

        File.Copy(_currentWallpaperPath!, destination, overwrite: false);
        Report($"当前图片已保存：{destination}");
        return destination;
    }

    private void RestartScheduler()
    {
        _schedulerCancellation.Cancel();
        _schedulerCancellation.Dispose();
        _schedulerCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _schedulerTask = RunSchedulerAsync(_schedulerCancellation.Token);
    }

    private async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        if (!_settings.ScheduleEnabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes)));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await RunNowAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Report(string message) => StatusChanged?.Invoke(this, message);

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _schedulerCancellation.Cancel();
        if (_schedulerTask is not null)
            await _schedulerTask;
        _schedulerCancellation.Dispose();
        _client.Dispose();
        _runLock.Dispose();
        _shutdown.Dispose();
    }
}
