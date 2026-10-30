using System;
using System.IO;
using System.Text.Json;

namespace Delta.Native;
// A capture keeps its original mode. Changing the switch can revoke an in-flight
// automatic Send, but cannot turn an upload-only capture into a later Send.
public sealed class ScreenshotSettings
{
    public readonly record struct CaptureMode(bool AutoSubmit, long Revision);
    public bool AutoSubmit { get; private set; } = true;
    private long revision;
    public CaptureMode Snapshot() => new(AutoSubmit, revision);
    public bool CanSubmit(CaptureMode mode) => mode.AutoSubmit && AutoSubmit && mode.Revision == revision;
    public void Set(bool enabled) { if (AutoSubmit != enabled) { AutoSubmit = enabled; revision++; } }
    public static ScreenshotSettings Load(string directory)
    {
        var settings = new ScreenshotSettings();
        string path = Path.Combine(directory, "preferences.json");
        if (!File.Exists(path)) return settings;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.TryGetProperty("screenshotAutoSubmit", out var enabled)) settings.Set(enabled.GetBoolean());
        return settings;
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "preferences.json"), pending = path + ".tmp";
        File.WriteAllText(pending, JsonSerializer.Serialize(new { screenshotAutoSubmit = AutoSubmit }));
        File.Move(pending, path, true);
    }
}
