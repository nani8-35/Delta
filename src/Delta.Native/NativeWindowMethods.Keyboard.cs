using System;
using System.Runtime.InteropServices;

namespace Delta.Native;

internal static partial class NativeWindowMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x100, WmKeyUp = 0x101;
    internal const int WmSysKeyDown = 0x104, WmSysKeyUp = 0x105;
    internal const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    internal const int VkLWin = 0x5B, VkRWin = 0x5C;
    internal const uint LlkhfInjected = 0x10;
    internal const uint LlkhfExtended = 0x01;
    internal static readonly UIntPtr ShortcutReplayMarker = new(0x444C5441u);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort Vk, Scan;
        internal uint Flags, Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int X, Y;
        internal uint Data, Flags, Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputData
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardReplayInput
    {
        internal uint Type;
        internal InputData Data;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint count, [In] KeyboardReplayInput[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        internal uint VkCode, ScanCode, Flags, Time;
        internal UIntPtr ExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static partial IntPtr SetWindowsHookEx(int hookId, IntPtr callback, IntPtr module, uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int key);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr GetModuleHandle(string? moduleName);
}
