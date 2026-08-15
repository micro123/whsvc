using System.Runtime.InteropServices;

namespace WallhavenService.Services;

public sealed class DesktopWallpaperService
{
    private const uint SpiSetDesktopWallpaper = 20;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint WmSettingChange = 0x001A;
    private static readonly IntPtr HwndBroadcast = new(-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SendNotifyMessage(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    public void SetWallpaper(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("壁纸文件不存在。", imagePath);

        // 只更新用户配置，不让 SystemParametersInfo 同步阻塞广播所有窗口。
        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, imagePath, SpifUpdateIniFile))
            throw new InvalidOperationException($"设置桌面壁纸失败，错误码：{Marshal.GetLastWin32Error()}。");

        // 异步通知其他窗口刷新系统设置，避免换壁纸时卡住其他软件。
        SendNotifyMessage(
            HwndBroadcast,
            WmSettingChange,
            new IntPtr(SpiSetDesktopWallpaper),
            IntPtr.Zero);
    }
}