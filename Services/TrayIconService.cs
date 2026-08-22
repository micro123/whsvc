using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace WallhavenService.Services;

public sealed class TrayIconService : IDisposable
{
    public enum CommandId : uint
    {
        Open = 1001,
        RunNow = 1002,
        SaveCurrent = 1003,
        Exit = 1004
    }

    private const uint WmApp = 0x8000;
    private const uint WmCommand = 0x0111;
    private const uint WmTimer = 0x0113;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmDestroy = 0x0002;
    private const uint WmNull = 0x0000;
    private const uint TrayCallbackMessage = WmApp + 1;
    private const uint NidMessage = 0x00000001;
    private const uint NidIcon = 0x00000002;
    private const uint NidTip = 0x00000004;
    private const uint NidGuid = 0x00000020;
    private const uint NidShowTip = 0x00000080;
    private const uint DefaultNotifyFlags = NidMessage | NidIcon | NidTip | NidGuid | NidShowTip;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfEnabled = 0x00000000;
    private const uint MfGrayed = 0x00000001;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmLeftAlign = 0x0000;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const string InstallerRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{D8B3E5A0-3F92-4F08-B1E5-9E49B5C9A8F2}_is1";
    private static readonly Guid InstalledTrayIconGuid = new("e4ed9104-38ff-4d0d-8e4b-b8d3b3a5c906");
    private static readonly Guid DevelopmentTrayIconGuid = new("503973b3-026f-47e1-9246-f05b5a7431ee");
    private static readonly nuint SingleClickTimerId = 1;

    private readonly IntPtr _windowHandle;
    private readonly IntPtr _iconHandle;
    private readonly WndProc _wndProc;
    private readonly string _className = $"WallhavenService.Tray.{Guid.NewGuid():N}";
    private NotifyIconData _notifyData;
    private bool _disposed;
    private bool _saveEnabled;
    private bool _suppressNextLeftButtonUp;

    public event EventHandler? OpenRequested;
    public event EventHandler? RunNowRequested;
    public event EventHandler? SaveCurrentRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _wndProc = WindowProcedure;
        var module = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            cbSize = (uint)Marshal.SizeOf<WindowClass>(),
            lpfnWndProc = _wndProc,
            hInstance = module,
            lpszClassName = _className
        };

        if (RegisterClassEx(ref windowClass) == 0)
            throw new InvalidOperationException($"注册托盘窗口类失败，错误码：{Marshal.GetLastWin32Error()}。");

        _windowHandle = CreateWindowEx(
            WsExToolWindow | WsExNoActivate,
            _className,
            "WallhavenServiceTrayWindow",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            module,
            IntPtr.Zero);

        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException($"创建托盘窗口失败，错误码：{Marshal.GetLastWin32Error()}。");

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        if (_iconHandle == IntPtr.Zero)
            throw new InvalidOperationException($"加载托盘图标失败，错误码：{Marshal.GetLastWin32Error()}。");

        _notifyData = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = DefaultNotifyFlags,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _iconHandle,
            szTip = "Wallhaven 壁纸服务",
            guidItem = ResolveTrayIconGuid()
        };

        if (!ShellNotifyIcon(NimAdd, ref _notifyData))
            throw new InvalidOperationException($"创建系统托盘图标失败，错误码：{Marshal.GetLastWin32Error()}。");

        _notifyData.uVersion = NotifyIconVersion4;
        ShellNotifyIcon(NimSetVersion, ref _notifyData);
    }

    public void SetSaveEnabled(bool enabled) => _saveEnabled = enabled;

    public void SetToolTip(string text)
    {
        const int maximumToolTipLength = 127;
        var toolTip = string.IsNullOrWhiteSpace(text) ? "Wallhaven 壁纸服务" : text.Trim();
        _notifyData.szTip = toolTip.Length <= maximumToolTipLength ? toolTip : toolTip[..maximumToolTipLength];
        _notifyData.uFlags = NidTip | NidGuid | NidShowTip;
        ShellNotifyIcon(NimModify, ref _notifyData);
        _notifyData.uFlags = DefaultNotifyFlags;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == TrayCallbackMessage)
        {
            var mouseMessage = unchecked((uint)(lParam.ToInt64() & 0xffff));
            if (mouseMessage == WmLButtonUp)
            {
                if (_suppressNextLeftButtonUp)
                {
                    _suppressNextLeftButtonUp = false;
                }
                else
                {
                    ScheduleSingleClick();
                }
            }
            else if (mouseMessage == WmLButtonDblClk)
            {
                CancelSingleClick();
                _suppressNextLeftButtonUp = true;
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (mouseMessage is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
            }
            return IntPtr.Zero;
        }

        if (message == WmTimer && unchecked((nuint)wParam.ToInt64()) == SingleClickTimerId)
        {
            CancelSingleClick();
            RunNowRequested?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (message == WmCommand)
        {
            HandleCommand(unchecked((uint)(wParam.ToInt64() & 0xffff)));
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ScheduleSingleClick()
    {
        CancelSingleClick();
        if (SetTimer(_windowHandle, SingleClickTimerId, GetDoubleClickTime(), IntPtr.Zero) == 0)
            RunNowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelSingleClick() => KillTimer(_windowHandle, SingleClickTimerId);

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenu(menu, MfString | MfEnabled, (nuint)CommandId.Open, "打开设置");
            AppendMenu(menu, MfString | MfEnabled, (nuint)CommandId.RunNow, "立即抓取");
            AppendMenu(menu, MfString | (_saveEnabled ? MfEnabled : MfGrayed), (nuint)CommandId.SaveCurrent, "保存当前图片");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString | MfEnabled, (nuint)CommandId.Exit, "退出");

            GetCursorPos(out var cursor);
            SetForegroundWindow(_windowHandle);
            TrackPopupMenuEx(menu, TpmRightButton | TpmBottomAlign | TpmLeftAlign, cursor.X, cursor.Y, _windowHandle, IntPtr.Zero);
            PostMessage(_windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void HandleCommand(uint command)
    {
        switch ((CommandId)command)
        {
            case CommandId.Open:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CommandId.RunNow:
                RunNowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CommandId.SaveCurrent:
                if (_saveEnabled)
                    SaveCurrentRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CommandId.Exit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
    private static Guid ResolveTrayIconGuid()
    {
        try
        {
            using var uninstallKey = Registry.CurrentUser.OpenSubKey(InstallerRegistryPath);
            var installLocation = uninstallKey?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                var currentDirectory = Path.GetFullPath(AppContext.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var installedDirectory = Path.GetFullPath(installLocation)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(currentDirectory, installedDirectory, StringComparison.OrdinalIgnoreCase))
                    return InstalledTrayIconGuid;
            }
        }
        catch
        {
        }

        return DevelopmentTrayIconGuid;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelSingleClick();
        _notifyData.uFlags = NidGuid;
        ShellNotifyIcon(NimDelete, ref _notifyData);
        if (_iconHandle != IntPtr.Zero)
            DestroyIcon(_iconHandle);
        if (_windowHandle != IntPtr.Zero)
            DestroyWindow(_windowHandle);
        UnregisterClass(_className, GetModuleHandle(null));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(IntPtr hwnd, nuint eventId, uint interval, IntPtr timerCallback);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hwnd, nuint eventId);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint newItem, string? newItemText);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
