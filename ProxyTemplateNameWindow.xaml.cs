using System;
using System.Windows;
using System.Windows.Interop;
using NetMonitor.Interop;

namespace NetMonitor;

/// <summary>Small modal that asks for the name of a proxy preset.</summary>
public partial class ProxyTemplateNameWindow : Window
{
    public ProxyTemplateNameWindow(string initialName = "")
    {
        InitializeComponent();
        NameBox.Text = initialName;
        SourceInitialized += (_, _) => ApplyTitleBar();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string TemplateName => NameBox.Text.Trim();

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (TemplateName.Length == 0)
        {
            ErrorText.Text = "Escribe un nombre para la plantilla.";
            ErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.StyleTitleBar(hwnd, App.Theme.IsDark,
            App.Theme.IsDark ? 0x08192Au : 0xE8F0F5u,
            App.Theme.IsDark ? 0xE6F0F6u : 0x0B1F33u);
    }
}
