using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NetMonitor.Services;

/// <summary>
/// Paints the tray menu from the active chart plate: flat ground, ink text,
/// one-pixel rules, no gradients. Values mirror Themes/Night.xaml and Day.xaml.
/// </summary>
internal sealed class PlateMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Color _ink;

    public PlateMenuRenderer(bool dark) : base(new PlateColors(dark))
    {
        RoundedEdges = false;
        _ink = dark ? Hex(0xE6F0F6) : Hex(0x0B1F33);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = _ink;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _ink;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // A drawn tick in ink, centred in the image margin.
        var r = e.ImageRectangle;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_ink, 1.6f);
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
        g.DrawLines(pen, new[]
        {
            new PointF(cx - 4, cy), new PointF(cx - 1, cy + 3), new PointF(cx + 4, cy - 3),
        });
    }

    internal static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private sealed class PlateColors : ProfessionalColorTable
    {
        private readonly Color _ground, _hover, _rule;

        public PlateColors(bool dark)
        {
            UseSystemColors = false;
            _ground = dark ? Hex(0x08192A) : Hex(0xE8F0F5);
            _hover = dark ? Hex(0x1C3851) : Hex(0xD5E2EA);
            _rule = dark ? Hex(0x3A6489) : Hex(0x9DB7CA);
        }

        public override Color ToolStripDropDownBackground => _ground;
        public override Color ImageMarginGradientBegin => _ground;
        public override Color ImageMarginGradientMiddle => _ground;
        public override Color ImageMarginGradientEnd => _ground;
        public override Color MenuBorder => _rule;
        public override Color MenuItemBorder => _hover;
        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;
        public override Color MenuItemPressedGradientBegin => _hover;
        public override Color MenuItemPressedGradientEnd => _hover;
        public override Color SeparatorDark => _rule;
        public override Color SeparatorLight => _ground;
        public override Color CheckBackground => _ground;
        public override Color CheckSelectedBackground => _hover;
        public override Color CheckPressedBackground => _hover;
    }
}
