using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Delta.Native;

// A separate message pump keeps the hook responsive while the UI performs work.
internal sealed class LowLevelShortcutHook : IDisposable
{
    private readonly NativeWindowMethods.KeyboardHookCallback callback;
    private readonly Func<uint, uint, bool> dispatch;
    private readonly BufferedShortcutState keys = new();
    private readonly Thread thread;
    private Dispatcher? dispatcher;
    private IntPtr hook;
    private bool disposed;

    internal LowLevelShortcutHook(Func<uint, uint, bool> dispatch)
    {
        this.dispatch = dispatch;
        callback = HookCallback; // Root the delegate until after unhooking.
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        thread = new Thread(() => {
            try {
                dispatcher = Dispatcher.CurrentDispatcher;
                // GetAsyncKeyState seeds modifiers already held when the hook starts.
                // Subsequent transitions are tracked: swallowed modifier events do
                // not reliably update Windows' asynchronous keyboard state.
                foreach (uint key in new uint[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
                    if (Down((int)key)) keys.SeedForwardedModifier(key);
                hook = NativeWindowMethods.SetWindowsHookEx(NativeWindowMethods.WhKeyboardLl,
                    Marshal.GetFunctionPointerForDelegate(callback), NativeWindowMethods.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError());
            } catch (Exception ex) { failure = ex; }
            finally { ready.Set(); }
            if (failure != null) return;
            try { Dispatcher.Run(); }
            finally {
                try { Replay(keys.ReleaseForwarded()); }
                catch (Exception ex) { App.Log("Keyboard modifier cleanup: " + ex.Message); }
                if (hook != IntPtr.Zero && !NativeWindowMethods.UnhookWindowsHookEx(hook))
                    App.Log("Keyboard hook cleanup: " + Marshal.GetLastPInvokeError());
                hook = IntPtr.Zero;
                GC.KeepAlive(callback);
            }
        }) { IsBackground = true, Name = "Delta shortcut hook" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure != null) throw new InvalidOperationException("Could not install Delta keyboard hook.", failure);
    }

    private static bool Down(int key) => (NativeWindowMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    private static void Replay(IReadOnlyList<ShortcutKeyEvent> events)
    {
        if (events.Count == 0) return;
        var inputs = new NativeWindowMethods.KeyboardReplayInput[events.Count];
        for (int i = 0; i < events.Count; i++) {
            var input = events[i];
            inputs[i] = new NativeWindowMethods.KeyboardReplayInput {
                Type = 1,
                Data = new NativeWindowMethods.InputData {
                    Keyboard = new NativeWindowMethods.KeyboardInput {
                        Vk = (ushort)input.Key, Scan = (ushort)input.Scan,
                        Flags = (input.Extended ? 1u : 0) | (input.Up ? 2u : 0),
                        ExtraInfo = NativeWindowMethods.ShortcutReplayMarker
                    }
                }
            };
        }
        if (NativeWindowMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeWindowMethods.KeyboardReplayInput>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows blocked buffered shortcut input replay.");
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return NativeWindowMethods.CallNextHookEx(hook, code, wParam, lParam);
        try {
            var data = Marshal.PtrToStructure<NativeWindowMethods.KeyboardHookData>(lParam);
            // Preserve Delta's synthetic typing and other accessibility input.
            if (data.ExtraInfo == NativeWindowMethods.ShortcutReplayMarker)
                return NativeWindowMethods.CallNextHookEx(hook, code, wParam, lParam);
            int message = wParam.ToInt32();
            bool up = message == NativeWindowMethods.WmKeyUp || message == NativeWindowMethods.WmSysKeyUp;
            // VK_PACKET is synthetic Unicode typing, never a shortcut.
            if (data.VkCode == 0xE7) return NativeWindowMethods.CallNextHookEx(hook, code, wParam, lParam);
            if (up || message == NativeWindowMethods.WmKeyDown || message == NativeWindowMethods.WmSysKeyDown) {
                var input = new ShortcutKeyEvent(data.VkCode, data.ScanCode, (data.Flags & NativeWindowMethods.LlkhfExtended) != 0, up);
                if (keys.Handle(input, dispatch, Replay)) return (IntPtr)1;
            }
        } catch (Exception ex) {
            App.Log("Keyboard hook: " + ex.Message);
            return (IntPtr)1; // Do not leak a partially buffered chord on replay failure.
        }
        return NativeWindowMethods.CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        if (Thread.CurrentThread != thread) thread.Join();
    }
}
