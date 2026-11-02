using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetMonitor.Services;

public readonly record struct TrafficReading(
    bool Valid,
    double DownBps,
    double UpBps,
    long DownBytes,
    long UpBytes,
    bool HasNetwork,
    string? AdapterName,
    bool IsWireless,
    string? LocalAddress,
    string? Gateway);

/// <summary>
/// Sums byte counters across the adapters that carry the default route. VPN tunnels only
/// replace the physical link when nothing else has a gateway, so the same bytes are never
/// counted twice (once encrypted on the NIC, once decrypted on the tunnel).
/// </summary>
public sealed class TrafficSampler
{
    private sealed record Live(
        NetworkInterface Ni,
        IPInterfaceProperties Props,
        IPInterfaceStatistics Stats,
        bool HasGateway);

    private Dictionary<string, (long Rx, long Tx)> _last = new();
    private DateTime _lastTime;

    public TrafficReading Read()
    {
        var now = DateTime.UtcNow;
        var seen = new Dictionary<string, (long Rx, long Tx)>();
        long downBytes = 0, upBytes = 0;

        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { all = Array.Empty<NetworkInterface>(); }

        var live = new List<Live>(all.Length);
        foreach (var ni in all)
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            try
            {
                var props = ni.GetIPProperties();
                live.Add(new Live(ni, props, ni.GetIPStatistics(), HasGateway(props)));
            }
            catch (NetworkInformationException) { continue; }
        }

        // Physical links with a gateway first. If a VPN owns the default route, its tunnel
        // takes over; only when no adapter has a gateway at all does the link still count.
        var chosen = live.Where(a => a.HasGateway && a.Ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToList();
        if (chosen.Count == 0) chosen = live.Where(a => a.HasGateway).ToList();
        if (chosen.Count == 0) chosen = live;

        NetworkInterface? primary = null;
        IPInterfaceProperties? primaryProps = null;
        long primaryTotal = -1;

        foreach (var adapter in chosen)
        {
            long rx = adapter.Stats.BytesReceived, tx = adapter.Stats.BytesSent;
            seen[adapter.Ni.Id] = (rx, tx);

            if (_last.TryGetValue(adapter.Ni.Id, out var prev))
            {
                // Counters reset when an adapter reconnects; ignore negative deltas.
                if (rx >= prev.Rx) downBytes += rx - prev.Rx;
                if (tx >= prev.Tx) upBytes += tx - prev.Tx;
            }

            if (rx + tx > primaryTotal)
            {
                primary = adapter.Ni;
                primaryProps = adapter.Props;
                primaryTotal = rx + tx;
            }
        }

        bool valid = _lastTime != default;
        double seconds = valid ? Math.Max((now - _lastTime).TotalSeconds, 0.001) : 1;
        _last = seen;
        _lastTime = now;

        return new TrafficReading(
            valid,
            valid ? downBytes * 8 / seconds : 0,
            valid ? upBytes * 8 / seconds : 0,
            valid ? downBytes : 0,
            valid ? upBytes : 0,
            primary is not null,
            primary?.Name,
            primary?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
            primaryProps?.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString(),
            primaryProps?.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))?.Address.ToString());
    }

    private static bool HasGateway(IPInterfaceProperties props) =>
        props.GatewayAddresses.Any(g =>
            !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
}
