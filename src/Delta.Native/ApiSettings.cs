using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Delta.Native;
public sealed record ApiSettings(string Key, string Model, Uri Endpoint)
{
    public static ApiSettings Parse(string text)
    {
        string key = Regex.Replace(text, "[\\u200B\\u200C\\u200D\\u200E\\u200F\\u2060\\uFEFF]", "").Trim(), model = "gemini-3.8-flash";
        string? endpoint = null;
        if (key.StartsWith('{'))
        {
            try {
                using var doc = JsonDocument.Parse(key);
                var root = doc.RootElement;
                if (!(root.TryGetProperty("key", out var k) || root.TryGetProperty("api_key", out k) || root.TryGetProperty("apiKey", out k)))
                    throw new InvalidDataException("The JSON file needs a key, api_key or apiKey field.");
                key = k.GetString() ?? "";
                if (root.TryGetProperty("model", out var m)) model = m.GetString() ?? model;
                if (root.TryGetProperty("endpoint", out var ep)) endpoint = ep.GetString();
            } catch (JsonException) { throw new InvalidDataException("The selected key file contains invalid JSON."); }
            catch (InvalidOperationException) { throw new InvalidDataException("Key, model and endpoint must be text values."); }
        }
        else {
            var assignment = Regex.Match(key, @"^(?:export\s+)?(?:(?:GEMINI|GOOGLE)[ _-]*API[ _-]*KEY|API[ _-]*KEY|KEY)\s*[:=]\s*(.+)$", RegexOptions.IgnoreCase);
            if (assignment.Success) key = assignment.Groups[1].Value.Trim();
        }
        key = key.Trim().Trim((char)0x201C, (char)0x201D, (char)0x2018, (char)0x2019);
        key = key.Trim().Trim('"', '\'').Trim();
        // Google auth keys can contain a period (AQ.); do not require the legacy AIza shape.
        if (!Regex.IsMatch(key, "^[A-Za-z0-9_.-]{10,1000}$"))
            throw new InvalidDataException("Key format not recognized. Save only the Google AI Studio key as plain text. Hidden characters and UTF-16 are supported; instructions or other text are not. The key is never displayed.");
        if (!Regex.IsMatch(model, "^[A-Za-z0-9._-]{1,80}$")) throw new InvalidDataException("Invalid transcription model name.");
        if (!Uri.TryCreate(endpoint ?? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "generativelanguage.googleapis.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Voice requires the official HTTPS Google Generative Language endpoint.");
        return new(key, model, uri);
    }
    public static ApiSettings Load(string directory)
    {
        string path = Path.Combine(directory, "API.txt");
        if (!File.Exists(path)) throw new FileNotFoundException(File.Exists(path + ".txt")
            ? "Found API.txt.txt. Rename it to API.txt or select it in Voice setup."
            : "Key file not found: " + path + ". Use Voice setup to select your file.");
        return LoadFile(path);
    }
    public static ApiSettings LoadFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Key file not found: " + path + ". Use Voice setup to select it.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 16000) throw new InvalidDataException("The key file must be smaller than 16 KB.");
        // Some editors save UTF-16 without a BOM. Detect interleaved NULs rather
        // than treating those files as UTF-8 and rejecting an otherwise valid key.
        var prefix = new byte[Math.Min(128, (int)file.Length)];
        int read = file.Read(prefix, 0, prefix.Length); file.Position = 0;
        int evenNull = 0, oddNull = 0;
        for (int i = 0; i < read; i++) if (prefix[i] == 0) { if ((i & 1) == 0) evenNull++; else oddNull++; }
        Encoding encoding = Encoding.UTF8;
        if (read >= 8 && oddNull >= read / 4 && evenNull == 0) encoding = Encoding.Unicode;
        else if (read >= 8 && evenNull >= read / 4 && oddNull == 0) encoding = Encoding.BigEndianUnicode;
        using var reader = new StreamReader(file, encoding, true);
        return Parse(reader.ReadToEnd());
    }
}



