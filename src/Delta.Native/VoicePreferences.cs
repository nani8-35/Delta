using System;
using System.IO;
using System.Text.Json;

namespace Delta.Native;
// Stores a file location and an output device ID, never the credential itself.
public sealed class VoicePreferences
{
    public string? KeyFile { get; set; }
    public string? OutputDeviceId { get; set; }
    public bool LiveDictation { get; set; } = true;
    public bool AutoSend { get; set; } = true;
    public static VoicePreferences Load(string directory)
    {
        string path = Path.Combine(directory, "voice-settings.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<VoicePreferences>(File.ReadAllText(path)) ?? new();
    }
    public string? ResolveKeyFile(string applicationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(KeyFile) && Path.IsPathFullyQualified(KeyFile) && File.Exists(KeyFile)) return KeyFile;
        string beside = Path.Combine(applicationDirectory, "API.txt");
        if (File.Exists(beside)) return beside;
        // Hidden Windows extensions commonly produce this exact filename.
        if (File.Exists(beside + ".txt")) return beside + ".txt";
        return null;
    }
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "voice-settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}

