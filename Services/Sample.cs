using System;

namespace NetMonitor.Services;

/// <summary>One second of traffic. Rates are in bits per second.</summary>
public readonly record struct Sample(DateTime Time, double DownBps, double UpBps, double? LatencyMs, bool Outage);

public enum LinkState
{
    Measuring,
    Connected,
    HighLatency,
    NoInternet,
    NoNetwork,
}
