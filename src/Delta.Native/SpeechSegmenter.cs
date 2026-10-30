using System;
using System.Collections.Generic;

namespace Delta.Native;
// Consumes 16 kHz, mono, signed PCM16. Silence is measured on the capture timeline,
// not on UI timers. Each frame is 20 ms; a 240 ms preroll preserves leading words.
public sealed class SpeechSegmenter
{
    private readonly List<byte[]> preroll = new();
    private readonly List<byte> speech = new();
    private int quietFrames, voicedFrames;
    public int PauseFrames { get; set; } = 50;
    public double MaximumSeconds { get; set; } = 12;
    public event Action<byte[]>? Segment;
    public event Action? SpeechStarted;
    public void Push(byte[] pcm)
    {
        if (pcm.Length != 640) throw new ArgumentException("Expected 20 ms of PCM16 at 16 kHz.");
        double energy = 0;
        for (int i = 0; i < pcm.Length; i += 2) { double s = (short)(pcm[i] | pcm[i + 1] << 8) / 32768d; energy += s * s; }
        bool voiced = Math.Sqrt(energy / 320) >= 0.0015;
        if (speech.Count == 0)
        {
            preroll.Add(pcm); if (preroll.Count > 12) preroll.RemoveAt(0);
            if (!voiced) return;
            SpeechStarted?.Invoke();
            foreach (var frame in preroll) speech.AddRange(frame);
            preroll.Clear(); voicedFrames = 1; quietFrames = 0;
        }
        else { speech.AddRange(pcm); if (voiced) { voicedFrames++; quietFrames = 0; } else quietFrames++; }
        if (quietFrames >= PauseFrames || speech.Count >= 16000 * 2 * Math.Clamp(MaximumSeconds, 1, 12)) Flush();
    }
    public void Flush()
    {
        if (speech.Count == 0) return;
        var bytes = speech.ToArray(); bool accepted = voicedFrames >= 5;
        speech.Clear(); quietFrames = voicedFrames = 0;
        if (accepted) Segment?.Invoke(bytes);
    }
    public void Reset() { speech.Clear(); preroll.Clear(); quietFrames = voicedFrames = 0; }
    public static byte[] Wave(byte[] pcm)
    {
        using var stream = new System.IO.MemoryStream(); using var writer = new System.IO.BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm); return stream.ToArray();
    }
}

