using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.Services;

public readonly record struct WifiStatus(string? Ssid, int? SignalPercent);

/// <summary>Reads the connected SSID and signal from <c>netsh wlan show interfaces</c>.</summary>
public static partial class WifiInfo
{
    static WifiInfo() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // "SSID" is not translated in localized netsh output; "BSSID" is excluded by the anchor.
    [GeneratedRegex(@"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex SsidLine();

    // The signal label is localized ("Signal", "Señal"), but it is the only field given in percent.
    [GeneratedRegex(@"^[^:\r\n]+:\s*(\d{1,3})\s*%\s*$", RegexOptions.Multiline)]
    private static partial Regex PercentLine();

    public static async Task<WifiStatus> GetStatusAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var process = Process.Start(psi);
            if (process is null) return default;
            // netsh can hang while the adapter is resetting; give up after a few seconds.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string output;
            try
            {
                output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return default;
            }
            var ssid = SsidLine().Match(output);
            var signal = PercentLine().Match(output);
            return new WifiStatus(
                ssid.Success ? ssid.Groups[1].Value : null,
                signal.Success ? int.Parse(signal.Groups[1].Value) : null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return default;
        }
    }
}
