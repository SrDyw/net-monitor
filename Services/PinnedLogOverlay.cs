using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using NetMonitor.ViewModels;

namespace NetMonitor.Services;

/// <summary>
/// Five separate one-row windows beside the watermark. Each shows one of the latest packet
/// lines from the pinned processes; new lines rise from the plate and push the older ones up.
/// </summary>
public sealed class PinnedLogOverlay : IDisposable
{
    private const int MaxRows = 10;
    private const double Gap = 2;
    private const double RowHeight = 24;

    private readonly ProcessesViewModel _vm;
    private readonly WatermarkWindow _anchor;
    private readonly PinnedLogRowWindow[] _rows = new PinnedLogRowWindow[MaxRows];
    private bool _visible;
    private int _visibleCount = 5;

    public PinnedLogOverlay(ProcessesViewModel vm, WatermarkWindow anchor)
    {
        _vm = vm;
        _anchor = anchor;
        for (int i = 0; i < MaxRows; i++) _rows[i] = new PinnedLogRowWindow();
        _vm.PinnedLog.CollectionChanged += OnLogChanged;
        _anchor.LocationChanged += OnAnchorChanged;
        _anchor.SizeChanged += OnAnchorChanged;
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        Update();
    }

    public void SetVisibleCount(int count)
    {
        _visibleCount = Math.Clamp(count, 0, MaxRows);
        Update();
    }

    public void SetOpacity(double opacity)
    {
        foreach (var row in _rows) row.Opacity = opacity;
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void OnAnchorChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        int count = _visible ? Math.Min(_visibleCount, _vm.PinnedLog.Count) : 0;
        if (count == 0 || !_anchor.IsVisible || _anchor.ActualWidth <= 0)
        {
            foreach (var row in _rows) row.Hide();
            return;
        }

        // Oldest first; the newest line sits closest to the plate.
        var lines = _vm.PinnedLog.Skip(_vm.PinnedLog.Count - count).ToList();
        var area = SystemParameters.WorkArea;
        double width = _anchor.ActualWidth;
        double stack = count * (RowHeight + Gap);
        bool above = _anchor.Top - stack - Gap >= area.Top;
        double left = Math.Clamp(_anchor.Left, area.Left, Math.Max(area.Left, area.Right - width));

        for (int i = 0; i < MaxRows; i++)
        {
            var row = _rows[i];
            if (i >= count)
            {
                row.Hide();
                continue;
            }
            row.DataContext = lines[i];
            row.Width = width;
            row.Left = left;
            row.Top = above
                ? _anchor.Top - (count - i) * (RowHeight + Gap)
                : _anchor.Top + _anchor.ActualHeight + Gap + (count - 1 - i) * (RowHeight + Gap);
            if (!row.IsVisible) row.Show();
        }
    }

    public void Dispose()
    {
        _vm.PinnedLog.CollectionChanged -= OnLogChanged;
        _anchor.LocationChanged -= OnAnchorChanged;
        _anchor.SizeChanged -= OnAnchorChanged;
        foreach (var row in _rows)
        {
            try { row.Close(); } catch (InvalidOperationException) { }
        }
    }
}
