using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Delta.Native;

internal sealed class TypingBuffer
{
    internal string Text { get; private set; } = "";
    internal int Position { get; private set; }
    internal bool Complete => Position >= Text.Length;
    private int generation;
    internal void Load(string text) { generation++; Text = text; Position = 0; }
    internal void Reset() => Load("");
    internal static int DelayMilliseconds(double speed) => (int)Math.Round(1000 / Math.Clamp(speed, 1, 60));

    internal async Task<bool> StepAsync(Func<string, Task> send, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Complete) return false;
        int version = generation;
        string next = Text[Position] == '\r' && Position + 1 < Text.Length && Text[Position + 1] == '\n'
            ? "\r\n" : StringInfo.GetNextTextElement(Text, Position);
        await send(next);
        if (version != generation) return false;
        Position += next.Length;
        token.ThrowIfCancellationRequested();
        return true;
    }

    internal async Task RunAsync(Func<string, Task> send, Func<double> speed, CancellationToken token)
    {
        int version = generation;
        while (!Complete && version == generation) {
            token.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            if (!await StepAsync(send, token)) return;
            int delay = Math.Max(0, DelayMilliseconds(speed()) - (int)clock.ElapsedMilliseconds);
            if (!Complete && delay > 0) await Task.Delay(delay, token);
        }
    }
}
