using System;
using Microsoft.Win32;

namespace NetMonitor.Services;

/// <summary>
/// Launch at sign-in through the per-user Run key (no admin rights needed).
/// What opens at sign-in follows the "Al iniciar" setting; <c>--startup</c> only marks the launch.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NetMonitor";
    public const string StartupFlag = "--startup";

    private static string Command => $"\"{Environment.ProcessPath}\" {StartupFlag}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Re-points an existing entry at this executable, in case the app was moved or rebuilt elsewhere.</summary>
    public static void Refresh()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && current != Command) key.SetValue(ValueName, Command);
    }
}
