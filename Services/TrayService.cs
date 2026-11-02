using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetMonitor.Services;

/// <summary>Notification-area icon: a small tide tile whose water turns magenta when the link is down.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    /// <summary>The context menu, exposed so <c>--capture</c> can photograph it.</summary>
    internal ContextMenuStrip Menu { get; }
    private readonly Icon _calm;
    private readonly Icon _alert;
    private readonly ToolStripMenuItem _watermarkItem;
    private readonly ToolStripMenuItem[] _cornerItems;
    private readonly ToolStripMenuItem _startupItem;
    private bool _alerting;

    public event EventHandler? ShowRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ProcessesRequested;
    public event EventHandler? ToggleModeRequested;
    public event EventHandler? MoveRequested;
    public event EventHandler<Corner>? CornerRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? StartupToggled;

    public TrayService(bool hotkeyRegistered)
    {
        _calm = Tile(Color.FromArgb(0xA8, 0xCF, 0xE0));
        _alert = Tile(Color.FromArgb(0xEC, 0x6A, 0xAE));

        var menu = new ContextMenuStrip { Renderer = new PlateMenuRenderer(dark: true) };
        menu.Items.Add("Mostrar ventana", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Inspeccionar proceso…", null, (_, _) => ProcessesRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Configuración…", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _watermarkItem = new ToolStripMenuItem("Modo marca de agua", null, (_, _) => ToggleModeRequested?.Invoke(this, EventArgs.Empty))
        {
            ShortcutKeyDisplayString = hotkeyRegistered ? "Ctrl+Alt+N" : "atajo no disponible",
        };
        menu.Items.Add(_watermarkItem);
        menu.Items.Add(new ToolStripMenuItem("Mover marca de agua", null, (_, _) => MoveRequested?.Invoke(this, EventArgs.Empty)));

        var corners = new ToolStripMenuItem("Esquina de la marca de agua");
        (string Label, Corner Corner)[] options =
        {
            ("Abajo a la derecha", Corner.BottomRight),
            ("Abajo a la izquierda", Corner.BottomLeft),
            ("Arriba a la derecha", Corner.TopRight),
            ("Arriba a la izquierda", Corner.TopLeft),
        };
        _cornerItems = new ToolStripMenuItem[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            var corner = options[i].Corner;
            _cornerItems[i] = new ToolStripMenuItem(options[i].Label, null, (_, _) => CornerRequested?.Invoke(this, corner)) { Tag = corner };
            corners.DropDownItems.Add(_cornerItems[i]);
        }
        menu.Items.Add(corners);
        _startupItem = new ToolStripMenuItem("Iniciar con Windows", null, (_, _) => StartupToggled?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        Menu = menu;

        _icon = new NotifyIcon
        {
            Icon = _calm,
            Text = "Net Monitor",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Repaints the menu from the day or night plate.</summary>
    public void SetTheme(bool dark) => Menu.Renderer = new PlateMenuRenderer(dark);

    public void Update(string summary, bool alert, bool watermark, Corner corner, bool startsWithWindows = false)
    {
        _startupItem.Checked = startsWithWindows;
        // NotifyIcon caps tooltip text at 127 characters.
        string text = $"Net Monitor · {summary}";
        _icon.Text = text.Length > 127 ? text[..127] : text;
        if (alert != _alerting)
        {
            _icon.Icon = alert ? _alert : _calm;
            _alerting = alert;
        }
        _watermarkItem.Checked = watermark;
        foreach (var item in _cornerItems) item.Checked = (Corner)item.Tag! == corner;
    }

    /// <summary>Draws the icon: deep-water square, datum rule, and a flat tide band.</summary>
    private static Icon Tile(Color water)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(0x0B, 0x1F, 0x33));
            using var path = new GraphicsPath();
            path.AddLines(new[]
            {
                new PointF(2, 20), new PointF(2, 15), new PointF(8, 12), new PointF(14, 6),
                new PointF(19, 9), new PointF(24, 13), new PointF(30, 11), new PointF(30, 20),
            });
            path.CloseFigure();
            using var fill = new SolidBrush(water);
            g.FillPath(fill, path);
            using var ebb = new SolidBrush(Color.FromArgb(0x4E, 0x8F, 0x7C));
            g.FillRectangle(ebb, 2, 21, 28, 5);
            using var datum = new Pen(Color.FromArgb(0xF2, 0xF7, 0xFA), 2);
            g.DrawLine(datum, 0, 20.5f, 32, 20.5f);
        }
        IntPtr handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _calm.Dispose();
        _alert.Dispose();
    }
}
