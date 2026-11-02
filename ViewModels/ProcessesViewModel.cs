using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetMonitor.Agent;
using NetMonitor.Services;

namespace NetMonitor.ViewModels;

/// <summary>One executable name in the process list; its rates sum every process with that name.</summary>
public partial class ProcessRow : ObservableObject
{
    public ProcessRow(string key, string name) => (Key, Name) = (key, name);

    public string Key { get; }
    public string Name { get; }
    public HashSet<int> Pids { get; set; } = new();

    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _downText = "—";
    [ObservableProperty] private string _upText = "—";
    [ObservableProperty] private bool _isQuiet = true;

    /// <summary>Pinned rows stay at the top in their current order.</summary>
    [ObservableProperty] private bool _isPinned;

    /// <summary>False for a pinned process that is not currently running.</summary>
    [ObservableProperty] private bool _isOpen = true;

    /// <summary>Bytes moved by this executable since the inspector started.</summary>
    public long TotalDown { get; set; }
    public long TotalUp { get; set; }
    [ObservableProperty] private string _totalDownText = "0 kB";
    [ObservableProperty] private string _totalUpText = "0 kB";

    public double Rate { get; set; }
}

/// <summary>A remote endpoint the inspected process talked to, with running totals.</summary>
public partial class DestinationRow : ObservableObject
{
    public DestinationRow(string ip, int port, string proto) => (Ip, Port, Proto) = (ip, port, proto);

    public string Ip { get; }
    public int Port { get; }
    public string Proto { get; }
    public string Endpoint => Ip.Contains(':') ? $"[{Ip}]:{Port}" : $"{Ip}:{Port}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayHost))]
    private string _host = "";

    /// <summary>Connections opened before the inspector started have no DNS answer to name them.</summary>
    public string DisplayHost => Host.Length > 0 ? Host : "sin nombre (solo IP)";
    [ObservableProperty] private string _downText = "0 kB";
    [ObservableProperty] private string _upText = "0 kB";
    [ObservableProperty] private string _lastText = "";

    public long Down { get; set; }
    public long Up { get; set; }
    public DateTime Last { get; set; }
}

/// <summary>One line of the verbose log. Kind drives its colour: DNS, CONECTA, CIERRA, DATOS, INFO.</summary>
public sealed record LogLine(DateTime Time, string Kind, string Text)
{
    public string TimeText => Time.ToString("HH:mm:ss.fff");
    public override string ToString() => $"{TimeText}  {Kind,-8} {Text}";
}

/// <summary>One packet-detail line for the pinned-process overlay in watermark mode.</summary>
public sealed record PinnedLogLine(DateTime Time, int Sequence, string Key, string Text)
{
    public string Counter => $"#{Sequence}";
}

public partial class ProcessesViewModel : ObservableObject
{
    private const int LogCapacity = 5000;
    private const int HistorySeconds = 15 * 60;
    private const int PinnedLogCapacity = 20;

