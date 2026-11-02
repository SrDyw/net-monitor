using System.Windows;
using System.Windows.Interop;
using NetMonitor.Interop;

namespace NetMonitor;

/// <summary>One click-through row of the pinned-process packet log beside the watermark.</summary>
public partial class PinnedLogRowWindow : Window
{
    public PinnedLogRowWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }
}
