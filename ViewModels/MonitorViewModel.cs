using System;
using System.Collections.Generic;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetMonitor.Services;

namespace NetMonitor.ViewModels;

public enum Body
{
    Monitor,
    Settings,
    Processes,
}

public partial class MonitorViewModel : ObservableObject
{
    private const int HistorySeconds = 15 * 60;
    private const int ProbeEverySeconds = 2;
    private const int SsidEverySeconds = 15;

    // Bytes crossing the adapter are proof the link works even when ICMP is blocked.
    private const int TrafficGraceSeconds = 12;

    private readonly TrafficSampler _sampler = new();
    private readonly LatencyProbe _probe = new();
    private readonly List<Sample> _samples = new(HistorySeconds + 8);
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startedAt = DateTime.Now;

    private long _totalDown, _totalUp;
    private double? _latency;
    private int _failedProbes;
    private bool _reachable;
    private ProbeTransport _probeTransport = ProbeTransport.Icmp;
    private DateTime _lastTrafficAt;
    private bool _probeInFlight;
    private DateTime _probeStarted;
    private LinkState _loggedState = LinkState.Measuring;
    private int _tick;
    private string? _ssid;
    private int? _signal;
    private bool _ssidInFlight;
    private int _outageCount;
    private int _outageSeconds;
    private bool _inOutage;

    public IReadOnlyList<Sample> Samples => _samples;

    public event EventHandler? ToggleModeRequested;

