using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace NetMonitor.Services;

public sealed record ProxyState(
    bool Enabled,
    string Server,
    string Bypass,
    string PacUrl,
    bool AutoDetect,
    string HttpProxy,
    string HttpsProxy);

/// <summary>
/// Reads and writes the Windows system proxy (WinINet) and the user's HTTP_PROXY /
/// HTTPS_PROXY variables, then tells open applications to reload the settings.
/// </summary>
public static partial class ProxyService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string HttpProxyName = "HTTP_PROXY";
    private const string HttpsProxyName = "HTTPS_PROXY";
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    public static ProxyState Load()
    {
        bool enabled = false, autoDetect = false;
        string server = "", bypass = "", pac = "";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is not null)
            {
                enabled = key.GetValue("ProxyEnable") is int e && e != 0;
                server = key.GetValue("ProxyServer") as string ?? "";
                bypass = key.GetValue("ProxyOverride") as string ?? "";
                pac = key.GetValue("AutoConfigURL") as string ?? "";
                autoDetect = key.GetValue("AutoDetect") is int a && a != 0;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }

        return new ProxyState(enabled, server, bypass, pac, autoDetect, ReadEnv(HttpProxyName), ReadEnv(HttpsProxyName));
    }

    /// <returns>Null on success, or a message to show in the dialog.</returns>
    public static string? Save(ProxyState state)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null) return "No se pudo abrir la configuración del proxy del sistema.";
            key.SetValue("ProxyEnable", state.Enabled ? 1 : 0, RegistryValueKind.DWord);
            SetOrDelete(key, "ProxyServer", state.Server);
            SetOrDelete(key, "ProxyOverride", state.Bypass);
            SetOrDelete(key, "AutoConfigURL", state.PacUrl);
            key.SetValue("AutoDetect", state.AutoDetect ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"No se pudo guardar el proxy del sistema: {ex.Message}";
        }

        try
        {
            SetEnv(HttpProxyName, state.HttpProxy);
            SetEnv(HttpsProxyName, state.HttpsProxy);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"No se pudieron guardar las variables de entorno: {ex.Message}";
        }

        // WinINet clients reread the registry; Explorer and shells reread the environment.
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment",
            SmtoAbortIfHung, 2000, out _);
        return null;
    }

    private static string ReadEnv(string name)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
                ?? "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "";
        }
    }

    private static void SetEnv(string name, string value)
    {
        string? text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Environment.SetEnvironmentVariable(name, text, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, text, EnvironmentVariableTarget.Process);
    }

    private static void SetOrDelete(RegistryKey key, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value.Trim(), RegistryValueKind.String);
    }

    [LibraryImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InternetSetOption(IntPtr session, int option, IntPtr buffer, int length);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
}
