using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using NetMonitor.Services;

namespace NetMonitor.Controls;

/// <summary>
/// Traffic drawn as a tide around a chart datum: download floods above it, upload
/// ebbs below it. Each side is cut into three flat depth bands at its contour
/// marks. Outages stay on the chart as land bands; nothing is erased.
/// </summary>
public sealed class TideChart : FrameworkElement
{
    private const int Bands = 3;

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<Sample>), typeof(TideChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowSecondsProperty = DependencyProperty.Register(
        nameof(WindowSeconds), typeof(int), typeof(TideChart),
        new FrameworkPropertyMetadata(300, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(TideChart),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(TideChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Sample>? Samples
    {
        get => (IReadOnlyList<Sample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public int WindowSeconds
    {
        get => (int)GetValue(WindowSecondsProperty);
        set => SetValue(WindowSecondsProperty, value);
    }

    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    /// <summary>Watermark rendering: no labels, contours, or annotations.</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        var ink = new ChartInk(this);
        bool compact = Compact;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        double top = compact ? 1 : 26;
        double axis = compact ? 1 : 42; // annotation lane plus time axis
        double plotH = h - top - axis;
        double datum = top + Math.Round(plotH * 0.62);
        // The scale lives in its own gutter so it never covers the newest water.
        double gutter = compact ? 0 : 68;
        double pw = w - gutter;
        double downH = datum - top;
        double upH = top + plotH - datum;

        var window = Visible();
        if (window.Count < 2)
        {
            ink.HLine(dc, "DatumBrush", 0, w, datum);
            if (!compact)
            {
                var wait = ink.Text("Midiendo…", "MutedBrush", 13);
                dc.DrawText(wait, new Point((w - wait.Width) / 2, datum - wait.Height - 8));
            }
            return;
        }

        DateTime now = window[^1].Time;
        double seconds = WindowSeconds;
        double X(DateTime t) => pw * (1 - (now - t).TotalSeconds / seconds);

        double downMax = 0, upMax = 0;
        int downPeak = 0, upPeak = 0;
        for (int i = 0; i < window.Count; i++)
        {
            if (window[i].DownBps > downMax) { downMax = window[i].DownBps; downPeak = i; }
            if (window[i].UpBps > upMax) { upMax = window[i].UpBps; upPeak = i; }
        }
        double downStep = ChartInk.NiceCeiling(Math.Max(downMax, 300e3) * 1.05 / Bands);
        double upStep = ChartInk.NiceCeiling(Math.Max(upMax, 150e3) * 1.05 / Bands);
        double downScale = downStep * Bands, upScale = upStep * Bands;

        // Contour rules sit under the water so they only show over dry chart.
        if (!compact)
        {
            for (int b = 1; b <= Bands; b++)
            {
                ink.HLine(dc, "RuleBrush", 0, pw, datum - downH * b / Bands);
                ink.HLine(dc, "RuleBrush", 0, pw, datum + upH * b / Bands);
            }
        }

        foreach (var (from, to) in Runs(window, outage: false))
        {
            var down = Water(window, from, to, X, s => datum - s.DownBps / downScale * downH, datum);
            var up = Water(window, from, to, X, s => datum + s.UpBps / upScale * upH, datum);
            for (int b = 0; b < Bands; b++)
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, datum - downH * (b + 1) / Bands, pw, downH / Bands)));
                dc.DrawGeometry(ink.Brush($"Down{b + 1}Brush"), null, down);
                dc.Pop();
                dc.PushClip(new RectangleGeometry(new Rect(0, datum + upH * b / Bands, pw, upH / Bands)));
                dc.DrawGeometry(ink.Brush($"Up{b + 1}Brush"), null, up);
                dc.Pop();
            }
        }

        foreach (var (from, to) in Runs(window, outage: true))
            DrawOutage(dc, ink, window, from, to, X, top, top + plotH, compact);

        ink.HLine(dc, "DatumBrush", 0, pw, datum);

        if (compact) return;

        for (int b = 1; b <= Bands; b++)
        {
            var ld = ink.Text(Format.RateText(downStep * b), "MutedBrush", 11);
            ink.Tag(dc, ld, new Point(pw + 8, datum - downH * b / Bands - ld.Height / 2));
            var lu = ink.Text(Format.RateText(upStep * b), "MutedBrush", 11);
            ink.Tag(dc, lu, new Point(pw + 8, datum + upH * b / Bands - lu.Height / 2));
        }

        var downLabel = ink.Text("BAJADA", "InkBrush", 11, bold: true);
        ink.Tag(dc, downLabel, new Point(3, datum - downLabel.Height - 1));
        var upLabel = ink.Text("SUBIDA", "InkBrush", 11, bold: true);
        ink.Tag(dc, upLabel, new Point(3, datum + 2));

        if (downMax > 0)
        {
            var p = window[downPeak];
            double x = X(p.Time), y = datum - p.DownBps / downScale * downH;
            var label = ink.Text($"PLEAMAR {Format.RateText(p.DownBps)} · {p.Time:HH:mm:ss}", "InkBrush", 12, bold: true);
            double lx = Math.Clamp(x + 6, 3, pw - label.Width - 3);
            ink.VLine(dc, "InkBrush", x, 2 + label.Height / 2, y);
            ink.Tag(dc, label, new Point(lx, 2));
        }

        if (upMax > 0)
        {
            var p = window[upPeak];
            double x = X(p.Time), y = datum + p.UpBps / upScale * upH;
            var label = ink.Text($"MÁX. SUBIDA {Format.RateText(p.UpBps)}", "InkBrush", 12, bold: true);
            double ly = top + plotH + 2;
            double lx = Math.Clamp(x + 6, 3, pw - label.Width - 3);
            ink.VLine(dc, "InkBrush", x, y, ly + label.Height / 2);
            ink.Tag(dc, label, new Point(lx, ly));
        }

        DrawAxis(dc, ink, pw, h, seconds);
    }

    private List<Sample> Visible()
    {
        var all = Samples;
        var result = new List<Sample>();
        if (all is null || all.Count == 0) return result;
        DateTime cutoff = all[^1].Time.AddSeconds(-WindowSeconds - 1);
        for (int i = all.Count - 1; i >= 0 && all[i].Time >= cutoff; i--) result.Add(all[i]);
        result.Reverse();
        return result;
    }

    private static IEnumerable<(int From, int To)> Runs(List<Sample> s, bool outage)
    {
        int i = 0;
        while (i < s.Count)
        {
            if (s[i].Outage != outage) { i++; continue; }
            int start = i;
            while (i < s.Count && s[i].Outage == outage) i++;
            yield return (start, i - 1);
        }
    }

    private static StreamGeometry Water(List<Sample> s, int from, int to, Func<DateTime, double> x, Func<Sample, double> y, double datum)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x(s[from].Time), datum), isFilled: true, isClosed: true);
            for (int i = from; i <= to; i++) ctx.LineTo(new Point(x(s[i].Time), y(s[i])), false, false);
            ctx.LineTo(new Point(x(s[to].Time), datum), false, false);
        }
        g.Freeze();
        return g;
    }

    private static void DrawOutage(DrawingContext dc, ChartInk ink, List<Sample> s, int from, int to,
        Func<DateTime, double> x, double y0, double y1, bool compact)
    {
        double x0 = Math.Floor(x(s[from].Time.AddSeconds(-1)));
        double x1 = Math.Ceiling(x(s[to].Time));
        var band = new Rect(x0, y0, Math.Max(1, x1 - x0), y1 - y0);
        dc.DrawRectangle(ink.Brush("LandBrush"), null, band);

        // Coastline: one device-pixel edge where the water meets the land.
        if (!compact)
        {
            ink.VLine(dc, "LandInkBrush", band.Left, y0, y1);
            ink.VLine(dc, "LandInkBrush", band.Right - ink.Hairline, y0, y1);
        }

        if (compact) return;
        // One sample per second: the same count the session total uses.
        var duration = TimeSpan.FromSeconds(to - from + 1);
        var title = ink.Text("CORTE", "LandInkBrush", 12, bold: true);
        var span = ink.Text(Format.Duration(duration), "LandInkBrush", 12);
        if (band.Width < Math.Max(title.Width, span.Width) + 8) return;
        dc.PushClip(new RectangleGeometry(band));
        dc.DrawRectangle(ink.Brush("LandBrush"), null, new Rect(x0 + 2, y0 + 4, Math.Max(title.Width, span.Width) + 6, title.Height + span.Height + 4));
        dc.DrawText(title, new Point(x0 + 5, y0 + 5));
        dc.DrawText(span, new Point(x0 + 5, y0 + 5 + title.Height));
        dc.Pop();
    }

    private static void DrawAxis(DrawingContext dc, ChartInk ink, double w, double h, double seconds)
    {
        const int Ticks = 5;
        double baseline = h - 22;
        ink.HLine(dc, "RuleStrongBrush", 0, w, baseline);
        for (int i = 0; i <= Ticks; i++)
        {
            double back = seconds * (Ticks - i) / Ticks;
            string text = i == Ticks ? "ahora" : back < 60 ? $"−{back:0} s" : $"−{back / 60:0.#} min";
            var label = ink.Text(text, "MutedBrush", 11);
            double x = w * i / Ticks;
            ink.VLine(dc, "RuleStrongBrush", Math.Min(x, w - 1), baseline, baseline + 4);
            double lx = i == 0 ? 0 : i == Ticks ? w - label.Width : x - label.Width / 2;
            dc.DrawText(label, new Point(lx, baseline + 6));
        }
    }
}
