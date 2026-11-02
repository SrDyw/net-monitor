using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.Services;

public enum ProbeTransport
{
    None,
    Icmp,
    Tcp,
    Http,
}

public readonly record struct ProbeResult(bool Reachable, double? Ms, ProbeTransport Transport);

/// <summary>
/// Probes the configured host. VPNs, proxies and corporate firewalls often drop ICMP,
/// so a failed ping is retried over TCP (443) and finally over HTTP through the system
/// proxy before the link is called unreachable.
/// </summary>
public sealed class LatencyProbe
{
    private const string Fallback = "8.8.8.8";
    private const int TimeoutMs = 1500;
    private const int TcpPort = 443;
    private const string ConnectivityUrl = "http://www.msftconnecttest.com/connecttest.txt";

    // Ping's own timeout is not always honoured while adapters come and go; this one is ours.
    private static readonly TimeSpan HardTimeout = TimeSpan.FromMilliseconds(TimeoutMs + 1500);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);

    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = true })
    {
        // Every call carries its own cancellation token; the handler must not time out first.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public string Host { get; set; } = Settings.DefaultPingHost;

    public async Task<ProbeResult> ProbeAsync()
    {
        string host = Host;
        string[] targets = host == Fallback ? new[] { host } : new[] { host, Fallback };

        if (await FirstSuccessAsync(targets.Select(PingAsync)) is { } icmp)
            return new ProbeResult(true, icmp, ProbeTransport.Icmp);

        if (await FirstSuccessAsync(targets.Select(TcpAsync)) is { } tcp)
            return new ProbeResult(true, tcp, ProbeTransport.Tcp);

        if (await HttpReachableAsync())
            return new ProbeResult(true, null, ProbeTransport.Http);

        return new ProbeResult(false, null, ProbeTransport.None);
    }

    /// <summary>Returns the first attempt that answered, or null when every target failed.</summary>
    private static async Task<double?> FirstSuccessAsync(IEnumerable<Task<double?>> attempts)
    {
        var pending = attempts.ToList();
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            if (await done is { } ms) return ms;
        }
        return null;
    }

    private static async Task<double?> PingAsync(string target)
    {
        var ping = new Ping();
        try
        {
            var attempt = ping.SendPingAsync(target, TimeoutMs);
            var finished = await Task.WhenAny(attempt, Task.Delay(HardTimeout));
            if (finished != attempt)
            {
                Log.Write($"Ping a {target} sin respuesta tras {HardTimeout.TotalSeconds:0.#} s; se abandona");
                _ = attempt.ContinueWith(t => { _ = t.Exception; ping.Dispose(); }, TaskScheduler.Default);
                ping = null;
                return null;
            }
            var reply = await attempt;
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        // Any failure (no route, adapter gone, unresolved name) counts as no answer.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex is not PingException) Log.Error($"ping {target}", ex);
            return null;
        }
        finally
        {
            ping?.Dispose();
        }
    }

    /// <summary>ICMP-free round trip: a plain TCP handshake survives most VPN and firewall setups.</summary>
    private static async Task<double?> TcpAsync(string target)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeoutMs);
        var clock = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(target, TcpPort, timeout.Token);
            return clock.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Last resort: goes through the system proxy, which is the only route on some networks.</summary>
    private static async Task<bool> HttpReachableAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(HttpTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, ConnectivityUrl);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return false;
            // Validate the body so a captive portal answering 200 does not read as internet.
            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Contains("Microsoft Connect Test", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return false;
        }
    }
}
