using System;
using System.Windows.Interop;
using NetMonitor.Interop;

namespace NetMonitor.Services;

/// <summary>Registers Ctrl+Alt+N system-wide, so the mode can change while the overlay ignores the mouse.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int Id = 0x4E4D; // "NM"
    private const uint VK_N = 0x4E;

    private readonly HwndSource _source;

    public bool IsRegistered { get; }

    public event EventHandler? Pressed;

    public HotkeyService(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(Hook);
        IsRegistered = Native.RegisterHotKey(hwnd, Id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, VK_N);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (IsRegistered) Native.UnregisterHotKey(_source.Handle, Id);
        _source.RemoveHook(Hook);
    }
}
