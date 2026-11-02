using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NetMonitor.Services;
using NetMonitor.ViewModels;

namespace NetMonitor.Controls;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private PreferencesViewModel? Prefs => (DataContext as MonitorViewModel)?.Prefs;

    private void OnPingHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Prefs?.CommitPingHost();
        e.Handled = true;
    }

    private void OnPingHostLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Prefs?.CommitPingHost();

    private void OnProxyClick(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        try
        {
            var window = new ProxyWindow();
            if (owner is not null) window.Owner = owner;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error("proxy window", ex);
            MessageBox.Show(owner, $"No se pudo abrir la configuración del proxy.\n{ex.Message}",
                "Proxy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
