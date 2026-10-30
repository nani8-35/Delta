using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace Delta.Native;
internal static class App { internal static void Log(string message)=>throw new Exception(message); }
internal static class Program {
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateWindowExW(uint exStyle,string windowClass,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr data);
 [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
 private static int checks;
 private static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 private static void CheckStyles(IntPtr hwnd){
  Check(hwnd!=IntPtr.Zero,"window handle created");
  long style=NativeWindowMethods.GetWindowLongPtr(hwnd,NativeWindowMethods.GwlExStyle).ToInt64();
  Check((style&NativeWindowMethods.WsExToolWindow)!=0,"TOOLWINDOW set before show");
  Check((style&NativeWindowMethods.WsExAppWindow)==0,"APPWINDOW removed before show");
  Check((style&NativeWindowMethods.WsExNoActivate)==0,"dialog activation retained");
 }
 [STAThread] static void Main(){
  Check(Marshal.SizeOf<NativeWindowMethods.KeyboardReplayInput>()==40,"Win64 INPUT size");
  Check(Marshal.SizeOf<NativeWindowMethods.CompletedWindowMessage>()==40,"Win64 CWPRETSTRUCT size");
  using(var policy=new ApplicationWindowPolicy()){
   var window=new Window{ShowInTaskbar=false,Width=200,Height=100}; window.SourceInitialized+=(_,_)=>NativeWindowMethods.ApplyToolWindowStyle(new WindowInteropHelper(window).Handle);
   try{var hwnd=new WindowInteropHelper(window).EnsureHandle();CheckStyles(hwnd);Check(!window.ShowInTaskbar,"WPF taskbar property");}finally{window.Close();}
   foreach(string name in new[]{"STATIC","#32770"}){
    var hwnd=CreateWindowExW(0x40000,name,"Delta policy fixture",0x00CF0000,0,0,100,100,IntPtr.Zero,IntPtr.Zero,NativeWindowMethods.GetModuleHandle(null),IntPtr.Zero);
    try{CheckStyles(hwnd); NativeWindowMethods.SetWindowLongPtr(hwnd,NativeWindowMethods.GwlExStyle,new IntPtr(0x40000)); CheckStyles(hwnd);}finally{if(hwnd!=IntPtr.Zero)DestroyWindow(hwnd);}
   }
  }
  // A window created after disposal must not be affected by our thread hook.
  var plain=CreateWindowExW(0x40000,"STATIC","Delta unhook fixture",0,0,0,100,100,IntPtr.Zero,IntPtr.Zero,NativeWindowMethods.GetModuleHandle(null),IntPtr.Zero);
  try{Check((NativeWindowMethods.GetWindowLongPtr(plain,NativeWindowMethods.GwlExStyle).ToInt64()&0x40000)!=0,"window hook removed");}finally{if(plain!=IntPtr.Zero)DestroyWindow(plain);}
  Console.WriteLine($"Passed {checks} native window checks: WPF, Win32, dialog-class creation, activation retention, structure layout, and unhooking. No windows were shown.");
 }
}
