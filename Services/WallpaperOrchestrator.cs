using System.Threading;
using System.IO;
using WallhavenService.Models;

namespace WallhavenService.Services;

public sealed class WallpaperOrchestrator : IAsyncDisposable
{
    private const int MaximumConsecutiveFailures = 5;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(30)
    ];

    private readonly SettingsStore _settingsStore = new();
    private readonly WallpaperCacheStore _cacheStore = new();
    private readonly WallhavenClient _client = new();
    private readonly DesktopWallpaperService _wallpaperService = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly object _nextSearchKeywordLock = new();
    private readonly object _scheduleStateLock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource _schedulerCancellation = new();
    private Task? _schedulerTask;
    private AppSettings _settings;
    private WallpaperItem? _currentWallpaper;
    private string? _currentWallpaperPath;
    private DateTime? _currentWallpaperLastWriteUtc;
    private string _nextSearchKeyword = string.Empty;
    private int _started;
    private int _consecutiveFailures;
    private long _scheduleVersion;
    private DateTimeOffset? _nextRotationUtc;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? SearchStarting;
    public event EventHandler? NextSearchKeywordConsumed;
    public event EventHandler<WallpaperItem>? WallpaperUpdated;
    public event EventHandler<WallpaperItem>? WallpaperRestored;
    public event EventHandler<SearchRetry>? SearchRetryScheduled;
    public event EventHandler<SearchFailure>? SearchFailed;
    public event EventHandler? AutoRotationDisabled;

    public AppSettings Settings => _settings;
    public DateTimeOffset? NextRotationUtc
    {
        get
        {
            lock (_scheduleStateLock)
                return _nextRotationUtc;
        }
    }
    public bool HasCurrentWallpaper =>
        _currentWallpaper is not null &&
        _currentWallpaperPath is not null &&
        File.Exists(_currentWallpaperPath);

    public WallpaperOrchestrator()
    {
        _settings = _settingsStore.Load();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        var cacheRestored = TryRestoreCurrentWallpaper();
        if (!TryValidateSettings(_settings, out var validationError))
        {
            Report($"启动搜索未执行：{validationError}");
            return;
        }

        var remainingDelay = GetRemainingRotationDelay();
        if (cacheRestored && remainingDelay > TimeSpan.Zero)
        {
            Report($"已恢复当前壁纸 {_currentWallpaper!.Id}，距离下次轮换还有 {FormatDelay(remainingDelay)}");
        }
        else
        {
            await RunNowAsync(cancellationToken);
            remainingDelay = GetRotationInterval();
        }

        if (_settings.ScheduleEnabled && !cancellationToken.IsCancellationRequested)
            RestartScheduler(remainingDelay);
    }

    public void UpdateSettings(AppSettings settings)
    {
        var wasScheduleEnabled = _settings.ScheduleEnabled;
        _settings = settings;
        if (settings.ScheduleEnabled && !wasScheduleEnabled)
            _consecutiveFailures = 0;
        _settingsStore.Save(settings);
        if (Volatile.Read(ref _started) != 0)
            RestartScheduler(GetRotationInterval());
        Report("设置已保存");
    }

    public void SetNextSearchKeyword(string keyword)
    {
        string configuredKeyword;
        lock (_nextSearchKeywordLock)
        {
            _nextSearchKeyword = keyword.Trim();
            configuredKeyword = _nextSearchKeyword;
        }

        Report(string.IsNullOrWhiteSpace(configuredKeyword)
            ? "一次性关键词已清除"
            : $"下一次搜索将使用一次性关键词：{configuredKeyword}");
    }

    public async Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            Report("已有抓取任务正在运行");
            return;
        }

        var countFailure = _settings.ScheduleEnabled;
        try
        {
            var (searchTag, usedOneTimeKeyword) = SelectSearchTag();
            var displayTag = string.IsNullOrWhiteSpace(searchTag) ? "（无关键词）" : searchTag;
            Report($"开始搜索壁纸，关键词：{displayTag}");
            SearchStarting?.Invoke(this, displayTag);
            var (item, imagePath) = await SearchAndApplyWithRetryAsync(searchTag, cancellationToken);
            _currentWallpaper = item;
            _currentWallpaperPath = imagePath;
            _currentWallpaperLastWriteUtc = File.GetLastWriteTimeUtc(imagePath);
            _consecutiveFailures = 0;
            if (usedOneTimeKeyword && TryClearNextSearchKeyword(searchTag))
            {
                NextSearchKeywordConsumed?.Invoke(this, EventArgs.Empty);
                Report("一次性关键词已使用并清空");
            }
            Report($"壁纸更新成功：{item.Id} / {item.Resolution} / {item.Purity}");
            WallpaperUpdated?.Invoke(this, item);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report("抓取已取消");
        }
        catch (Exception ex)
        {
            var disabled = false;
            if (countFailure)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaximumConsecutiveFailures && _settings.ScheduleEnabled)
                {
                    _settings.ScheduleEnabled = false;
                    _settingsStore.Save(_settings);
                    _schedulerCancellation.Cancel();
                    disabled = true;
                }
            }

            Report($"抓取失败：{ex.Message}");
            var reportedFailureCount = countFailure ? _consecutiveFailures : 0;
            SearchFailed?.Invoke(this, new SearchFailure(ex.Message, reportedFailureCount, disabled));
            if (disabled)
            {
                Report("连续失败 5 次，自动轮换已禁用");
                AutoRotationDisabled?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<(WallpaperItem Wallpaper, string ImagePath)> SearchAndApplyWithRetryAsync(
        string searchTag,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var item = await _client.FindWallpaperAsync(_settings, searchTag, cancellationToken);
                Report($"已找到壁纸 {item.Id}，正在下载");
                var imagePath = await _client.DownloadCurrentAsync(item, _cacheStore.CacheDirectory, cancellationToken);
                _cacheStore.Save(item, imagePath);
                _wallpaperService.SetWallpaper(imagePath);
                return (item, imagePath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                var delay = RetryDelays[attempt];
                var retryNumber = attempt + 1;
                Report($"搜索失败：{ex.Message}；{delay.TotalSeconds:0} 秒后重试（{retryNumber}/{RetryDelays.Length}）");
                SearchRetryScheduled?.Invoke(
                    this,
                    new SearchRetry(ex.Message, retryNumber, RetryDelays.Length, delay));
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private (string SearchTag, bool UsedOneTimeKeyword) SelectSearchTag()
    {
        lock (_nextSearchKeywordLock)
        {
            if (!string.IsNullOrWhiteSpace(_nextSearchKeyword))
                return (_nextSearchKeyword, true);
        }

        var searchTag = _settings.SearchKeywords
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(_ => Random.Shared.Next())
            .FirstOrDefault() ?? string.Empty;
        return (searchTag, false);
    }

    private bool TryClearNextSearchKeyword(string usedKeyword)
    {
        lock (_nextSearchKeywordLock)
        {
            if (!string.Equals(_nextSearchKeyword, usedKeyword, StringComparison.Ordinal))
                return false;

            _nextSearchKeyword = string.Empty;
            return true;
        }
    }

    private bool TryRestoreCurrentWallpaper()
    {
        var cached = _cacheStore.Load();
        if (cached is null)
            return false;

        _currentWallpaper = cached.Value.Wallpaper;
        _currentWallpaperPath = cached.Value.ImagePath;
        _currentWallpaperLastWriteUtc = cached.Value.LastWriteTimeUtc;
        WallpaperRestored?.Invoke(this, _currentWallpaper);
        return true;
    }

    private TimeSpan GetRemainingRotationDelay()
    {
        if (_currentWallpaperLastWriteUtc is null)
            return TimeSpan.Zero;

        var interval = GetRotationInterval();
        var elapsed = DateTime.UtcNow - _currentWallpaperLastWriteUtc.Value;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return elapsed >= interval ? TimeSpan.Zero : interval - elapsed;
    }

    private TimeSpan GetRotationInterval() =>
        TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes));

    private static string FormatDelay(TimeSpan delay)
    {
        if (delay.TotalHours >= 1)
            return $"{(int)delay.TotalHours} 小时 {delay.Minutes} 分钟";
        return $"{Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes))} 分钟";
    }

    public static bool TryValidateSettings(AppSettings settings, out string error)
    {
        if (settings.IntervalMinutes < 1)
        {
            error = "轮换间隔必须大于 0 分钟。";
            return false;
        }

        var hasAllowedPurity = settings.IncludeSfw ||
                               settings.IncludeSketchy ||
                               (settings.IncludeNsfw && !string.IsNullOrWhiteSpace(settings.ApiKey));
        if (!hasAllowedPurity)
        {
            error = "没有可用的内容纯度。";
            return false;
        }

        if (!settings.IncludeGeneral && !settings.IncludeAnime && !settings.IncludePeople)
        {
            error = "没有选择壁纸分类。";
            return false;
        }

        error = string.Empty;
        return true;
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

    private void RestartScheduler(TimeSpan initialDelay)
    {
        _schedulerCancellation.Cancel();
        _schedulerCancellation.Dispose();
        _schedulerCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var scheduleVersion = Interlocked.Increment(ref _scheduleVersion);
        var shouldRun = TryValidateSettings(_settings, out _) && _settings.ScheduleEnabled;
        if (!shouldRun)
        {
            SetNextRotationUtc(scheduleVersion, null);
            _schedulerTask = Task.CompletedTask;
            return;
        }

        _schedulerTask = RunSchedulerAsync(initialDelay, scheduleVersion, _schedulerCancellation.Token);
    }

    private async Task RunSchedulerAsync(
        TimeSpan initialDelay,
        long scheduleVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var delay = initialDelay < TimeSpan.Zero ? TimeSpan.Zero : initialDelay;
            while (!cancellationToken.IsCancellationRequested && _settings.ScheduleEnabled)
            {
                SetNextRotationUtc(scheduleVersion, DateTimeOffset.UtcNow + delay);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                SetNextRotationUtc(scheduleVersion, null);
                if (cancellationToken.IsCancellationRequested || !_settings.ScheduleEnabled)
                    break;

                await RunNowAsync(cancellationToken).ConfigureAwait(false);
                delay = GetRotationInterval();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            SetNextRotationUtc(scheduleVersion, null);
        }
    }

    private void SetNextRotationUtc(long scheduleVersion, DateTimeOffset? value)
    {
        lock (_scheduleStateLock)
        {
            if (scheduleVersion == Volatile.Read(ref _scheduleVersion))
                _nextRotationUtc = value;
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
