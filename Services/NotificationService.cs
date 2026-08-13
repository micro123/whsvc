using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.ApplicationModel.DataTransfer;
using WallhavenService.Models;

namespace WallhavenService.Services;

public sealed class NotificationService : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly AppNotificationManager _manager = AppNotificationManager.Default;

    public NotificationService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        _manager.NotificationInvoked += OnNotificationInvoked;
        _manager.Register();
    }

    public void ShowSearchStarting(string searchTag) =>
        Show(new AppNotificationBuilder()
            .AddText("开始搜索 Wallhaven")
            .AddText($"本次关键词：{searchTag}"));

    public void ShowSearchResult(WallpaperItem wallpaper) =>
        Show(new AppNotificationBuilder()
            .AddText("壁纸下载完成")
            .AddText($"Tag：{wallpaper.SearchTag}\nID：{wallpaper.Id}\nResolution：{wallpaper.Resolution}\nPurity：{wallpaper.Purity}")
            .SetHeroImage(new Uri(wallpaper.ThumbnailUrl))
            .AddButton(new AppNotificationButton("复制页面 URL")
                .AddArgument("action", "copy")
                .AddArgument("url", wallpaper.SourceUrl))
            .AddButton(new AppNotificationButton("在浏览器打开")
                .AddArgument("action", "open")
                .AddArgument("url", wallpaper.SourceUrl)));

    public void ShowSearchFailure(SearchFailure failure)
    {
        var countText = failure.ConsecutiveFailures > 0
            ? $"连续失败：{failure.ConsecutiveFailures}/5"
            : string.Empty;
        var disabledText = failure.AutoRotationDisabled
            ? "\n自动轮换已禁用，请检查设置后重新启用。"
            : string.Empty;

        Show(new AppNotificationBuilder()
            .AddText("壁纸搜索失败")
            .AddText($"{failure.Message}\n{countText}{disabledText}".Trim()));
    }

    public void ShowSearchRetry(SearchRetry retry) =>
        Show(new AppNotificationBuilder()
            .AddText("壁纸搜索暂时失败")
            .AddText($"{retry.Message}\n将在 {retry.Delay.TotalSeconds:0} 秒后重试（{retry.RetryNumber}/{retry.MaximumRetries}）"));

    private void Show(AppNotificationBuilder builder) =>
        _manager.Show(builder.BuildNotification());

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            var action = args.Arguments.TryGetValue("action", out var actionValue) ? actionValue : null;
            var url = args.Arguments.TryGetValue("url", out var urlValue) ? urlValue : null;
            if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(url))
                return;

            if (action == "open")
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }

            if (action == "copy")
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    var package = new DataPackage();
                    package.SetText(url);
                    Clipboard.SetContent(package);
                    Clipboard.Flush();
                });
            }
        }
        catch
        {
            // Notification activation must not terminate the background application.
        }
    }

    public void Dispose()
    {
        _manager.NotificationInvoked -= OnNotificationInvoked;
        _manager.Unregister();
    }
}
