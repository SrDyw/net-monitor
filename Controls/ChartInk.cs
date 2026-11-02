using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NetMonitor.Controls;

/// <summary>Shared drawing helpers: device-pixel snapping and chart lettering.</summary>
internal readonly struct ChartInk
{
    private readonly FrameworkElement _owner;
    public readonly double PixelsPerDip;
    public readonly double Hairline;

    private static readonly Typeface Face = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface FaceBold = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public ChartInk(FrameworkElement owner)
    {
        _owner = owner;
        PixelsPerDip = VisualTreeHelper.GetDpi(owner).PixelsPerDip;
        Hairline = 1 / PixelsPerDip;
    }

    /// <summary>Centre of the device pixel nearest to <paramref name="v"/>, so a 1px line lands on exactly one pixel row.</summary>
    public double Snap(double v) => (Math.Floor(v * PixelsPerDip) + 0.5) / PixelsPerDip;

    public Brush Brush(string key) =>
        _owner.TryFindResource(key) as Brush ?? Brushes.Magenta;

    public Pen Pen(string key) => new(Brush(key), Hairline);

    public FormattedText Text(string text, string brushKey, double size = 11, bool bold = false) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? FaceBold : Face, size, Brush(brushKey), PixelsPerDip);

    public void HLine(DrawingContext dc, string key, double x0, double x1, double y) =>
        dc.DrawLine(Pen(key), new Point(x0, Snap(y)), new Point(x1, Snap(y)));

    public void VLine(DrawingContext dc, string key, double x, double y0, double y1) =>
        dc.DrawLine(Pen(key), new Point(Snap(x), y0), new Point(Snap(x), y1));

    /// <summary>Text set on a patch of chart ground, so it stays legible over water or land.</summary>
    public void Tag(DrawingContext dc, FormattedText text, Point at)
    {
        dc.DrawRectangle(Brush("GroundBrush"), null, new Rect(at.X - 3, at.Y, text.Width + 6, text.Height));
        dc.DrawText(text, at);
    }

    /// <summary>Rounds a contour step up to 1, 2, 2.5 or 5 × 10ⁿ.</summary>
    public static double NiceCeiling(double v)
    {
        if (v <= 0) return 1;
        double exp = Math.Pow(10, Math.Floor(Math.Log10(v)));
        double f = v / exp;
        double nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
        return nice * exp;
    }
}
