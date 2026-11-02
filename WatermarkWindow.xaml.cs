using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NetMonitor.Interop;
using NetMonitor.Services;

namespace NetMonitor;

/// <summary>
/// Compact always-on-top overlay. The mouse passes through it unless Ctrl+Alt is held:
/// then a drag moves it and a click opens the main window; the offset is saved.
/// </summary>
public partial class WatermarkWindow : Window
{
    private const double Inset = 16;
    private const int ClickSlop = 3;
    private Corner _corner = Corner.BottomRight;
    private double _offsetX, _offsetY;
    private IntPtr _hwnd;
    private bool _interactive;
    private bool _movable;
    private bool _moveMode;
    private bool _pressed;
    private bool _dragging;
    private int _dragStartX, _dragStartY;
    private double _dragLeft, _dragTop, _dragScaleX = 1, _dragScaleY = 1;
    private bool _nativeDrag;
    private int _dragRectLeft, _dragRectTop, _dragWidth, _dragHeight;
    private DispatcherTimer? _modifierWatch;
    private DispatcherTimer? _moveTimeout;

    /// <summary>Raised after a drag, with the new corner-relative offset ready in <see cref="OffsetX"/>.</summary>
    public event EventHandler? OffsetChanged;

    /// <summary>Raised when Ctrl+Alt+click is released without dragging.</summary>
    public event EventHandler? ActivateRequested;

