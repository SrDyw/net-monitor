using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;
using NetMonitor.Interop;
using NetMonitor.Services;

namespace NetMonitor;

public partial class MainWindow : Window
{
    /// <summary>Set by the tray's Exit item so closing really closes.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Shown in the window footer, taken from the assembly version.</summary>
    public string AppVersion { get; } =
        "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTitleBar(App.Theme.IsDark);
    }

    public void ApplyTitleBar(bool dark)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;
        // Ground2 of each plate, so the caption continues the chart.
        Native.StyleTitleBar(hwnd, dark, dark ? 0x08192Au : 0xE8F0F5u, dark ? 0xE6F0F6u : 0x0B1F33u);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void OnGitHub(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Error("github", ex);
        }
        e.Handled = true;
    }
}
