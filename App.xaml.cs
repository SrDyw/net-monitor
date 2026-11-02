using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NetMonitor.Agent;
using NetMonitor.Services;
using NetMonitor.ViewModels;

namespace NetMonitor;

public partial class App : Application
{
    private Mutex? _single;
    private PreferencesViewModel _prefs = null!;
    private MonitorViewModel _vm = null!;
    private MainWindow _main = null!;
    private WatermarkWindow _watermark = null!;
    private HotkeyService? _hotkey;
    private TrayService? _tray;
    private PinnedLogOverlay? _pinnedLog;
    private bool _isWatermark;
    private readonly ProcessInspector _inspector = new();

    public static ThemeService Theme { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A bad UI callback must log and keep the monitor alive, not close it.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("ui", args.Exception);
            args.Handled = true;
        };

        // Elevated helper for the process inspector: no UI, no single-instance lock.
        if (ArgAfter(e.Args, EtwAgent.Flag) is { } pipe)
        {
            int parent = int.TryParse(ArgAfter(e.Args, pipe), out int p) ? p : 0;
            Shutdown(EtwAgent.Run(pipe, parent));
            return;
        }

        string? captureDir = ArgAfter(e.Args, "--capture");

        if (captureDir is null)
        {
            _single = new Mutex(true, "NetMonitor.SingleInstance", out bool first);
            if (!first)
            {
                Shutdown();
                return;
            }
            StartupService.Refresh();
        }

        _prefs = new PreferencesViewModel(Settings.Load());
        Theme = new ThemeService(Resources);
        Theme.Mode = captureDir is null ? _prefs.Theme
            : e.Args.Contains("--light") ? Services.ThemeMode.Light : Services.ThemeMode.Dark;
        Format.RatesInBytes = _prefs.RatesInBytes;

        _vm = new MonitorViewModel
        {
            Prefs = _prefs,
            RangeMinutes = _prefs.RangeMinutes,
            HighLatencyMs = _prefs.HighLatencyMs,
            PingHost = _prefs.PingHost,
            Processes = new ProcessesViewModel(_inspector, _prefs.PinnedProcessNames,
                names => _prefs.PinnedProcessNames = names),
        };
        _main = new MainWindow { DataContext = _vm };
        _main.StateChanged += (_, _) =>
        {
            // Minimizing the window hands over to the overlay and leaves the taskbar.
            if (_main.WindowState == WindowState.Minimized) EnterWatermark();
        };
        _watermark = new WatermarkWindow { DataContext = _vm, Corner = _prefs.WatermarkCorner };
        _watermark.SetOffset(_prefs.WatermarkOffsetX, _prefs.WatermarkOffsetY);
        _watermark.ActivateRequested += (_, _) => ShowMain();
        _watermark.OffsetChanged += (_, _) =>
        {
            _prefs.WatermarkOffsetX = _watermark.OffsetX;
            _prefs.WatermarkOffsetY = _watermark.OffsetY;
        };
        Theme.Changed += (_, _) => _main.ApplyTitleBar(Theme.IsDark);
        _prefs.PropertyChanged += OnPreferenceChanged;

        if (captureDir is not null)
        {
            Capture(captureDir, e.Args.Contains("--light") ? "light" : "dark");
            return;
        }

        _hotkey = new HotkeyService(new WindowInteropHelper(_main).EnsureHandle());
        _hotkey.Pressed += (_, _) => ToggleMode();