    public WatermarkWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeClickThrough(_hwnd);
            StartModifierWatch();
        };
        SizeChanged += (_, _) => Reposition();
        Loaded += (_, _) => Reposition();
        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        IsVisibleChanged += (_, _) => UpdateHitTest();
        LostMouseCapture += (_, _) =>
        {
            if (_pressed && !_dragging) _pressed = false;
            else FinishDrag();
        };
        Closed += (_, _) =>
        {
            _modifierWatch?.Stop();
            _moveTimeout?.Stop();
        };
    }

    public Corner Corner
    {
        get => _corner;
        set
        {
            _corner = value;
            Reposition();
        }
    }

    public double OffsetX => _offsetX;
    public double OffsetY => _offsetY;

    /// <summary>Sets the saved offset without raising <see cref="OffsetChanged"/>.</summary>
    public void SetOffset(double x, double y)
    {
        _offsetX = x;
        _offsetY = y;
        Reposition();
    }

    /// <summary>Tray command: unlock the plate for a few seconds so it can be dragged.</summary>
    public void BeginMove()
    {
        _moveMode = true;
        UpdateHitTest();
        _moveTimeout?.Stop();
        _moveTimeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
        _moveTimeout.Tick += (_, _) => EndMove();
        _moveTimeout.Start();
    }

    private void EndMove()
    {
        _moveMode = false;
        _moveTimeout?.Stop();
        if (_dragging) return;
        UpdateHitTest();
    }

    /// <summary>Pins the overlay inside the primary work area, clear of the taskbar.</summary>
    public void Reposition()
    {
        var area = SystemParameters.WorkArea;
        double w = ActualWidth, h = ActualHeight;
        bool right = _corner is Corner.BottomRight or Corner.TopRight;
        bool bottom = _corner is Corner.BottomRight or Corner.BottomLeft;
        double left = (right ? area.Right - w - Inset : area.Left + Inset) + _offsetX;
        double top = (bottom ? area.Bottom - h - Inset : area.Top + Inset) + _offsetY;
        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - w));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - h));
    }

    /// <summary>Ctrl+Alt is the drag handle; hovering also makes the plate clickable.</summary>
    private void StartModifierWatch()
    {
        _modifierWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _modifierWatch.Tick += (_, _) =>
        {
            if (!_dragging) UpdateHitTest();
        };
        _modifierWatch.Start();
    }

    private static bool ModifierHeld => Native.IsKeyDown(Native.VK_CONTROL) && Native.IsKeyDown(Native.VK_MENU);

    private void UpdateHitTest()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool interactive = IsVisible && (_moveMode || ModifierHeld);
        _movable = interactive;
        if (interactive != _interactive)
        {
            _interactive = interactive;
            Native.SetClickThrough(_hwnd, !interactive);
        }
        Cursor = interactive ? Cursors.SizeAll : null;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_interactive || !_movable) return;
        if (!Native.GetCursorPos(out var point)) return;
        if (!CaptureMouse()) Native.SetCapture(_hwnd);
        _pressed = true;
        _dragging = false;
        _dragStartX = point.X;
        _dragStartY = point.Y;

        // Physical HWND movement is the only reliable path for a layered, no-activate window.
        _nativeDrag = Native.TryGetWindowRect(_hwnd, out var rect);
        if (_nativeDrag)
        {
            _dragRectLeft = rect.Left;
            _dragRectTop = rect.Top;
            _dragWidth = rect.Right - rect.Left;
            _dragHeight = rect.Bottom - rect.Top;
        }
        else
        {
            _dragLeft = Left;
            _dragTop = Top;
            var dpi = VisualTreeHelper.GetDpi(this);
            _dragScaleX = dpi.DpiScaleX;
            _dragScaleY = dpi.DpiScaleY;
        }
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || !Native.GetCursorPos(out var point)) return;
        if (!_dragging)
        {
            // Distinguish Ctrl+Alt+click (opens the window) from Ctrl+Alt+drag (moves it).
            if (Math.Max(Math.Abs(point.X - _dragStartX), Math.Abs(point.Y - _dragStartY)) <= ClickSlop) return;
            _dragging = true;
        }
        if (_nativeDrag)
        {
            int x = _dragRectLeft + (point.X - _dragStartX);
            int y = _dragRectTop + (point.Y - _dragStartY);
            if (Native.TryGetWorkArea(_hwnd, out var area))
            {
                x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - _dragWidth));
                y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - _dragHeight));
            }
            Native.MoveWindowTo(_hwnd, x, y);
        }
        else
        {
            // GetCursorPos is in physical pixels; Window.Left/Top are DIPs.
            Left = _dragLeft + (point.X - _dragStartX) / _dragScaleX;
            Top = _dragTop + (point.Y - _dragStartY) / _dragScaleY;
            ClampToWorkArea();
        }
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        if (_dragging)
        {
            FinishDrag();
            e.Handled = true;
            return;
        }

        ReleaseMouseCapture();
        Native.ReleaseCapture();
        e.Handled = true;
        if (ModifierHeld) ActivateRequested?.Invoke(this, EventArgs.Empty);
        else EndMove(); // a plain click in tray move mode just locks it again
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _pressed = false;
        ReleaseMouseCapture();
        Native.ReleaseCapture();
        if (_nativeDrag && Native.TryGetWindowRect(_hwnd, out var rect))
        {
            // The HWND moved directly; bring WPF's cached DIP position into step before saving.
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = rect.Left / dpi.DpiScaleX;
            Top = rect.Top / dpi.DpiScaleY;
        }
        ClampToWorkArea();
        SaveOffset();
        if (_moveMode || !ModifierHeld) EndMove();
        else UpdateHitTest();
    }

    private void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    private void SaveOffset()
    {
        var area = SystemParameters.WorkArea;
        bool right = _corner is Corner.BottomRight or Corner.TopRight;
        bool bottom = _corner is Corner.BottomRight or Corner.BottomLeft;
        double anchorLeft = right ? area.Right - ActualWidth - Inset : area.Left + Inset;
        double anchorTop = bottom ? area.Bottom - ActualHeight - Inset : area.Top + Inset;
        _offsetX = Math.Round(Left - anchorLeft);
        _offsetY = Math.Round(Top - anchorTop);
        OffsetChanged?.Invoke(this, EventArgs.Empty);
    }
}
