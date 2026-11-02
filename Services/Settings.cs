using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetMonitor.Services;

public enum Corner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum StartView
{
    Window,
    Tray,
    Watermark,
}

public sealed class Settings
{
    public const string DefaultPingHost = "1.1.1.1";

    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public StartView StartView { get; set; } = StartView.Window;
    public int RangeMinutes { get; set; } = 5;
    public bool ShowNetworkDetails { get; set; } = true;

    public Corner WatermarkCorner { get; set; } = Corner.BottomRight;
    public double WatermarkOffsetX { get; set; }
    public double WatermarkOffsetY { get; set; }
    public double WatermarkOpacity { get; set; } = 1.0;
    public int WatermarkLogLines { get; set; } = 5;
    public List<string> PinnedProcesses { get; set; } = new();
    public bool WatermarkShowDown { get; set; } = true;
    public bool WatermarkShowUp { get; set; } = true;
    public bool WatermarkShowLatency { get; set; } = true;
    public bool WatermarkShowChart { get; set; } = true;
    public bool WatermarkShowNetwork { get; set; } = true;

    public string PingHost { get; set; } = DefaultPingHost;
    public int HighLatencyMs { get; set; } = 100;
    public bool RatesInBytes { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMonitor", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