        _tray = new TrayService(_hotkey.IsRegistered);
        _tray.SetTheme(Theme.IsDark);
        Theme.Changed += (_, _) => _tray.SetTheme(Theme.IsDark);
        _tray.ShowRequested += (_, _) => ShowMain();
        _tray.SettingsRequested += (_, _) =>
        {
            _vm.Body = Body.Settings;
            ShowMain();
        };
        _tray.ProcessesRequested += (_, _) =>
        {
            ShowMain();
            _vm.OpenProcessesCommand.Execute(null);
        };
        _tray.ToggleModeRequested += (_, _) => ToggleMode();
        _tray.MoveRequested += (_, _) =>
        {
            if (!_isWatermark) EnterWatermark();
            _watermark.BeginMove();
        };
        _tray.CornerRequested += (_, corner) => _prefs.WatermarkCorner = corner;
        _tray.StartupToggled += (_, _) => _prefs.StartWithWindows = !_prefs.StartWithWindows;
        _tray.ExitRequested += (_, _) => ExitApp();

        _vm.ToggleModeRequested += (_, _) => ToggleMode();
        _vm.PropertyChanged += OnViewModelChanged;
        _vm.Start();
        _pinnedLog = new PinnedLogOverlay(_vm.Processes!, _watermark);
        _pinnedLog.SetOpacity(_prefs.WatermarkOpacity);
        _pinnedLog.SetVisibleCount(_prefs.WatermarkLogLines);

