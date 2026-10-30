using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Delta.Native;

internal static partial class NativeWindowMethods
{
    internal const int WhCallWndProcRet = 12;
    internal const int WmNcCreate = 0x81, WmCreate = 0x01, WmInitDialog = 0x110, WmStyleChanged = 0x7D;
    internal const int WsChild = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CompletedWindowMessage
    {
        internal IntPtr Result, LParam, WParam;
        internal uint Message;
        internal IntPtr Hwnd;
    }

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowHandle(IntPtr hwnd);

    // Subsidiary windows retain normal activation so login and dialog input works.
    internal static void ApplyToolWindowStyle(IntPtr hwnd, bool refresh = true)
    {
        Marshal.SetLastPInvokeError(0);
        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        int error = Marshal.GetLastPInvokeError();
        if (style == 0 && error != 0) throw new Win32Exception(error);
        long updated = (style | WsExToolWindow) & ~WsExAppWindow;
        if (style == updated) return;
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(updated));
        error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error);
        if (refresh && !SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x37))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
