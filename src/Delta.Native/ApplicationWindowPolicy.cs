using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Delta.Native;

// Install on the WPF UI thread before any app window opens. Native message boxes
// and common dialogs are covered as well as WPF windows, without changing UI.
internal sealed class ApplicationWindowPolicy : IDisposable
{
    private readonly NativeWindowMethods.KeyboardHookCallback callback;
    private IntPtr hook;

    internal ApplicationWindowPolicy()
    {
        callback = WindowHook;
        hook = NativeWindowMethods.SetWindowsHookEx(NativeWindowMethods.WhCallWndProcRet,
            Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero, NativeWindowMethods.GetCurrentThreadId());
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not install Delta window policy.");
    }

    private IntPtr WindowHook(int code, IntPtr wParam, IntPtr data)
    {
        try {
            if (code >= 0 && data != IntPtr.Zero) {
                var message = Marshal.PtrToStructure<NativeWindowMethods.CompletedWindowMessage>(data);
                // Observe after creation/init and style changes. GetWindowLongPtr
                // is reliable here, and WPF cannot undo the policy afterward.
                if ((message.Message == NativeWindowMethods.WmNcCreate
                    || message.Message == NativeWindowMethods.WmCreate
                    || message.Message == NativeWindowMethods.WmInitDialog
                    || message.Message == NativeWindowMethods.WmStyleChanged)
                    && NativeWindowMethods.IsWindowHandle(message.Hwnd)
                    && (NativeWindowMethods.GetWindowLongPtr(message.Hwnd, -16).ToInt64() & NativeWindowMethods.WsChild) == 0) {
                    NativeWindowMethods.ApplyToolWindowStyle(message.Hwnd, refresh: false);
                }
            }
        } catch (Exception ex) { App.Log("Window switcher policy: " + ex.Message); }
        return NativeWindowMethods.CallNextHookEx(hook, code, wParam, data);
    }

    public void Dispose()
    {
        if (hook == IntPtr.Zero) return;
        if (!NativeWindowMethods.UnhookWindowsHookEx(hook))
            App.Log("Window policy cleanup: " + Marshal.GetLastPInvokeError());
        hook = IntPtr.Zero;
        GC.KeepAlive(callback);
    }
}
