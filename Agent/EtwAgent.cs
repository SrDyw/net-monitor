using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetMonitor.Agent;

/// <summary>
/// The elevated half of the process inspector: <c>NetMonitor.exe --etw-agent &lt;pipe&gt; &lt;parentPid&gt;</c>.
/// Reads kernel TCP/UDP and DNS-Client events, folds them into one message per second,
/// and writes JSON lines to the app's pipe. Exits when the pipe breaks or the app is gone.
/// </summary>
public static class EtwAgent
{
    public const string Flag = "--etw-agent";
    private const string SessionPrefix = "NetMonitor-Inspector";
    private const int DnsQueryCompleted = 3008;

    private static readonly object Gate = new();      // guards _flows
    private static readonly object WriteGate = new(); // guards the pipe writer
    private static Dictionary<(int Pid, string Proto, string Ip, int Port), (long Down, long Up)> _flows = new();
    private static HashSet<string> _localIps = new();
    private static StreamWriter? _out;
    private static TraceEventSession? _session;
    private static Process? _parent;
    private static readonly Dictionary<int, string> _knownNames = new();

    public static int Run(string pipeName, int parentPid)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        // A UAC approval with another admin account cannot open this user's pipe; the app reports it.
        try { pipe.Connect(15000); }
        catch (Exception ex) when (ex is TimeoutException or UnauthorizedAccessException or IOException) { return 2; }
        // One handle on the app for the whole run: no per-second handle churn, no pid-reuse confusion.
        try { _parent = Process.GetProcessById(parentPid); }
        catch (ArgumentException) { return 0; }
        _out = new StreamWriter(pipe, new UTF8Encoding(false));

        if (TraceEventSession.IsElevated() != true)
        {
            Send(new AgentMessage { Kind = "error", Text = "El agente no tiene permisos de administrador." });
            return 1;
        }

        RefreshLocalIps();
        try
        {
            _session = new TraceEventSession($"{SessionPrefix}-{parentPid}") { StopOnDispose = true };
            // Kernel events first: TraceEvent requires it before user providers.
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            _session.EnableProvider("Microsoft-Windows-DNS-Client");
        }
        catch (Exception ex)
        {
            Send(new AgentMessage { Kind = "error", Text = $"No se pudo iniciar el rastreo de red: {ex.Message}" });
            return 1;
        }

        Hook(_session.Source);

