using System.Runtime.InteropServices;

namespace WallhavenService.Services;

public sealed class DesktopWallpaperService
{
    private const uint SpiSetDesktopWallpaper = 20;
    private const uint SpifUpdateIniFile = 0x01;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    public void SetWallpaper(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("壁纸文件不存在。", imagePath);

        // 更新并持久化壁纸，但不向所有顶级窗口广播 WM_SETTINGCHANGE，
        // 避免浏览器等其他应用因处理全局设置变更而出现卡顿。
        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, imagePath, SpifUpdateIniFile))
            throw new InvalidOperationException($"设置桌面壁纸失败，错误码：{Marshal.GetLastWin32Error()}。");
    }
}