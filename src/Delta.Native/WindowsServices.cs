using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Delta.Native;
internal static class WindowsServices
{
    internal const long OverlayStyleMask = 0x20L | 0x08000000L; // TRANSPARENT + NOACTIVATE
    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => NativeWindowMethods.GetWindowLongPtr(hwnd, index);
    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => NativeWindowMethods.SetWindowLongPtr(hwnd, index, value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    internal static long OverlayStyleBits(IntPtr hwnd) => GetWindowLongPtr(hwnd, -20).ToInt64() & OverlayStyleMask;
    internal static void SetOverlayStyleBits(IntPtr hwnd, long bits)
    {
        long current = GetWindowLongPtr(hwnd, -20).ToInt64();
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = SetWindowLongPtr(hwnd, -20, new IntPtr((current & ~OverlayStyleMask) | (bits & OverlayStyleMask)));
        if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not change overlay mode.");
        // FRAMECHANGED | NOACTIVATE | NOMOVE | NOSIZE | NOZORDER
        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x37))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not refresh overlay mode.");
    }
    internal static void ShowWithoutActivation(IntPtr hwnd)
    {
        if (!SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x53)) // SHOWWINDOW | NOACTIVATE | NOMOVE | NOSIZE
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not show overlay.");
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    private delegate void ForegroundCallback(IntPtr hook,uint evt,IntPtr window,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,ForegroundCallback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    internal sealed class ForegroundObserver : IDisposable {
        private readonly ForegroundCallback callback;
        private readonly IntPtr hook;
        internal ForegroundObserver(Action<IntPtr> changed) {
            void Observe(IntPtr window) {if(window==IntPtr.Zero)return;GetWindowThreadProcessId(window,out uint process);if(process!=Environment.ProcessId)changed(window);}
            callback=(_,_,window,_,_,_,_)=>Observe(window);
            hook=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,2);
            Observe(GetForegroundWindow());
        }
        public void Dispose(){if(hook!=IntPtr.Zero)UnhookWinEvent(hook);GC.KeepAlive(callback);}
    }
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    private static bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity) => NativeWindowMethods.SetWindowDisplayAffinity(hwnd, affinity);
    private static bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity) => NativeWindowMethods.GetWindowDisplayAffinity(hwnd, out affinity);
    internal const uint ExcludeFromCapture = 0x11;
    internal static uint CaptureAffinity(Window window) {
        if (!GetWindowDisplayAffinity(new WindowInteropHelper(window).Handle,out uint affinity))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not read capture protection.");
        return affinity;
    }
    internal static void SetCaptureAffinity(Window window,uint affinity) {
        if (!SetWindowDisplayAffinity(new WindowInteropHelper(window).Handle,affinity))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows could not enable capture protection.");
    }
    internal static void ProtectWindow(Window window, Action<bool,string>? status = null) {
        window.ShowInTaskbar = false;
        void Apply() {
            try { NativeWindowMethods.ApplyToolWindowStyle(new WindowInteropHelper(window).Handle); }
            catch (Exception ex) { App.Log("Window switcher styles: " + ex.Message); }
            try {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041))
                    throw new NotSupportedException("Capture exclusion requires Windows 10 version 2004 or newer.");
                SetCaptureAffinity(window,ExcludeFromCapture);
                if(CaptureAffinity(window)!=ExcludeFromCapture) throw new InvalidOperationException("Windows did not confirm capture protection.");
                status?.Invoke(true,"Excluded from supported Windows screenshots, sharing and recordings.");
            } catch(Exception ex) { App.Log("Capture protection: "+ex.Message); status?.Invoke(false,ex.Message); }
        }
        window.SourceInitialized += (_,_)=>Apply();
        if(new WindowInteropHelper(window).Handle!=IntPtr.Zero) Apply();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    // Used only by the opt-in local UI smoke fixture; never invoked in normal use.
    internal static async Task ClickFixtureAt(int x, int y)
    {
        GetCursorPos(out var original);
        try {
            if (!SetCursorPos(x,y)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var inputs = new[] { new INPUT { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = 2 } } }, new INPUT { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = 4 } } } };
            if (SendInput(2, inputs, Marshal.SizeOf<INPUT>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error());
            await Task.Delay(300);
        } finally { SetCursorPos(original.X,original.Y); }
    }
    internal static void TypeCharacter(IntPtr target, char value)
    {
        if (!IsWindow(target) || GetForegroundWindow() != target) throw new InvalidOperationException("Typing paused because the external editor lost focus.");
        var inputs = new[] { new INPUT { Type = 1, Data = new InputUnion { Keyboard = new KEYBDINPUT { Scan = value, Flags = 4 } } }, new INPUT { Type = 1, Data = new InputUnion { Keyboard = new KEYBDINPUT { Scan = value, Flags = 6 } } } };
        if (SendInput(2, inputs, Marshal.SizeOf<INPUT>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked typing into the selected editor.");
    }
    private static Bitmap CaptureBitmap(Window window)
    {
        uint previous=CaptureAffinity(window);
        SetCaptureAffinity(window,ExcludeFromCapture);
        try {
            DwmFlush();
            var bounds=System.Windows.Forms.SystemInformation.VirtualScreen;
            var bitmap=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
            try {
                using var graphics=Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.X,bounds.Y,0,0,bounds.Size,CopyPixelOperation.SourceCopy);
                return bitmap;
            } catch {bitmap.Dispose();throw;}
        } finally {if(previous!=ExcludeFromCapture)SetCaptureAffinity(window,previous);}
    }
    private static byte[] EncodeCapture(Bitmap bitmap) {
        using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();
    }
    internal static byte[] Capture(Window window) {
        using var bitmap=CaptureBitmap(window);return EncodeCapture(bitmap);
    }
    internal static async Task<byte[]> CaptureAsync(Window window) {
        // Snapshot pixels and apply affinity on the owning UI thread; only the
        // expensive PNG encoder moves to a worker, with bitmap lifetime retained.
        using var bitmap=CaptureBitmap(window);
        return await Task.Run(()=>EncodeCapture(bitmap));
    }
    internal static async Task<string> Recognize(byte[] png)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("Install a Windows OCR language in Settings → Time & language, then retry.");
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(png); await writer.StoreAsync(); }
        stream.Seek(0); var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension) throw new InvalidOperationException("Select a smaller OCR area.");
        return (await engine.RecognizeAsync(bitmap)).Text;
    }
}