        using var timer = new Timer(_ => Flush(), null, 1000, 1000);
        try { _session.Source.Process(); } // blocks until the session stops
        catch (Exception ex) { Send(new AgentMessage { Kind = "error", Text = ex.Message }); }
        finally
        {
            timer.Dispose();
            _session.Dispose();
            _parent?.Dispose();
        }
        return 0;
    }

    private static void Hook(TraceEventSource source)
    {
        var k = source.Kernel;
        k.TcpIpRecv += d => Add(d.ProcessID, "TCP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: true);
        k.TcpIpSend += d => Add(d.ProcessID, "TCP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: false);
        k.TcpIpRecvIPV6 += d => Add(d.ProcessID, "TCP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: true);
        k.TcpIpSendIPV6 += d => Add(d.ProcessID, "TCP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: false);
        k.UdpIpRecv += d => Add(d.ProcessID, "UDP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: true);
        k.UdpIpSend += d => Add(d.ProcessID, "UDP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: false);
        k.UdpIpRecvIPV6 += d => Add(d.ProcessID, "UDP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: true);
        k.UdpIpSendIPV6 += d => Add(d.ProcessID, "UDP", d.daddr, d.saddr, d.dport, d.sport, d.size, down: false);

        k.TcpIpConnect += d => Connection(d.ProcessID, d.daddr, d.saddr, d.dport, d.sport, open: true);
        k.TcpIpConnectIPV6 += d => Connection(d.ProcessID, d.daddr, d.saddr, d.dport, d.sport, open: true);
        k.TcpIpDisconnect += d => Connection(d.ProcessID, d.daddr, d.saddr, d.dport, d.sport, open: false);
        k.TcpIpDisconnectIPV6 += d => Connection(d.ProcessID, d.daddr, d.saddr, d.dport, d.sport, open: false);

        var registered = new RegisteredTraceEventParser(source);
        registered.All += e =>
        {
            if ((int)e.ID != DnsQueryCompleted || e.ProviderName != "Microsoft-Windows-DNS-Client") return;
            if (e.PayloadByName("QueryStatus") is IConvertible status && Convert.ToInt64(status) != 0) return;
            string? name = e.PayloadByName("QueryName") as string;
            string? results = e.PayloadByName("QueryResults") as string;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(results)) return;
            // "140.82.112.6;::ffff:140.82.112.6;type:  5 example.com;" — keep the parseable addresses.
            var ips = results.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) ? s[7..] : s)
                .Where(s => IPAddress.TryParse(s, out _))
                .Distinct()
                .ToArray();
            if (ips.Length > 0) Send(new AgentMessage { Kind = "dns", Pid = e.ProcessID, Name = name, Ips = ips });
        };
    }

    /// <summary>Kernel network events carry both ends; the remote one is whichever is not this machine.</summary>
    private static (string Ip, int Port) Remote(IPAddress daddr, IPAddress saddr, int dport, int sport)
    {
        string d = daddr.ToString(), s = saddr.ToString();
        return _localIps.Contains(d) && !_localIps.Contains(s) ? (s, sport) : (d, dport);
    }

    private static void Add(int pid, string proto, IPAddress daddr, IPAddress saddr, int dport, int sport, int size, bool down)
    {
        var (ip, port) = Remote(daddr, saddr, dport, sport);
        if (proto == "UDP" && port == 443) proto = "QUIC";
        var key = (pid, proto, ip, port);
        lock (Gate)
        {
            _flows.TryGetValue(key, out var v);
            _flows[key] = down ? (v.Down + size, v.Up) : (v.Down, v.Up + size);
        }
    }

    private static void Connection(int pid, IPAddress daddr, IPAddress saddr, int dport, int sport, bool open)
    {
        var (ip, port) = Remote(daddr, saddr, dport, sport);
        Send(new AgentMessage { Kind = "conn", Pid = pid, Proto = "TCP", Ip = ip, Port = port, Open = open });
    }

    private static int _ticks;

    private static void Flush()
    {
        // Runs on a timer thread: nothing here may take the elevated process down.
        try { FlushCore(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _session?.Stop(); }
    }

    private static void FlushCore()
    {
        if (++_ticks % 30 == 0) RefreshLocalIps();
        if (_parent is null || _parent.HasExited) { _session?.Stop(); return; }

        Dictionary<(int Pid, string Proto, string Ip, int Port), (long Down, long Up)> batch;
        lock (Gate)
        {
            batch = _flows;
            _flows = new();
        }
        var flows = batch.Select(f => new FlowDelta(f.Key.Pid, f.Key.Proto, f.Key.Ip, f.Key.Port, f.Value.Down, f.Value.Up)).ToList();
        Dictionary<int, string>? names = null;
        foreach (int pid in flows.Select(f => f.Pid).Distinct())
        {
            if (_knownNames.ContainsKey(pid)) continue;
            string name = NameOf(pid);
            _knownNames[pid] = name;
            (names ??= new())[pid] = name;
        }
        // Sent every second even when empty: it is also the heartbeat that detects a closed pipe.
        Send(new AgentMessage { Kind = "tick", Flows = flows, Names = names });
    }

    private static string NameOf(int pid)
    {
        if (pid == 4) return "Sistema";
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return $"pid {pid}"; }
    }

    private static void RefreshLocalIps()
    {
        var set = new HashSet<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var a in ni.GetIPProperties().UnicastAddresses) set.Add(a.Address.ToString());
        _localIps = set;
    }

    private static void Send(AgentMessage message)
    {
        lock (WriteGate)
        {
            try
            {
                _out!.WriteLine(JsonSerializer.Serialize(message, AgentMessage.Json));
                _out.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The app closed the pipe: stop tracing and let Run return.
                _session?.Stop();
            }
        }
    }
}
