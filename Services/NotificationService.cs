using System.Diagnostics;
using System.Windows;
using CommunityToolkit.WinUI.Notifications;
using WallhavenService.Models;

namespace WallhavenService.Services;

public sealed class NotificationService : IDisposable
{
    public NotificationService()
    {
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    public void ShowSearchStarting(string searchTag)
    {
        new ToastContentBuilder()
            .AddText("开始搜索 Wallhaven")
            .AddText($"本次关键词：{searchTag}")
            .Show();
    }

    public void ShowSearchResult(WallpaperItem wallpaper)
    {
        new ToastContentBuilder()
            .AddText("壁纸下载完成")
            .AddText($"Tag：{wallpaper.SearchTag}\nID：{wallpaper.Id}\nResolution：{wallpaper.Resolution}\nPurity：{wallpaper.Purity}")
            .AddHeroImage(new Uri(wallpaper.ThumbnailUrl))
            .AddButton(new ToastButton()
                .SetContent("复制页面 URL")
                .AddArgument("action", "copy")
                .AddArgument("url", wallpaper.SourceUrl))
            .AddButton(new ToastButton()
                .SetContent("在浏览器打开")
                .AddArgument("action", "open")
                .AddArgument("url", wallpaper.SourceUrl))
            .Show();
    }

    public void ShowSearchFailure(SearchFailure failure)
    {
        var countText = failure.ConsecutiveFailures > 0
            ? $"连续失败：{failure.ConsecutiveFailures}/5"
            : "";
        var disabledText = failure.AutoRotationDisabled
            ? "\n自动轮换已禁用，请检查设置后重新启用。"
            : "";

        new ToastContentBuilder()
            .AddText("壁纸搜索失败")
            .AddText($"{failure.Message}\n{countText}{disabledText}".Trim())
            .Show();
    }

    private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var arguments = ToastArguments.Parse(e.Argument);
            var action = arguments.TryGetValue("action", out var actionValue) ? actionValue : null;
            var url = arguments.TryGetValue("url", out var urlValue) ? urlValue : null;
            if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(url))
                return;

            if (action == "open")
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }

            if (action == "copy")
                System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(url));
        }
        catch
        {
            // Toast activation must not terminate the background application.
        }
    }

    public void Dispose()
    {
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
    }
}
