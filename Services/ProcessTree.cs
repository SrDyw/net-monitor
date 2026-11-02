using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NetMonitor.Services;

public sealed record ProcessInfo(int Pid, int ParentPid, string Name, string? Path);

/// <summary>
/// Snapshot of running processes (Toolhelp32: names and parents, no admin needed),
/// plus their executable icons, cached by path.
/// </summary>
public sealed class ProcessTree
{
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<int, ProcessInfo> Processes { get; private set; } = new Dictionary<int, ProcessInfo>();

    public void Refresh()
    {
        var map = new Dictionary<int, ProcessInfo>();
        IntPtr snap = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snap == InvalidHandle) return;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = Process32FirstW(snap, ref entry); ok; ok = Process32NextW(snap, ref entry))
            {
                int pid = (int)entry.ProcessId;
                string name = pid switch
                {
                    0 => "Inactivo",
                    4 => "Sistema",
                    _ => System.IO.Path.GetFileNameWithoutExtension(entry.ExeFile),
                };
                map[pid] = new ProcessInfo(pid, (int)entry.ParentProcessId, name, ImagePath(pid));
            }
        }
        finally { CloseHandle(snap); }
        Processes = map;
    }

    public string NameOf(int pid) =>
        Processes.TryGetValue(pid, out var p) ? p.Name : pid == 4 ? "Sistema" : $"pid {pid}";

    public ImageSource? IconFor(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                image.Freeze();
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or COMException) { }
        _icons[path] = image;
        return image;
    }

    private static string? ImagePath(int pid)
    {
        if (pid <= 4) return null;
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    private const uint SnapProcess = 0x00000002;
    private const uint QueryLimitedInformation = 0x1000;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
