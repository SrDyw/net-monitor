using System;
using System.Windows;
using Microsoft.Win32;

namespace NetMonitor.Services;

/// <summary>Swaps the chart's day and night plates: following Windows, or pinned by the owner.</summary>
public sealed class ThemeService : IDisposable
{
    private readonly ResourceDictionary _host;
    private ResourceDictionary? _plate;
    private ThemeMode _mode = ThemeMode.System;

    public bool IsDark { get; private set; }

    public event EventHandler? Changed;

    public ThemeService(ResourceDictionary host)
    {
        _host = host;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    public ThemeMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Apply();
        }
    }

    public void Apply()
    {
        bool dark = _mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => !AppsUseLightTheme(),
        };
        if (_plate is not null && dark == IsDark) return;

        IsDark = dark;
        var plate = new ResourceDictionary
        {
            Source = new Uri(IsDark ? "Themes/Night.xaml" : "Themes/Day.xaml", UriKind.Relative),
        };
        if (_plate is not null) _host.MergedDictionaries.Remove(_plate);
        _host.MergedDictionaries.Insert(0, plate);
        _plate = plate;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || _mode != ThemeMode.System) return;
        Application.Current.Dispatcher.Invoke(Apply);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
}
