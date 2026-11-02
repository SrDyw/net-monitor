using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NetMonitor.ViewModels;

namespace NetMonitor.Controls;

public partial class ProcessesView : UserControl
{
    private ProcessesViewModel? _vm;

    public ProcessesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ProcessesViewModel);
    }

    private void Attach(ProcessesViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Log.CollectionChanged -= OnLogChanged;
            _vm.SelectionMoved -= OnSelectionMoved;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.Log.CollectionChanged += OnLogChanged;
            _vm.SelectionMoved += OnSelectionMoved;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    /// <summary>Keeps the newest line in view while auto-scroll is on, like a tail -f.</summary>
    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _vm is not { AutoScroll: true } || LogList.Items.Count == 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        });
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessesViewModel.Inspected)) FollowSelection();
    }

    private void OnSelectionMoved(object? sender, EventArgs e) => FollowSelection();

    /// <summary>Sorting reorders rows every couple of seconds; keep the inspected row on screen.</summary>
    private void FollowSelection()
    {
        if (_vm?.Inspected is not { } row) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_vm is null || !_vm.RowsView.Contains(row)) return;
            if (ProcessList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item &&
                FindScrollViewer(ProcessList) is { } scroll)
            {
                double top = item.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
                if (top >= 0 && top + item.ActualHeight <= scroll.ViewportHeight) return;
            }
            ProcessList.ScrollIntoView(row);
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroll) return scroll;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
