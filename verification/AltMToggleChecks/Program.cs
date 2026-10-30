using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Delta.Native;
internal static class Program {
 [StructLayout(LayoutKind.Sequential)]struct K{public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Explicit,Size=32)]struct U{[FieldOffset(0)]public K key;}
 [StructLayout(LayoutKind.Sequential)]struct I{public uint type;public U data;}
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint n,I[] data,int size);
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static object? Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Private)!.Invoke(o,args);
 static FieldInfo Field(string name)=>typeof(MainWindow).GetField(name,Private)!;
 static void Chord(){var keys=new[]{(0xA4,0),(0x4D,0),(0x4D,2),(0xA4,2)};var inputs=new I[4];for(int i=0;i<4;i++)inputs[i]=new I{type=1,data=new U{key=new K{vk=(ushort)keys[i].Item1,flags=(uint)keys[i].Item2}}};if(SendInput(4,inputs,Marshal.SizeOf<I>())!=4)throw new Exception("SendInput failed");}
 [STAThread]static int Main(){
  int result=1;var app=new App();app.InitializeComponent();
  var window=new MainWindow(false);
  var loaded=(RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler),window,typeof(MainWindow).GetMethod("WindowLoaded",Private)!);
  window.Loaded-=loaded; // Fixture avoids website initialization and account/network calls.
  window.Show();var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;Field("source").SetValue(window,source);Field("loaded").SetValue(window,true);
  source.AddHook((HwndSourceHook)Delegate.CreateDelegate(typeof(HwndSourceHook),window,typeof(MainWindow).GetMethod("WindowMessage",Private)!));
  Call(window,"RegisterToggleFallback");Call(window,"RegisterFocusedShortcuts",true);
  Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async()=>{
   try{
    int checks=0;
    void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    Check((bool)Field("toggleFallbackRegistered").GetValue(window)!,"fallback registers");
    Chord();await Task.Delay(250);Check(!window.IsVisible,"Alt+M hides through live hook");
    Chord();await Task.Delay(250);Check(window.IsVisible,"Alt+M shows through live hook");
    Field("capturing").SetValue(window,true);Chord();await Task.Delay(250);Check(!window.IsVisible,"capture/upload does not block hiding");
    Chord();await Task.Delay(250);Check(window.IsVisible,"capture/upload does not block showing");Field("capturing").SetValue(window,false);
    ((IDisposable)Field("shortcutHook").GetValue(window)!).Dispose(); // Deliberately leave field non-null, simulating a lost hook.
    Chord();await Task.Delay(250);Check(!window.IsVisible,"registered Alt+M fallback hides after hook loss");
    Chord();await Task.Delay(250);Check(window.IsVisible,"registered Alt+M fallback shows after hook loss");
    window.Close();Check(!(bool)Field("toggleFallbackRegistered").GetValue(window)!,"fallback unregisters on close");
    Console.WriteLine($"Passed {checks} live Alt+M checks: hide/show, capture/upload, fallback after hook loss, and cleanup.");result=0;
   }catch(Exception ex){Console.Error.WriteLine(ex);}
   finally{window.Close();Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);}
  }));
  Dispatcher.Run();return result;
 }
}
