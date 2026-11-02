using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NetMonitor.Interop;
using NetMonitor.Services;

namespace NetMonitor;

/// <summary>Modal editor for the Windows proxy and the user's HTTP proxy variables.</summary>
public partial class ProxyWindow : Window
{
    private readonly ObservableCollection<ProxyTemplate> _templates = new();
    private bool _loadingTemplates;

    public ProxyWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTitleBar();
        App.Theme.Changed += OnThemeChanged;
        Closed += (_, _) => App.Theme.Changed -= OnThemeChanged;
        TemplateCombo.ItemsSource = _templates;
        try { Load(); }
        catch (Exception ex)
        {
            Log.Error("proxy load", ex);
            ShowStatus("No se pudo leer la configuración actual.", error: true);
        }
        LoadTemplates();
    }

    private void Load()
    {
        ApplyState(ProxyService.Load());
    }

    private void ApplyState(ProxyState state)
    {
        SystemProxySwitch.IsChecked = state.Enabled;
        ServerBox.Text = state.Server;
        BypassBox.Text = state.Bypass;
        PacBox.Text = state.PacUrl;
        AutoDetectSwitch.IsChecked = state.AutoDetect;
        HttpProxyBox.Text = state.HttpProxy;
        HttpsProxyBox.Text = state.HttpsProxy;
    }

    private ProxyState CurrentState() => new(
        SystemProxySwitch.IsChecked == true,
        ServerBox.Text.Trim(),
        BypassBox.Text.Trim(),
        PacBox.Text.Trim(),
        AutoDetectSwitch.IsChecked == true,
        HttpProxyBox.Text.Trim(),
        HttpsProxyBox.Text.Trim());

    private void LoadTemplates()
    {
        _loadingTemplates = true;
        _templates.Clear();
        foreach (var template in ProxyTemplates.Load()) _templates.Add(template);
        TemplateCombo.SelectedIndex = -1;
        _loadingTemplates = false;
    }

    private void OnTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTemplates || TemplateCombo.SelectedItem is not ProxyTemplate template) return;
        ApplyState(template.ToState());
    }

    private void OnSaveTemplateClick(object sender, RoutedEventArgs e)
    {
        string initial = (TemplateCombo.SelectedItem as ProxyTemplate)?.Name ?? "";
        var dialog = new ProxyTemplateNameWindow(initial) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var state = CurrentState();
        var template = new ProxyTemplate(dialog.TemplateName, state.Enabled, state.Server, state.Bypass,
            state.PacUrl, state.AutoDetect, state.HttpProxy, state.HttpsProxy);

        _loadingTemplates = true;
        int index = _templates.ToList().FindIndex(t =>
            string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _templates[index] = template;
        else _templates.Add(template);
        _loadingTemplates = false;

        ProxyTemplates.Save(_templates);
        TemplateCombo.SelectedItem = _templates.First(t =>
            string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));
        ShowStatus($"Plantilla «{template.Name}» guardada.", error: false);
    }

    private void OnDeleteTemplateClick(object sender, RoutedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is not ProxyTemplate template) return;
        if (MessageBox.Show(this, $"¿Eliminar la plantilla «{template.Name}»?", "Plantillas de proxy",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _loadingTemplates = true;
        _templates.Remove(template);
        _loadingTemplates = false;
        ProxyTemplates.Save(_templates);
        TemplateCombo.SelectedIndex = -1;
        ShowStatus($"Plantilla «{template.Name}» eliminada.", error: false);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string? error;
        try { error = ProxyService.Save(CurrentState()); }
        catch (Exception ex)
        {
            Log.Error("proxy save", ex);
            error = $"No se pudo guardar: {ex.Message}";
        }
        if (error is not null)
        {
            ShowStatus(error, error: true);
            return;
        }
        DialogResult = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowStatus(string text, bool error)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "AlertBrush" : "OkBrush");
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTitleBar();

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.StyleTitleBar(hwnd, App.Theme.IsDark,
            App.Theme.IsDark ? 0x08192Au : 0xE8F0F5u,
            App.Theme.IsDark ? 0xE6F0F6u : 0x0B1F33u);
    }
}
