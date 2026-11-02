using System;

namespace NetMonitor.Services;

public static class Format
{
    /// <summary>Show rates in bytes per second (MB/s) instead of bits (Mb/s).</summary>
    public static bool RatesInBytes { get; set; }

    /// <summary>Splits a bit rate into a short number and its unit, in the chosen unit family.</summary>
    public static (string Value, string Unit) Rate(double bps)
    {
        double v = RatesInBytes ? bps / 8 : bps;
        string b = RatesInBytes ? "B/s" : "b/s";
        if (v < 1e6)
        {
            double k = v / 1e3;
            return (k < 10 ? k.ToString("0.0") : k.ToString("0"), "k" + b);
        }
        if (v < 1e9)
        {
            double m = v / 1e6;
            return (m < 100 ? m.ToString("0.0") : m.ToString("0"), "M" + b);
        }
        return ((v / 1e9).ToString("0.00"), "G" + b);
    }

    public static string RateText(double bps)
    {
        var (value, unit) = Rate(bps);
        return $"{value} {unit}";
    }

    public static string Bytes(long bytes) => bytes switch
    {
        < 1_000_000 => $"{bytes / 1e3:0} kB",
        < 10_000_000 => $"{bytes / 1e6:0.0} MB",
        < 1_000_000_000 => $"{bytes / 1e6:0} MB",
        _ => $"{bytes / 1e9:0.00} GB",
    };

    public static string Duration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes:00} min";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}:{span.Seconds:00}";
        return $"{span.Seconds} s";
    }
}