    [ObservableProperty] private int _revision;
    [ObservableProperty] private LinkState _state = LinkState.Measuring;
    [ObservableProperty] private string _stateTitle = "MIDIENDO";
    [ObservableProperty] private string _stateDetail = "Buscando adaptador";
    [ObservableProperty] private string _stateNote = "";
    [ObservableProperty] private string _downValue = "—";
    [ObservableProperty] private string _downUnit = "Mb/s";
    [ObservableProperty] private string _upValue = "—";
    [ObservableProperty] private string _upUnit = "Mb/s";
    [ObservableProperty] private string _latencyValue = "—";
    [ObservableProperty] private string _latencyUnit = "ms";
    [ObservableProperty] private bool _isLatencyHigh;
    [ObservableProperty] private bool _isOffline;
    [ObservableProperty] private string _totalDownText = "0 kB";
    [ObservableProperty] private string _totalUpText = "0 kB";
    [ObservableProperty] private string _sessionText = "0 s";
    [ObservableProperty] private string _outagesText = "Ninguno";
    [ObservableProperty] private string _summaryText = "Midiendo…";
    [ObservableProperty] private string _networkName = "—";
    [ObservableProperty] private string _networkKind = "—";
    [ObservableProperty] private string _localAddress = "—";
    [ObservableProperty] private string _gateway = "—";
    [ObservableProperty] private string _networkLine = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowSeconds))]
    private int _rangeMinutes = 5;

    public int WindowSeconds => RangeMinutes * 60;

    public string ProbeTarget => _probe.Host;

    /// <summary>Latency above this many ms reads as degraded.</summary>
    [ObservableProperty] private int _highLatencyMs = 100;

    /// <summary>Which body the main window shows.</summary>
    [ObservableProperty] private Body _body = Body.Monitor;

    /// <summary>The settings view binds here; App owns its side effects.</summary>
    public PreferencesViewModel? Prefs { get; init; }

    /// <summary>The process inspector view binds here.</summary>
    public ProcessesViewModel? Processes { get; init; }

    public string PingHost
    {
        get => _probe.Host;
        set => _probe.Host = value;
    }

    public MonitorViewModel()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        _sampler.Read(); // prime the counters
        _ = ProbeAsync();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    [RelayCommand]
    private void SetRange(string minutes) => RangeMinutes = int.Parse(minutes);

    [RelayCommand]
    private void ToggleMode() => ToggleModeRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenSettings() => Body = Body.Settings;

    [RelayCommand]
    private void OpenProcesses()
    {
        Body = Body.Processes;
        Processes?.Activate();
    }

    [RelayCommand]
    private void ShowMonitor() => Body = Body.Monitor;

    private void Tick()
    {
        // One bad cycle must never stop the timer: log it and keep sampling.
        try { Sample(); }
        catch (Exception ex) { Log.Error("tick", ex); }
    }

    private void Sample()
    {
        _tick++;
        // Watchdog: a probe stuck past any sane timeout is abandoned so measuring resumes.
        if (_probeInFlight && DateTime.Now - _probeStarted > TimeSpan.FromSeconds(10))
        {
            Log.Write("Medición de latencia atascada; se reinicia");
            _probeInFlight = false;
        }
        var reading = _sampler.Read();
        if (reading.DownBytes + reading.UpBytes > 0) _lastTrafficAt = DateTime.Now;
        if (_tick % ProbeEverySeconds == 0) _ = ProbeAsync();
        if (reading.IsWireless && _tick % SsidEverySeconds == 1) _ = RefreshSsidAsync();
        if (!reading.IsWireless) (_ssid, _signal) = (null, null);
        LocalAddress = reading.LocalAddress ?? "—";
        Gateway = reading.Gateway ?? "—";

        _totalDown += reading.DownBytes;
        _totalUp += reading.UpBytes;

        var state = Classify(reading.HasNetwork);
        if (state != _loggedState)
        {
            Log.Write($"Estado {_loggedState} -> {state} (adaptador: {reading.AdapterName ?? "ninguno"}, pings fallidos: {_failedProbes})");
            _loggedState = state;
        }
        TrackOutage(state);
        Push(new Sample(DateTime.Now, reading.DownBps, reading.UpBps, _latency, IsOutage(state)));
        Publish(state, reading.DownBps, reading.UpBps, reading.AdapterName, reading.IsWireless);
    }

    private LinkState Classify(bool hasNetwork)
    {
        if (!hasNetwork) return LinkState.NoNetwork;

        // VPNs, proxies and corporate firewalls routinely drop ICMP. A successful probe
        // of any kind, or bytes actually moving, proves the internet is there.
        bool trafficRecent = DateTime.Now - _lastTrafficAt <= TimeSpan.FromSeconds(TrafficGraceSeconds);
        if (!_reachable && !trafficRecent)
            return _failedProbes >= 2 ? LinkState.NoInternet : LinkState.Measuring;

        // Reachable but no round trip (HTTP-only through the proxy): connected, latency unknown.
        if (_latency is null) return LinkState.Connected;
        return _latency > HighLatencyMs ? LinkState.HighLatency : LinkState.Connected;
    }

    private static bool IsOutage(LinkState s) => s is LinkState.NoInternet or LinkState.NoNetwork;

    private void TrackOutage(LinkState state)
    {
        // Counted in samples (one per second) so the total matches the chart's CORTE marks.
        bool outage = IsOutage(state);
        if (outage)
        {
            _outageSeconds++;
            if (!_inOutage) _outageCount++;
        }
        _inOutage = outage;
    }

    private void Push(Sample sample)
    {
        _samples.Add(sample);
        if (_samples.Count > HistorySeconds) _samples.RemoveRange(0, _samples.Count - HistorySeconds);
    }

    private void Publish(LinkState state, double down, double up, string? adapter, bool wireless)
    {
        State = state;
        IsOffline = IsOutage(state);
        IsLatencyHigh = state == LinkState.HighLatency;

        (DownValue, DownUnit) = Format.Rate(down);
        (UpValue, UpUnit) = Format.Rate(up);

        if (state == LinkState.NoInternet)
        {
            LatencyValue = "Sin respuesta";
            LatencyUnit = "";
        }
        else if (_latency is { } latency)
        {
            LatencyValue = latency.ToString("0");
            LatencyUnit = "ms";
        }
        else
        {
            // Reachable through the proxy only: there is a connection but no round trip to show.
            LatencyValue = "—";
            LatencyUnit = "";
        }

        string link = adapter is null ? "Sin adaptador activo"
            : wireless && _ssid is not null ? $"Wi‑Fi · {_ssid}"
            : wireless ? $"Wi‑Fi · {adapter}"
            : adapter;

        string probeNote = _probeTransport switch
        {
            ProbeTransport.Tcp when _latency is { } tcp => $"TCP {ProbeTarget}:443 · {tcp:0} ms",
            ProbeTransport.Icmp when _latency is { } icmp => $"Ping {ProbeTarget} · {icmp:0} ms",
            ProbeTransport.Http => $"Ping {ProbeTarget} sin respuesta; conexión por proxy o VPN",
            _ => "Ping sin respuesta; hay tráfico",
        };

        (StateTitle, StateDetail, StateNote) = state switch
        {
            LinkState.Connected => ("CONECTADO", link, probeNote),
            LinkState.HighLatency => ("LATENCIA ALTA", link, probeNote),
            LinkState.NoInternet => ("SIN INTERNET", link, $"{ProbeTarget} no responde"),
            LinkState.NoNetwork => ("SIN RED", "Ningún adaptador con puerta de enlace", "Revisa el cable o el Wi‑Fi"),
            _ => ("MIDIENDO", link, "Esperando el primer ping"),
        };

        NetworkName = adapter is null ? "Sin red" : wireless && _ssid is not null ? _ssid : adapter;
        NetworkKind = adapter is null ? "—"
            : wireless ? (_signal is { } pct ? $"Wi‑Fi · señal {pct} %" : "Wi‑Fi")
            : "Cable";
        NetworkLine = adapter is null ? "Sin red" : wireless && _signal is { } p ? $"{NetworkName} · {p} %" : NetworkName;

        TotalDownText = Format.Bytes(_totalDown);
        TotalUpText = Format.Bytes(_totalUp);
        SessionText = Format.Duration(DateTime.Now - _startedAt);

        OutagesText = _outageCount == 0 ? "Ninguno" : $"{_outageCount} · {Format.Duration(TimeSpan.FromSeconds(_outageSeconds))}";

        SummaryText = IsOffline
            ? $"{StateTitle} · {StateDetail}"
            : $"Bajada {Format.RateText(down)} · Subida {Format.RateText(up)} · {(_latency is { } l ? $"{l:0} ms" : "sin ping")}";

        Revision++;
    }

    private async System.Threading.Tasks.Task ProbeAsync()
    {
        if (_probeInFlight) return;
        _probeInFlight = true;
        _probeStarted = DateTime.Now;
        try
        {
            var result = await _probe.ProbeAsync();
            if (!result.Reachable)
            {
                _failedProbes++;
                if (_failedProbes >= 2)
                {
                    _latency = null;
                    _reachable = false;
                }
                return;
            }
            _failedProbes = 0;
            _reachable = true;
            _probeTransport = result.Transport;
            _latency = result.Ms;
        }
        catch (Exception ex) { Log.Error("probe", ex); }
        finally { _probeInFlight = false; }
    }

    private async System.Threading.Tasks.Task RefreshSsidAsync()
    {
        if (_ssidInFlight) return;
        _ssidInFlight = true;
        try { (_ssid, _signal) = await WifiInfo.GetStatusAsync(); }
        catch (Exception ex) { Log.Error("wifi", ex); }
        finally { _ssidInFlight = false; }
    }

    /// <summary>
    /// Fills the history with synthetic traffic, including one outage, for screenshots
    /// (<c>--capture</c>). Never used during normal runs.
    /// </summary>
    public void LoadDemo()
    {
        var rng = new Random(7);
        var now = DateTime.Now;
        _samples.Clear();
        for (int i = HistorySeconds; i > 0; i--)
        {
            double t = HistorySeconds - i;
            bool outage = i is > 70 and < 112;
            double swell = 18e6 + 14e6 * Math.Sin(t / 47) + 9e6 * Math.Sin(t / 13);
            double burst = i is > 180 and < 230 ? 22e6 * Math.Sin((230 - i) / 50.0 * Math.PI) : 0;
            double down = outage ? 0 : Math.Max(0.4e6, swell + burst + rng.NextDouble() * 4e6);
            double up = outage ? 0 : Math.Max(0.2e6, 2.6e6 + 1.4e6 * Math.Sin(t / 29) + rng.NextDouble() * 1.2e6);
            double? ping = outage ? null : i < 25 ? 104 + rng.Next(30) : 16 + rng.Next(8);
            _samples.Add(new Sample(now.AddSeconds(-i), down, up, ping, outage));
        }
        _totalDown = 1_820_000_000;
        _totalUp = 214_000_000;
        _outageCount = 1;
        _outageSeconds = _samples.FindAll(x => x.Outage).Count;
        _latency = 118;
        _ssid = "CASA-5G";
        _signal = 82;
        LocalAddress = "192.168.1.34";
        Gateway = "192.168.1.1";
        var last = _samples[^1];
        Publish(LinkState.HighLatency, last.DownBps, last.UpBps, "Wi-Fi", wireless: true);
        SessionText = "2 h 17 min";
    }

    /// <summary>Screenshot state: the link just dropped.</summary>
    public void DemoOffline() => Publish(LinkState.NoInternet, 0, 0, "Wi-Fi", wireless: true);

    /// <summary>Screenshot state: a few seconds after launch, before the traces fill their minute.</summary>
    public void DemoStartup(int seconds)
    {
        _samples.RemoveRange(0, _samples.Count - seconds);
        var last = _samples[^1];
        Publish(LinkState.Connected, last.DownBps, last.UpBps, "Wi-Fi", wireless: true);
    }
}