        switch (_prefs.StartView)
        {
            case StartView.Watermark: EnterWatermark(); break;
            case StartView.Tray: UpdateTray(); break;
            default: ShowMain(); break;
        }
    }

    /// <summary>Settings apply the moment they change.</summary>
    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PreferencesViewModel.Theme): Theme.Mode = _prefs.Theme; break;
            case nameof(PreferencesViewModel.RatesInBytes): Format.RatesInBytes = _prefs.RatesInBytes; break;
            case nameof(PreferencesViewModel.HighLatencyMs): _vm.HighLatencyMs = _prefs.HighLatencyMs; break;
            case nameof(PreferencesViewModel.PingHost): _vm.PingHost = _prefs.PingHost; break;
            case nameof(PreferencesViewModel.WatermarkCorner):
                // A new corner is a fresh anchor; dragging adjusts from there and is saved.
                _prefs.WatermarkOffsetX = 0;
                _prefs.WatermarkOffsetY = 0;
                _watermark.Corner = _prefs.WatermarkCorner;
                break;
            case nameof(PreferencesViewModel.WatermarkOffsetX):
            case nameof(PreferencesViewModel.WatermarkOffsetY):
                _watermark.SetOffset(_prefs.WatermarkOffsetX, _prefs.WatermarkOffsetY);
                break;
            case nameof(PreferencesViewModel.WatermarkOpacity):
                _pinnedLog?.SetOpacity(_prefs.WatermarkOpacity);
                break;
            case nameof(PreferencesViewModel.WatermarkLogLines):
                _pinnedLog?.SetVisibleCount(_prefs.WatermarkLogLines);
                break;
        }
        UpdateTray();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonitorViewModel.Revision)) UpdateTray();
        if (e.PropertyName == nameof(MonitorViewModel.RangeMinutes)) _prefs.RangeMinutes = _vm.RangeMinutes;
    }

    private void UpdateTray() =>
        _tray?.Update(_vm.SummaryText, _vm.IsOffline || _vm.IsLatencyHigh, _isWatermark, _watermark.Corner, _prefs.StartWithWindows);

    private void ToggleMode()
    {
        if (_isWatermark) ShowMain();
        else EnterWatermark();
    }

    private void ShowMain()
    {
        _isWatermark = false;
        _pinnedLog?.SetVisible(false);
        _watermark.Hide();
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        UpdateTray();
    }

    private void EnterWatermark()
    {
        _isWatermark = true;
        _main.Hide();
        _watermark.Show();
        _watermark.Reposition();
        _pinnedLog?.SetVisible(true);
        UpdateTray();
    }

    private void ExitApp()
    {
        _vm.Stop();
        _inspector.Dispose();
        _pinnedLog?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        Theme.Dispose();
        _main.AllowClose = true;
        _main.Close();
        _watermark.Close();
        _single?.ReleaseMutex();
        Shutdown();
    }

    /// <summary>
    /// <c>--capture &lt;dir&gt; [--light]</c>: renders both modes with synthetic data to PNG and exits.
    /// Used to review the design without a live connection.
    /// </summary>
    private void Capture(string dir, string variant)
    {
        Directory.CreateDirectory(dir);
        _vm.LoadDemo();
        _main.Show();
        _watermark.Show();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            Save((FrameworkElement)_main.Content, Path.Combine(dir, $"main-{variant}.png"), ((SolidColorBrush)_main.Background).Color);
            // The overlay is judged over the two extremes it will sit on.
            Save((FrameworkElement)_watermark.Content, Path.Combine(dir, "watermark-on-white.png"), Colors.White);
            Save((FrameworkElement)_watermark.Content, Path.Combine(dir, "watermark-on-black.png"), Colors.Black);

            _vm.Body = Body.Settings;
            Settle();
            Save((FrameworkElement)_main.Content, Path.Combine(dir, $"settings-{variant}.png"), ((SolidColorBrush)_main.Background).Color);

            _vm.Processes!.LoadDemo();
            _vm.Body = Body.Processes;
            Settle();
            Save((FrameworkElement)_main.Content, Path.Combine(dir, $"processes-{variant}.png"), ((SolidColorBrush)_main.Background).Color);
            _vm.Processes.Tab = "Log";
            Settle();
            Save((FrameworkElement)_main.Content, Path.Combine(dir, $"processes-log-{variant}.png"), ((SolidColorBrush)_main.Background).Color);
            _vm.Body = Body.Monitor;

            _vm.DemoOffline();
            Settle();
            Save((FrameworkElement)_watermark.Content, Path.Combine(dir, "watermark-offline-on-white.png"), Colors.White);

            _vm.DemoStartup(8);
            Settle();
            Save((FrameworkElement)_main.Content, Path.Combine(dir, $"main-startup-{variant}.png"), ((SolidColorBrush)_main.Background).Color);

            try { CaptureProxy(dir, variant); }
            catch (Exception ex) { Log.Error("capture proxy", ex); }
            CaptureTrayMenu(Path.Combine(dir, $"tray-menu-{variant}.png"));
            _main.AllowClose = true;
            Shutdown();
        });
    }

    private void Settle()
    {
        _main.UpdateLayout();
        _watermark.UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>Renders the modal proxy editor used by the settings view.</summary>
    private void CaptureProxy(string dir, string variant)
    {
        var proxy = new ProxyWindow { Owner = _main };
        proxy.Loaded += (_, _) =>
        {
            proxy.UpdateLayout();
            Save((FrameworkElement)proxy.Content, Path.Combine(dir, $"proxy-{variant}.png"),
                ((SolidColorBrush)proxy.Background).Color);
            proxy.Close();
        };
        proxy.ShowDialog();
    }

    /// <summary>Opens the real tray menu at a known spot and grabs that part of the screen.</summary>
    private void CaptureTrayMenu(string path)
    {
        using var tray = new TrayService(hotkeyRegistered: true);
        tray.SetTheme(Theme.IsDark);
        tray.Update("captura", alert: false, watermark: true, Corner.BottomRight);
        tray.Menu.Show(new System.Drawing.Point(200, 200));
        System.Windows.Forms.Application.DoEvents();
        Thread.Sleep(400);
        System.Windows.Forms.Application.DoEvents();
        var size = tray.Menu.Size;
        using var shot = new System.Drawing.Bitmap(size.Width + 40, size.Height + 40);
        using (var g = System.Drawing.Graphics.FromImage(shot))
            g.CopyFromScreen(180, 180, 0, 0, shot.Size);
        shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        tray.Menu.Close();
    }

    private static void Save(FrameworkElement element, string path, Color background)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var ground = new DrawingVisual();
        using (var dc = ground.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bmp.Render(ground);
        bmp.Render(element);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        png.Save(file);
    }

    private static string? ArgAfter(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
