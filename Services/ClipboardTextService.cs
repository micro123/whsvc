using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WallhavenService.Services;

public static class ClipboardTextService
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int MaximumAttempts = 3;

    public static async Task SetTextAsync(IntPtr ownerWindow, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var textBytes = Encoding.Unicode.GetBytes(text + '\0');
        var globalMemory = GlobalAlloc(GmemMoveable, (nuint)textBytes.Length);
        if (globalMemory == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "分配剪贴板内存失败。");

        try
        {
            var target = GlobalLock(globalMemory);
            if (target == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "锁定剪贴板内存失败。");

            try
            {
                Marshal.Copy(textBytes, 0, target, textBytes.Length);
            }
            finally
            {
                GlobalUnlock(globalMemory);
            }

            Exception? lastError = null;
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                if (!OpenClipboard(ownerWindow))
                {
                    lastError = new Win32Exception(Marshal.GetLastWin32Error(), "打开系统剪贴板失败。");
                }
                else
                {
                    try
                    {
                        if (!EmptyClipboard())
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "清空系统剪贴板失败。");

                        if (SetClipboardData(CfUnicodeText, globalMemory) == IntPtr.Zero)
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "写入系统剪贴板失败。");

                        // SetClipboardData 成功后，内存所有权由系统接管。
                        globalMemory = IntPtr.Zero;
                        return;
                    }
                    catch (Win32Exception ex)
                    {
                        lastError = ex;
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }

                if (attempt < MaximumAttempts)
                    await Task.Delay(100 * attempt);
            }

            throw lastError ?? new InvalidOperationException("写入系统剪贴板失败。");
        }
        finally
        {
            if (globalMemory != IntPtr.Zero)
                GlobalFree(globalMemory);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr ownerWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
