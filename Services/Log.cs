using System;
using System.IO;

namespace NetMonitor.Services;

/// <summary>
/// Small diagnostics log at %AppData%\NetMonitor\net-monitor.log: errors and link-state
/// changes, so a stall can be traced after the fact. Rotates at 512 kB.
/// </summary>
public static class Log
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMonitor", "net-monitor.log");

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 512 * 1024) File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR {where}: {ex.GetType().Name}: {ex.Message}");
}
