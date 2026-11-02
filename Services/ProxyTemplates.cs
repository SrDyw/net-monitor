using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NetMonitor.Services;

/// <summary>A named proxy configuration the owner can load from the proxy editor.</summary>
public sealed record ProxyTemplate(
    string Name,
    bool Enabled,
    string Server,
    string Bypass,
    string PacUrl,
    bool AutoDetect,
    string HttpProxy,
    string HttpsProxy)
{
    public ProxyState ToState() => new(Enabled, Server, Bypass, PacUrl, AutoDetect, HttpProxy, HttpsProxy);
}

/// <summary>Saved proxy presets, in <c>%AppData%\NetMonitor\proxy-templates.json</c>.</summary>
public static class ProxyTemplates
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMonitor", "proxy-templates.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static List<ProxyTemplate> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<ProxyTemplate>>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public static void Save(IEnumerable<ProxyTemplate> templates)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(templates, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
