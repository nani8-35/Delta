using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace Delta.Native;

// Paste text units rather than Enter/Tab keystrokes: editors receive literal code
// whitespace, without triggering normal key-driven auto-indent/auto-close rules.
internal sealed class ClipboardCodeWriter
{
    private readonly IntPtr target;
    private readonly string original;
    private uint sequence;
    internal ClipboardCodeWriter(IntPtr target, string original)
    {
        this.target = target; this.original = original;
        sequence = NativeWindowMethods.GetClipboardSequenceNumber();
    }

    internal static async Task SetTextAsync(string text)
    {
        for (int attempt = 0; ; attempt++) {
            try { Clipboard.SetText(text); return; }
            catch (ExternalException) when (attempt < 5) { await Task.Delay(30); }
        }
    }

    internal async Task WriteAsync(string text)
    {
        if (WindowsServices.GetForegroundWindow() != target)
            throw new InvalidOperationException("Typing paused: the destination editor lost focus. Press Start to resume.");
        if (NativeWindowMethods.GetClipboardSequenceNumber() != sequence)
            throw new InvalidOperationException("Typing paused because the clipboard changed. Resume to continue the loaded code, or Reset to load the new copy.");
        await SetTextAsync(text);
        sequence = NativeWindowMethods.GetClipboardSequenceNumber();
        NativeWindowMethods.PasteClipboard(target);
        // Give the destination a message-pump turn before replacing clipboard data.
        // Once sent, finish this unit even when Pause is pressed to retain position.
        await Task.Delay(25);
    }

    internal async Task RestoreAsync()
    {
        // Never overwrite something the user copied while typing was running.
        if (NativeWindowMethods.GetClipboardSequenceNumber() == sequence)
            await SetTextAsync(original);
    }
}