    private readonly ProcessInspector _inspector;
    private readonly ProcessTree _tree = new();
    private readonly Dictionary<string, ProcessRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Down, long Up)> _totals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _hostByIp = new();
    private readonly Dictionary<(string Proto, string Ip, int Port), DestinationRow> _destinations = new();
    private readonly List<Sample> _samples = new();
    // Pinned names survive restarts, in the order the owner pinned them.
    private readonly List<string> _pinnedNames;
    private readonly Action<IReadOnlyList<string>>? _savePins;
    // Names the agent resolved for pids the snapshot never saw (processes that came and went).
    private readonly Dictionary<int, string> _agentNames = new();
    private int _ticks;
    private DateTime _lastTreeRefresh;
    private int _lastSelectedIndex = -1;
    private int _pinnedLogSequence;

    /// <summary>Raised when sorting moves the inspected row, so the view can scroll it back into sight.</summary>
    public event EventHandler? SelectionMoved;

    public ProcessesViewModel(ProcessInspector inspector, IEnumerable<string>? pinnedNames = null,
        Action<IReadOnlyList<string>>? savePins = null)
    {
        _inspector = inspector;
        _pinnedNames = pinnedNames?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new();
        _savePins = savePins;
        _inspector.StateChanged += (_, _) => OnInspectorState();
        _inspector.Message += (_, m) => OnMessage(m);

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => string.IsNullOrWhiteSpace(Search) ||
            ((ProcessRow)o).Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase);
        LogView = CollectionViewSource.GetDefaultView(Log);
        LogView.Filter = o => string.IsNullOrWhiteSpace(LogFilter) ||
            o.ToString()!.Contains(LogFilter.Trim(), StringComparison.OrdinalIgnoreCase);
        OnInspectorState();
    }

    public ObservableCollection<ProcessRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }
    public ObservableCollection<DestinationRow> Destinations { get; } = new();
    public ObservableCollection<LogLine> Log { get; } = new();
    public ICollectionView LogView { get; }
    /// <summary>Latest packet lines for pinned processes, shown beside the watermark.</summary>
    public ObservableCollection<PinnedLogLine> PinnedLog { get; } = new();
    public IReadOnlyList<Sample> Samples => _samples;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private ProcessRow? _selected;

    /// <summary>The row being tracked. Unlike the ListBox selection, it survives sorting and refreshes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ProcessRow? _inspected;

    [ObservableProperty] private InspectorState _state;
    [ObservableProperty] private string _stateTitle = "";
    [ObservableProperty] private string _stateMessage = "";
    [ObservableProperty] private string _stateAction = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _revision;
    [ObservableProperty] private string _groupDetail = "";
    [ObservableProperty] private string _downText = "—";
    [ObservableProperty] private string _upText = "—";
    [ObservableProperty] private string _totalDownText = "0 kB";
    [ObservableProperty] private string _totalUpText = "0 kB";
    [ObservableProperty] private bool _processGone;
    [ObservableProperty] private string _tab = "Destinations";
    [ObservableProperty] private string _sortBy = "Down";
    [ObservableProperty] private string _logFilter = "";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _autoScroll = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowSeconds))]
    private int _rangeMinutes = 1;

    public int WindowSeconds => RangeMinutes * 60;
    public bool HasSelection => Inspected is not null;
    public bool HasDestinations => Destinations.Count > 0;

    private long _totalDown, _totalUp;

    /// <summary>Called when the view opens: lists processes at once and asks for elevation if needed.</summary>
    public void Activate()
    {
        RefreshTree(force: true);
        if (_inspector.State is InspectorState.Idle) _inspector.Start();
    }

    [RelayCommand] private void Start() => _inspector.Start();
    [RelayCommand] private void Stop() => _inspector.Stop();
    [RelayCommand] private void SetTab(string tab) => Tab = tab;
    [RelayCommand] private void SetSort(string sort) { SortBy = sort; SortDestinations(); }
    [RelayCommand] private void SetRange(string minutes) => RangeMinutes = int.Parse(minutes);
    [RelayCommand] private void TogglePause() => IsPaused = !IsPaused;
    [RelayCommand] private void ClearLog() => Log.Clear();

    [RelayCommand]
    private void CopyLog()
    {
        string text = string.Join(Environment.NewLine, LogView.Cast<LogLine>());
        if (text.Length > 0) Clipboard.SetText(text);
    }

    [RelayCommand]
    private void SaveLog()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{Inspected?.Name ?? "net-monitor"}-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = "Registro (*.log)|*.log|Texto (*.txt)|*.txt",
        };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllLines(dialog.FileName, LogView.Cast<LogLine>().Select(l => l.ToString())); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog("INFO", $"No se pudo guardar: {ex.Message}", force: true);
        }
    }

    partial void OnSearchChanged(string value)
    {
        RowsView.Refresh();
        // A refresh can drop SelectedItem; put the inspected row back if it is visible again.
        if (Selected is null && Inspected is { } row && RowsView.Contains(row)) Selected = row;
    }

    partial void OnLogFilterChanged(string value) => LogView.Refresh();

    partial void OnSelectedChanged(ProcessRow? value)
    {
        if (value is null)
        {
            // Sorting and view refreshes can briefly clear SelectedItem. The inspected row
            // and its data stay put; the visual selection is restored once the list settles.
            if (Inspected is { } current && _rows.TryGetValue(current.Key, out var live) &&
                ReferenceEquals(live, current) && RowsView.Contains(current))
            {
                Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (Selected is not null || !RowsView.Contains(current)) return;
                    if (!_rows.TryGetValue(current.Key, out var still) || !ReferenceEquals(still, current)) return;
                    Selected = current;
                }));
            }
            return;
        }
        if (ReferenceEquals(value, Inspected)) return; // restoring the visual selection, not a new inspection

        Inspected = value;
        _lastSelectedIndex = Rows.IndexOf(value);
        _samples.Clear();
        _destinations.Clear();
        Destinations.Clear();
        OnPropertyChanged(nameof(HasDestinations));
        Log.Clear();
        _totalDown = _totalUp = 0;
        TotalDownText = TotalUpText = Format.Bytes(0);
        DownText = UpText = "—";
        ProcessGone = false;
        UpdateGroupDetail(value);
        AddLog("INFO", $"Inspeccionando {value.Name} ({Plural(value.Pids.Count)}). Solo se ve el tráfico desde ahora.", force: true);
        Revision++;
    }

    private void OnInspectorState()
    {
        State = _inspector.State;
        IsRunning = State == InspectorState.Running;
        (StateTitle, StateMessage, StateAction) = State switch
        {
            InspectorState.WaitingForPermission => ("ESPERANDO PERMISO",
                "Acepta el aviso de Windows para ver el tráfico de cada proceso. Se pide una vez por sesión.", ""),
            InspectorState.Denied => ("SIN PERMISO",
                "Se necesita permiso de administrador para ver el tráfico por proceso.", "Reintentar"),
            InspectorState.Failed => ("ERROR", _inspector.Error ?? "El rastreo se detuvo.", "Reintentar"),
            InspectorState.Idle => ("DETENIDO", "La inspección está detenida.", "Iniciar inspección"),
            _ => ("", "", ""),
        };
    }

    private void OnMessage(AgentMessage m)
    {
        switch (m.Kind)
        {
            case "tick": OnTick(m.Flows ?? new(), m.Names); break;
            case "dns": OnDns(m); break;
            case "conn": OnConnection(m); break;
        }
    }

    private void OnTick(List<FlowDelta> flows, Dictionary<int, string>? names)
    {
        _ticks++;
        bool unknown = false;
        if (names is not null)
        {
            foreach (var (pid, name) in names)
            {
                _agentNames[pid] = name;
                unknown |= !_tree.Processes.ContainsKey(pid);
            }
        }
        RefreshTree(force: _ticks % 5 == 0 || (unknown && DateTime.Now - _lastTreeRefresh > TimeSpan.FromSeconds(1)));

        var down = new Dictionary<int, long>();
        var up = new Dictionary<int, long>();
        foreach (var f in flows)
        {
            down[f.Pid] = down.GetValueOrDefault(f.Pid) + f.Down;
            up[f.Pid] = up.GetValueOrDefault(f.Pid) + f.Up;
        }

        foreach (var row in _rows.Values)
        {
            long d = row.Pids.Sum(p => down.GetValueOrDefault(p));
            long u = row.Pids.Sum(p => up.GetValueOrDefault(p));
            row.Rate = d + u;
            row.IsQuiet = d + u == 0;
            row.DownText = d == 0 ? "—" : Format.RateText(d * 8);
            row.UpText = u == 0 ? "—" : Format.RateText(u * 8);
            row.TotalDown += d;
            row.TotalUp += u;
            row.TotalDownText = Format.Bytes(row.TotalDown);
            row.TotalUpText = Format.Bytes(row.TotalUp);
            _totals[row.Key] = (row.TotalDown, row.TotalUp);
        }
        LogPinned(flows);
        if (_ticks % 2 == 0) SortRows();

        if (Inspected is { } sel) UpdateSelected(sel, flows, down, up);
    }

    private void UpdateSelected(ProcessRow sel, List<FlowDelta> flows, Dictionary<int, long> down, Dictionary<int, long> up)
    {
        long d = sel.Pids.Sum(p => down.GetValueOrDefault(p));
        long u = sel.Pids.Sum(p => up.GetValueOrDefault(p));
        _totalDown += d;
        _totalUp += u;
        DownText = Format.RateText(d * 8);
        UpText = Format.RateText(u * 8);
        TotalDownText = Format.Bytes(_totalDown);
        TotalUpText = Format.Bytes(_totalUp);

        _samples.Add(new Sample(DateTime.Now, d * 8, u * 8, null, false));
        if (_samples.Count > HistorySeconds) _samples.RemoveAt(0);

        var now = DateTime.Now;
        foreach (var f in flows.Where(f => sel.Pids.Contains(f.Pid)).OrderByDescending(f => f.Down + f.Up))
        {
            var row = Destination(f.Proto, f.Ip, f.Port);
            row.Down += f.Down;
            row.Up += f.Up;
            row.Last = now;
            row.DownText = Format.Bytes(row.Down);
            row.UpText = Format.Bytes(row.Up);
            AddLog("DATOS", $"{Prefix(f.Pid)}{Label(f.Ip, f.Port)}  {f.Proto}  ↓ {Format.Bytes(f.Down)}  ↑ {Format.Bytes(f.Up)}");
        }
        foreach (var row in _destinations.Values) row.LastText = Ago(now - row.Last);
        SortDestinations();

        ProcessGone = !sel.IsOpen || sel.Pids.All(p => !_tree.Processes.ContainsKey(p));
        Revision++;
    }

    /// <summary>Recent packet lines for the pinned processes; the watermark shows the last five.</summary>
    private void LogPinned(List<FlowDelta> flows)
    {
        Dictionary<int, ProcessRow>? byPid = null;
        foreach (var row in _rows.Values)
        {
            if (!row.IsPinned) continue;
            byPid ??= new();
            foreach (int pid in row.Pids) byPid[pid] = row;
        }
        if (byPid is null)
        {
            if (PinnedLog.Count > 0) PinnedLog.Clear();
            return;
        }

        // At most two destinations per pinned process each second, biggest first.
        var newest = flows
            .Where(f => f.Down + f.Up > 0 && byPid.ContainsKey(f.Pid))
            .GroupBy(f => byPid[f.Pid])
            .SelectMany(g => g.OrderByDescending(f => f.Down + f.Up).Take(2));
        foreach (var flow in newest)
        {
            var row = byPid[flow.Pid];
            AddPinnedLog(row.Key,
                $"{row.Name}  {Label(flow.Ip, flow.Port)}  {flow.Proto}  ↓ {Format.Bytes(flow.Down)}  ↑ {Format.Bytes(flow.Up)}");
        }
    }

    private void AddPinnedLog(string key, string text)
    {
        if (PinnedLog.Count >= PinnedLogCapacity) PinnedLog.RemoveAt(0);
        PinnedLog.Add(new PinnedLogLine(DateTime.Now, ++_pinnedLogSequence, key, text));
    }

    private void OnDns(AgentMessage m)
    {
        if (m.Name is null || m.Ips is null) return;
        foreach (string ip in m.Ips) _hostByIp[ip] = m.Name;
        foreach (var row in _destinations.Values)
            if (_hostByIp.TryGetValue(row.Ip, out var host)) row.Host = host;
        if (Inspected?.Pids.Contains(m.Pid) == true)
            AddLog("DNS", $"{Prefix(m.Pid)}{m.Name} → {string.Join(", ", m.Ips.Take(4))}{(m.Ips.Length > 4 ? "…" : "")}");
    }

    private void OnConnection(AgentMessage m)
    {
        if (Inspected?.Pids.Contains(m.Pid) != true || m.Ip is null) return;
        Destination(m.Proto ?? "TCP", m.Ip, m.Port);
        AddLog(m.Open ? "CONECTA" : "CIERRA", $"{Prefix(m.Pid)}{Label(m.Ip, m.Port)}  {m.Proto}");
    }

    private DestinationRow Destination(string proto, string ip, int port)
    {
        var key = (proto, ip, port);
        if (_destinations.TryGetValue(key, out var row)) return row;
        row = new DestinationRow(ip, port, proto)
        {
            Host = _hostByIp.TryGetValue(ip, out var host) ? host : "",
            Last = DateTime.Now,
        };
        _destinations[key] = row;
        Destinations.Add(row);
        OnPropertyChanged(nameof(HasDestinations));
        return row;
    }

    private void RefreshTree(bool force)
    {
        if (!force && _rows.Count > 0) return;
        _tree.Refresh();
        _lastTreeRefresh = DateTime.Now;

        // Saved pins exist even before their process starts.
        foreach (string name in _pinnedNames)
        {
            if (_rows.ContainsKey(name)) continue;
            var row = new ProcessRow(name, name) { IsPinned = true, IsOpen = false, Detail = "No está abierto" };
            RestoreTotals(row);
            row.PropertyChanged += OnRowPropertyChanged;
            _rows[name] = row;
            Rows.Add(row);
        }

        // Live processes plus any the agent named that already exited.
        var all = _tree.Processes.Values.Where(p => p.Pid != 0).ToList();
        foreach (var (pid, name) in _agentNames)
            if (!_tree.Processes.ContainsKey(pid)) all.Add(new ProcessInfo(pid, 0, name, null));

        foreach (var g in all.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!_rows.TryGetValue(g.Key, out var row))
            {
                row = new ProcessRow(g.Key, g.First().Name);
                RestoreTotals(row);
                row.IsPinned = _pinnedNames.Contains(g.Key, StringComparer.OrdinalIgnoreCase);
                row.PropertyChanged += OnRowPropertyChanged;
                _rows[g.Key] = row;
                Rows.Add(row);
            }
            row.Pids = g.Select(p => p.Pid).ToHashSet();
            row.Icon ??= _tree.IconFor(g.Select(p => p.Path).FirstOrDefault(p => p is not null));
            row.IsOpen = g.Any(p => _tree.Processes.ContainsKey(p.Pid));
            row.Detail = row.IsOpen ? Plural(row.Pids.Count) : "No está abierto";
        }
        // Pinned rows stay in place (marked idle) when their process exits. The rest are
        // dropped once quiet, except the one being inspected.
        foreach (var gone in _rows.Values.Where(r => r.Rate == 0 && !r.Pids.Any(_tree.Processes.ContainsKey)).ToList())
        {
            gone.IsOpen = false;
            if (gone.IsPinned)
            {
                foreach (int pid in gone.Pids) _agentNames.Remove(pid);
                gone.Pids = new HashSet<int>();
                gone.Detail = "No está abierto";
                gone.DownText = gone.UpText = "—";
                gone.IsQuiet = true;
                continue;
            }
            if (gone == Inspected)
            {
                gone.Detail = "No está abierto";
                continue;
            }
            foreach (int pid in gone.Pids) _agentNames.Remove(pid);
            _rows.Remove(gone.Key);
            Rows.Remove(gone);
        }
        if (Inspected is { } sel) UpdateGroupDetail(sel);
    }

    private void RestoreTotals(ProcessRow row)
    {
        if (!_totals.TryGetValue(row.Key, out var total)) return;
        row.TotalDown = total.Down;
        row.TotalUp = total.Up;
        row.TotalDownText = Format.Bytes(total.Down);
        row.TotalUpText = Format.Bytes(total.Up);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProcessRow.IsPinned)) return;
        if (sender is ProcessRow row)
        {
            if (row.IsPinned)
            {
                if (!_pinnedNames.Contains(row.Key, StringComparer.OrdinalIgnoreCase))
                {
                    _pinnedNames.Add(row.Key);
                    _savePins?.Invoke(_pinnedNames.ToList());
                }
            }
            else
            {
                if (_pinnedNames.RemoveAll(n => string.Equals(n, row.Key, StringComparison.OrdinalIgnoreCase)) > 0)
                    _savePins?.Invoke(_pinnedNames.ToList());
                for (int i = PinnedLog.Count - 1; i >= 0; i--)
                    if (PinnedLog[i].Key == row.Key) PinnedLog.RemoveAt(i);
                if (!_rows.Values.Any(r => r.IsPinned)) PinnedLog.Clear();
            }
        }
        SortRows();
    }

    /// <summary>Pinned rows in the order they were pinned; the rest busiest first. Move keeps the selection.</summary>
    private void SortRows()
    {
        int PinOrder(ProcessRow row)
        {
            int index = _pinnedNames.FindIndex(n => string.Equals(n, row.Key, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        var order = Rows.Where(r => r.IsPinned)
            .OrderBy(PinOrder)
            .Concat(Rows.Where(r => !r.IsPinned)
                .OrderByDescending(r => r.Rate)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        for (int i = 0; i < order.Count; i++)
        {
            int at = Rows.IndexOf(order[i]);
            if (at != i) Rows.Move(at, i);
        }
        TrackSelectionMove();
    }

    private void TrackSelectionMove()
    {
        if (Inspected is not { } sel)
        {
            _lastSelectedIndex = -1;
            return;
        }
        int index = Rows.IndexOf(sel);
        if (index == _lastSelectedIndex) return;
        _lastSelectedIndex = index;
        SelectionMoved?.Invoke(this, EventArgs.Empty);
    }

    private void SortDestinations()
    {
        IEnumerable<DestinationRow> sorted = SortBy switch
        {
            "Up" => Destinations.OrderByDescending(d => d.Up),
            "Recent" => Destinations.OrderByDescending(d => d.Last),
            _ => Destinations.OrderByDescending(d => d.Down),
        };
        var order = sorted.ToList();
        for (int i = 0; i < order.Count; i++)
        {
            int at = Destinations.IndexOf(order[i]);
            if (at != i) Destinations.Move(at, i);
        }
    }

    private void AddLog(string kind, string text, bool force = false)
    {
        if (IsPaused && !force) return;
        Log.Add(new LogLine(DateTime.Now, kind, text));
        while (Log.Count > LogCapacity) Log.RemoveAt(0);
    }

    private void UpdateGroupDetail(ProcessRow row)
    {
        var pids = row.Pids.OrderBy(p => p).ToList();
        GroupDetail = pids.Count switch
        {
            0 => "Sin procesos activos",
            1 => $"PID {pids[0]}",
            _ => $"{Plural(pids.Count)} · PID {string.Join(", ", pids.Take(4))}{(pids.Count > 4 ? "…" : "")}",
        };
    }

    private string Prefix(int pid) => Inspected is { Pids.Count: > 1 } ? $"[{pid}] " : "";

    private string Label(string ip, int port)
    {
        if (_hostByIp.TryGetValue(ip, out var host)) return $"{host}:{port}";
        return ip.Contains(':') ? $"[{ip}]:{port}" : $"{ip}:{port}";
    }

    private static string Plural(int n) => n == 1 ? "1 proceso" : $"{n} procesos";

    private static string Ago(TimeSpan span) =>
        span.TotalSeconds < 2 ? "ahora" : span.TotalMinutes < 1 ? $"hace {span.Seconds} s" : $"hace {(int)span.TotalMinutes} min";

    /// <summary>Synthetic inspector state for <c>--capture</c> screenshots. Never used during normal runs.</summary>
    public void LoadDemo()
    {
        State = InspectorState.Running;
        IsRunning = true;
        (StateTitle, StateMessage, StateAction) = ("", "", "");
        _tree.Refresh();
        string[] names = { "chrome", "Discord", "steam", "OneDrive", "powershell", "svchost", "Spotify", "Code" };
        int[] counts = { 24, 6, 3, 1, 1, 14, 5, 9 };
        double[] rates = { 6.1e6, 220e3, 1.9e6, 48e3, 0, 12e3, 410e3, 0 };
        Inspected = null;
        Selected = null;
        Rows.Clear();
        _rows.Clear();
        _totals.Clear();
        PinnedLog.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            var row = new ProcessRow(names[i], names[i])
            {
                Detail = Plural(counts[i]),
                Rate = rates[i],
                IsQuiet = rates[i] == 0,
                DownText = rates[i] == 0 ? "—" : Format.RateText(rates[i] * 0.92 * 8),
                UpText = rates[i] == 0 ? "—" : Format.RateText(rates[i] * 0.08 * 8),
                Pids = Enumerable.Range(1000 + i * 100, counts[i]).ToHashSet(),
            };
            row.TotalDown = (long)((rates[i] + 80e3) * 540);
            row.TotalUp = (long)(row.TotalDown * 0.08);
            row.TotalDownText = Format.Bytes(row.TotalDown);
            row.TotalUpText = Format.Bytes(row.TotalUp);
            _totals[row.Key] = (row.TotalDown, row.TotalUp);
            _rows[row.Key] = row;
            Rows.Add(row);
        }
        SortRows();
        Selected = Rows[0];

        var rng = new Random(3);
        var now = DateTime.Now;
        _samples.Clear();
        for (int s = 60; s > 0; s--)
        {
            double d = Math.Max(0, 5.2e6 + 3.5e6 * Math.Sin(s / 6.0) + rng.NextDouble() * 1.5e6) * 8;
            _samples.Add(new Sample(now.AddSeconds(-s), d, d * 0.07 + rng.NextDouble() * 1e5, null, false));
        }
        (string Host, string Ip, int Port, string Proto, long Down, long Up, int Ago)[] dest =
        {
            ("rr3---sn-hxqp5-ab5e.googlevideo.com", "173.194.5.200", 443, "QUIC", 182_400_000, 2_100_000, 0),
            ("www.youtube.com", "142.250.78.110", 443, "QUIC", 6_800_000, 950_000, 1),
            ("i.ytimg.com", "142.250.64.150", 443, "TCP", 3_400_000, 120_000, 4),
            ("fonts.gstatic.com", "142.251.34.163", 443, "TCP", 410_000, 18_000, 38),
            ("api.github.com", "140.82.112.6", 443, "TCP", 96_000, 12_000, 75),
            ("", "34.107.243.93", 443, "TCP", 22_000, 6_000, 140),
        };
        foreach (var x in dest)
        {
            var row = Destination(x.Proto, x.Ip, x.Port);
            row.Host = x.Host;
            row.Down = x.Down;
            row.Up = x.Up;
            row.Last = now.AddSeconds(-x.Ago);
            row.DownText = Format.Bytes(x.Down);
            row.UpText = Format.Bytes(x.Up);
            row.LastText = Ago(TimeSpan.FromSeconds(x.Ago));
        }
        _totalDown = 193_100_000;
        _totalUp = 3_200_000;
        TotalDownText = Format.Bytes(_totalDown);
        TotalUpText = Format.Bytes(_totalUp);
        DownText = Rows[0].DownText;
        UpText = Rows[0].UpText;
        GroupDetail = "24 procesos · PID 1000, 1001, 1002, 1003…";

        Log.Clear();
        void L(int ms, string kind, string text) => Log.Add(new LogLine(now.AddMilliseconds(-ms), kind, text));
        L(9200, "INFO", "Inspeccionando chrome (24 procesos). Solo se ve el tráfico desde ahora.");
        L(8100, "DNS", "[1004] api.github.com → 140.82.112.6");
        L(8050, "CONECTA", "[1004] api.github.com:443  TCP");
        L(7000, "DATOS", "[1004] api.github.com:443  TCP  ↓ 96 kB  ↑ 12 kB");
        L(5200, "DNS", "[1011] rr3---sn-hxqp5-ab5e.googlevideo.com → 173.194.5.200");
        L(4000, "DATOS", "[1011] rr3---sn-hxqp5-ab5e.googlevideo.com:443  QUIC  ↓ 5.8 MB  ↑ 61 kB");
        L(3000, "DATOS", "[1011] rr3---sn-hxqp5-ab5e.googlevideo.com:443  QUIC  ↓ 6.1 MB  ↑ 70 kB");
        L(2900, "CIERRA", "[1004] api.github.com:443  TCP");
        L(2000, "DATOS", "[1011] rr3---sn-hxqp5-ab5e.googlevideo.com:443  QUIC  ↓ 5.4 MB  ↑ 58 kB");
        L(1000, "DATOS", "[1007] i.ytimg.com:443  TCP  ↓ 210 kB  ↑ 9 kB");
        Revision++;
    }
}
