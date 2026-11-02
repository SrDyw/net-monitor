using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using NetMonitor.Services;

namespace NetMonitor.ViewModels;

/// <summary>
/// The settings view's state. Every change applies at once and is saved; there is no
/// Save button. App listens to <see cref="ObservableObject.PropertyChanged"/> for side effects.
/// </summary>
public partial class PreferencesViewModel : ObservableObject
{
    private readonly Settings _settings;

    public PreferencesViewModel(Settings settings)
    {
        _settings = settings;
        _theme = settings.Theme;
        _startView = settings.StartView;
        _watermarkCorner = settings.WatermarkCorner;
        _watermarkOpacity = Math.Clamp(settings.WatermarkOpacity, MinOpacity, 1);
        _showDown = settings.WatermarkShowDown;
        _showUp = settings.WatermarkShowUp;
        _showLatency = settings.WatermarkShowLatency;
        _showTotal = settings.WatermarkShowTotal;
        _showChart = settings.WatermarkShowChart;
        _showNetwork = settings.WatermarkShowNetwork;
        _showNetworkDetails = settings.ShowNetworkDetails;
        _pingHost = settings.PingHost;
        _pingHostDraft = settings.PingHost;
        _highLatencyMs = Math.Clamp(settings.HighLatencyMs, MinLatency, MaxLatency);
        _ratesInBytes = settings.RatesInBytes;
        _startWithWindows = StartupService.IsEnabled;
    }

    public const double MinOpacity = 0.4;
    public const int MinLatency = 30;
    public const int MaxLatency = 400;
    public const int MinLogLines = 0;
    public const int MaxLogLines = 10;

    [ObservableProperty] private ThemeMode _theme;
    [ObservableProperty] private StartView _startView;
    [ObservableProperty] private Corner _watermarkCorner;
    [ObservableProperty] private double _watermarkOpacity;
    [ObservableProperty] private bool _showDown;
    [ObservableProperty] private bool _showUp;
    [ObservableProperty] private bool _showLatency;
    [ObservableProperty] private bool _showTotal;
    [ObservableProperty] private bool _showChart;
    [ObservableProperty] private bool _showNetwork;
    [ObservableProperty] private bool _showNetworkDetails;
    [ObservableProperty] private string _pingHost;
    [ObservableProperty] private int _highLatencyMs;
    [ObservableProperty] private bool _ratesInBytes;
    [ObservableProperty] private bool _startWithWindows;

    /// <summary>How many pinned-process log rows appear beside the watermark; 0 hides them.</summary>
    public int WatermarkLogLines
    {
        get => Math.Clamp(_settings.WatermarkLogLines, MinLogLines, MaxLogLines);
        set
        {
            int lines = Math.Clamp(value, MinLogLines, MaxLogLines);
            if (_settings.WatermarkLogLines == lines) return;
            _settings.WatermarkLogLines = lines;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Pinned process names, in pin order, saved so they come back next run.</summary>
    public IReadOnlyList<string> PinnedProcessNames
    {
        get => _settings.PinnedProcesses;
        set
        {
            var names = value
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.SequenceEqual(_settings.PinnedProcesses, StringComparer.OrdinalIgnoreCase)) return;
            _settings.PinnedProcesses = names;
            _settings.Save();
        }
    }

    /// <summary>Where the watermark sits relative to its chosen corner; set by dragging it.</summary>
    public double WatermarkOffsetX
    {
        get => _settings.WatermarkOffsetX;
        set
        {
            if (Math.Abs(_settings.WatermarkOffsetX - value) < 0.01) return;
            _settings.WatermarkOffsetX = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public double WatermarkOffsetY
    {
        get => _settings.WatermarkOffsetY;
        set
        {
            if (Math.Abs(_settings.WatermarkOffsetY - value) < 0.01) return;
            _settings.WatermarkOffsetY = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>What the text box holds while editing; committed on Enter or when focus leaves.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingHostError))]
    private string _pingHostDraft;

    public string? PingHostError =>
        IsValidHost(PingHostDraft) ? null : "Escribe una IP o un nombre de servidor, por ejemplo 1.1.1.1";

    public int RangeMinutes
    {
        get => _settings.RangeMinutes;
        set { _settings.RangeMinutes = value; _settings.Save(); }
    }

    public void CommitPingHost()
    {
        string host = PingHostDraft.Trim();
        if (!IsValidHost(host))
        {
            PingHostDraft = PingHost; // keep the last working host
            return;
        }
        PingHostDraft = host;
        PingHost = host;
    }

    private static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (IPAddress.TryParse(host.Trim(), out _) || Uri.CheckHostName(host.Trim()) == UriHostNameType.Dns);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(Theme): _settings.Theme = Theme; break;
            case nameof(StartView): _settings.StartView = StartView; break;
            case nameof(WatermarkCorner): _settings.WatermarkCorner = WatermarkCorner; break;
            case nameof(WatermarkOpacity): _settings.WatermarkOpacity = Math.Round(WatermarkOpacity, 2); break;
            case nameof(ShowDown): _settings.WatermarkShowDown = ShowDown; break;
            case nameof(ShowUp): _settings.WatermarkShowUp = ShowUp; break;
            case nameof(ShowLatency): _settings.WatermarkShowLatency = ShowLatency; break;
            case nameof(ShowTotal): _settings.WatermarkShowTotal = ShowTotal; break;
            case nameof(ShowChart): _settings.WatermarkShowChart = ShowChart; break;
            case nameof(ShowNetwork): _settings.WatermarkShowNetwork = ShowNetwork; break;
            case nameof(ShowNetworkDetails): _settings.ShowNetworkDetails = ShowNetworkDetails; break;
            case nameof(PingHost): _settings.PingHost = PingHost; break;
            case nameof(HighLatencyMs): _settings.HighLatencyMs = HighLatencyMs; break;
            case nameof(RatesInBytes): _settings.RatesInBytes = RatesInBytes; break;
            case nameof(StartWithWindows):
                StartupService.SetEnabled(StartWithWindows);
                return; // lives in the registry, not settings.json
            default: return;
        }
        _settings.Save();
    }
}
