using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Delta.Native;

// The application targets win-x64, where the pointer-sized entry points are exported.
internal static partial class NativeWindowMethods
{
    internal const int GwlExStyle = -20;
    internal const long WsExToolWindow = 0x00000080;
    internal const long WsExAppWindow = 0x00040000;
    internal const long WsExNoActivate = 0x08000000;
    internal const int WmMouseActivate = 0x0021;
    internal const int MaNoActivate = 3;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static void HideFromWindowSwitchers(IntPtr hwnd)
    {
        Marshal.SetLastPInvokeError(0);
        long styles = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        int error = Marshal.GetLastPInvokeError();
        if (styles == 0 && error != 0) throw new Win32Exception(error, "Could not read window styles.");
        long updated = (styles | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(updated));
        error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error, "Could not hide window from switchers.");
        // FRAMECHANGED | NOACTIVATE | NOMOVE | NOSIZE | NOZORDER.
        // Preserve activation and the existing topmost position.
        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x37))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not refresh window styles.");
    }
}
