using System.Runtime.InteropServices;
using System.IO;

namespace WallhavenService.Services;

public sealed class DesktopWallpaperService
{
    private const uint SpiSetDesktopWallpaper = 20;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    public void SetWallpaper(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("壁纸文件不存在。", imagePath);

        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, imagePath, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException($"设置桌面壁纸失败，错误码：{Marshal.GetLastWin32Error()}。");
    }
}
