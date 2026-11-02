using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using NetMonitor.Services;

namespace NetMonitor.Controls;

public enum TraceKind
{
    Down,
    Up,
    Total,
    Latency,
}

/// <summary>The last minute of one series as a single device-pixel line: the proof under each number.</summary>
public sealed class Trace : FrameworkElement
{
    private const int Seconds = 60;

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<Sample>), typeof(Trace),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(Trace),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
        nameof(Threshold), typeof(int), typeof(Trace),
        new FrameworkPropertyMetadata(100, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The high-latency line, in ms; only used by the latency trace.</summary>
    public int Threshold
    {
        get => (int)GetValue(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(TraceKind), typeof(Trace),
        new FrameworkPropertyMetadata(TraceKind.Down, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Sample>? Samples
    {
        get => (IReadOnlyList<Sample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public TraceKind Kind
    {
        get => (TraceKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var all = Samples;
        if (all is null || all.Count < 2 || w < 2) return;

        var ink = new ChartInk(this);
        // Baseline across the full minute, so a young trace reads as the start of a line.
        ink.HLine(dc, "RuleBrush", 0, w, h - 1);
        int start = Math.Max(0, all.Count - Seconds);
        double max = 0;
        for (int i = start; i < all.Count; i++) max = Math.Max(max, Value(all[i]) ?? 0);
        if (Kind == TraceKind.Latency) max = Math.Max(max, Threshold * 1.5);
        max = Math.Max(max * 1.1, 1);

        string key = Kind switch
        {
            TraceKind.Down => "Down3Brush",
            TraceKind.Up => "Up3Brush",
            TraceKind.Total => "InkBrush",
            _ => "MutedBrush",
        };
        var pen = new Pen(ink.Brush(key), ink.Hairline * 1.5);
        var alarm = new Pen(ink.Brush("AlertBrush"), ink.Hairline * 1.5);

        if (Kind == TraceKind.Latency)
        {
            // The line the state strip reports against.
            double limit = h - h * Threshold / max;
            dc.DrawLine(new Pen(ink.Brush("RuleBrush"), ink.Hairline) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) },
                new Point(0, ink.Snap(limit)), new Point(w, ink.Snap(limit)));
        }

        Point? prev = null;
        for (int i = start; i < all.Count; i++)
        {
            double x = w * (i - (all.Count - Seconds)) / (Seconds - 1);
            double? v = Value(all[i]);
            if (v is null) { prev = null; continue; }
            var p = new Point(x, h - 1 - (h - 2) * v.Value / max);
            if (prev is { } q)
            {
                bool high = Kind == TraceKind.Latency && v > Threshold;
                dc.DrawLine(high ? alarm : pen, q, p);
            }
            prev = p;
        }
    }

    private double? Value(Sample s) => Kind switch
    {
        TraceKind.Down => s.Outage ? null : s.DownBps,
        TraceKind.Up => s.Outage ? null : s.UpBps,
        TraceKind.Total => s.Outage ? null : s.DownBps + s.UpBps,
        _ => s.LatencyMs,
    };
}
