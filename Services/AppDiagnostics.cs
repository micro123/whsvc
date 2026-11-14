using System.Diagnostics;
using System.Text;

namespace WallhavenService.Services;

internal static class AppDiagnostics
{
    private static readonly object LogLock = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallhavenService", "diagnostics.log");

    public static void Write(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= 1024 * 1024)
                    File.Move(LogPath, LogPath + ".previous", overwrite: true);

                File.AppendAllText(LogPath,
                    $"[{DateTimeOffset.Now:O}] PID={Environment.ProcessId} " +
                    $"TID={Environment.CurrentManagedThreadId} " +
                    $"Apartment={Thread.CurrentThread.GetApartmentState()} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // Logging must not cause a second failure while handling an exception.
            Debug.WriteLine($"Diagnostic log write failed: {ex.Message}");
        }
    }

    public static void WriteException(string context, Exception exception) =>
        Write($"{context}{Environment.NewLine}{exception}");
}
