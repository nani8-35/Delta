using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Delta.Native;

internal static partial class NativeWindowMethods
{
    [LibraryImport("user32.dll")]
    internal static partial uint GetClipboardSequenceNumber();

    internal static void PasteClipboard(IntPtr target)
    {
        if (!WindowsServices.IsWindow(target) || WindowsServices.GetForegroundWindow() != target)
            throw new InvalidOperationException("Typing paused: click the destination editor and press Start to resume.");
        var keys = new (ushort Key, uint Flags)[] { (0x11, 0), (0x56, 0), (0x56, 2), (0x11, 2) };
        var inputs = new KeyboardReplayInput[keys.Length];
        for (int i = 0; i < keys.Length; i++) inputs[i] = new KeyboardReplayInput {
            Type = 1, Data = new InputData { Keyboard = new KeyboardInput {
                Vk = keys[i].Key, Flags = keys[i].Flags, ExtraInfo = ShortcutReplayMarker
            } }
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyboardReplayInput>()) != inputs.Length) {
            int error = Marshal.GetLastPInvokeError();
            // A partially accepted batch must not leave Ctrl held down.
            SendInput(1, new[] { inputs[3] }, Marshal.SizeOf<KeyboardReplayInput>());
            throw new Win32Exception(error, "Windows blocked pasting into the editor.");
        }
    }
}
