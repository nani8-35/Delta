using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
namespace Delta.Native;
internal static class WindowsServices {
 [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
 [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd,IntPtr unused);
 [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint a,uint b,bool attach);
 internal static void FocusFixture(IntPtr hwnd){var current=GetCurrentThreadId();var other=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero);bool attach=current!=other&&AttachThreadInput(current,other,true);try{SetForegroundWindow(hwnd);}finally{if(attach)AttachThreadInput(current,other,false);}}
}
internal static class Program {
 [STAThread] static int Main(){
  int exit=1,checks=0;
  void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
  var app=new Application();var editor=new TextBox{AcceptsReturn=true,AcceptsTab=true};
  var window=new Window{Title="Delta synthetic typing test",Width=500,Height=220,Content=editor,Topmost=true};
  IDataObject? saved=null;uint last=0;
  window.Loaded+=async(_,_)=>{
   try{
    var buffer=new TypingBuffer();buffer.Load("abc");var sent=new List<string>();var cancel=new CancellationTokenSource();
    try{await buffer.RunAsync(s=>{sent.Add(s);cancel.Cancel();return Task.CompletedTask;},()=>60,cancel.Token);}catch(OperationCanceledException){}
    Check(string.Join("",sent)=="a"&&buffer.Position==1,"pause keeps exact position");
    await buffer.RunAsync(s=>{sent.Add(s);return Task.CompletedTask;},()=>60,CancellationToken.None);
    Check(string.Join("",sent)=="abc"&&buffer.Complete,"resume has no missing or duplicate characters");
    buffer.Reset();Check(buffer.Text==""&&buffer.Position==0,"reset clears buffer");
    buffer.Load("a\r\n\t  Ω😀e\u0301");sent.Clear();await buffer.RunAsync(s=>{sent.Add(s);return Task.CompletedTask;},()=>60,CancellationToken.None);
    Check(string.Join("",sent)=="a\r\n\t  Ω😀e\u0301","whitespace and Unicode preserved");
    Check(sent.Contains("\r\n")&&sent.Contains("😀")&&sent.Contains("e\u0301"),"CRLF and graphemes remain intact");
    buffer.Load("old");await buffer.RunAsync(s=>{buffer.Reset();return Task.CompletedTask;},()=>60,CancellationToken.None);
    Check(buffer.Position==0&&buffer.Text=="","reset during send cannot advance new buffer");
    Check(TypingBuffer.DelayMilliseconds(6)==167&&TypingBuffer.DelayMilliseconds(60)==17,"10 percent default and full-speed timing");
    buffer.Load("ab");int reads=0;await buffer.RunAsync(_=>Task.CompletedTask,()=>{reads++;return 60;},CancellationToken.None);Check(reads==2,"speed read live each unit");
    var manual=new TypingBuffer();manual.Load("A \r\n\t😀B");var units=new List<string>();
    for(int i=0;i<6;i++){Check(await manual.StepAsync(s=>{units.Add(s);return Task.CompletedTask;},CancellationToken.None),"manual step accepted");Check(units.Count==i+1,"exactly one unit per press");}
    Check(string.Join("",units)=="A \r\n\t😀B","manual stepping preserves text and whitespace");
    Check(!await manual.StepAsync(s=>{units.Add(s);return Task.CompletedTask;},CancellationToken.None)&&units.Count==6,"completed manual buffer does not repeat");
    manual.Load("abc");units.Clear();await manual.StepAsync(s=>{units.Add(s);return Task.CompletedTask;},CancellationToken.None);
    await manual.RunAsync(s=>{units.Add(s);return Task.CompletedTask;},()=>60,CancellationToken.None);
    Check(string.Join("",units)=="abc","manual step and Start share cursor");
    manual.Load("x");try{await manual.StepAsync(_=>throw new InvalidOperationException("fixture"),CancellationToken.None);}catch(InvalidOperationException){}
    Check(manual.Position==0,"failed manual send does not consume a character");
    bool resetResult=await manual.StepAsync(_=>{manual.Reset();return Task.CompletedTask;},CancellationToken.None);
    Check(!resetResult&&manual.Position==0&&manual.Text=="","reset during manual send clears cursor");
    var shortcutKeys=new ShortcutKeyState();int dispatches=0;
    bool DispatchStep(uint key,uint mods){if(ShortcutCatalog.ActionFor(key,mods)!="typing-step")return false;dispatches++;return true;}
    Check(shortcutKeys.KeyDown(0xBC,1,DispatchStep),"Alt comma recognizes step action");
    Check(shortcutKeys.KeyDown(0xBC,1,DispatchStep)&&dispatches==1,"held comma does not dispatch a second step");
    shortcutKeys.KeyUp(0xBC);shortcutKeys.KeyDown(0xBC,1,DispatchStep);
    Check(dispatches==2,"release and press comma while holding Alt dispatches next step");
    Check(ShortcutCatalog.ActionFor(0xBC,2)=="typing-insert","Ctrl comma retains full paste");
    window.Activate();editor.Focus();Keyboard.Focus(editor);var hwnd=new WindowInteropHelper(window).Handle;WindowsServices.FocusFixture(hwnd);await Task.Delay(100);
    if(WindowsServices.GetForegroundWindow()!=hwnd){last=NativeWindowMethods.GetClipboardSequenceNumber();Console.WriteLine($"Passed {checks} typing-session checks. Live clipboard/editor checks skipped: Windows denied foreground focus in this session.");exit=0;return;}
    Check(WindowsServices.GetForegroundWindow()==hwnd,"fixture editor focused");
    string code="def palindrome(s):\r\n    if s == s[::-1]:\r\n\treturn True\r\n    return False # Ω😀";
    await ClipboardCodeWriter.SetTextAsync(code);var writer=new ClipboardCodeWriter(hwnd,code);
    buffer.Load(code);await buffer.RunAsync(writer.WriteAsync,()=>60,CancellationToken.None);await writer.RestoreAsync();await Task.Delay(100);
    Check(editor.Text.Replace("\r\n","\n")==code.Replace("\r\n","\n"),"live character entry retains exact indentation, tabs, Unicode and line breaks");
    Check(Clipboard.GetText()==code,"copied code restored after typing");
    editor.Clear();writer=new ClipboardCodeWriter(hwnd,code);await writer.WriteAsync(code);await writer.RestoreAsync();await Task.Delay(100);
    Check(editor.Text.Replace("\r\n","\n")==code.Replace("\r\n","\n"),"Insert pastes whole code at once");
    writer=new ClipboardCodeWriter(hwnd,code);await ClipboardCodeWriter.SetTextAsync("new user copy");bool stopped=false;
    try{await writer.WriteAsync("x");}catch(InvalidOperationException){stopped=true;}await writer.RestoreAsync();
    Check(stopped&&Clipboard.GetText()=="new user copy","clipboard change stops typing and preserves user's new copy");
    var other=new Window{Title="Delta focus-loss fixture",Width=100,Height=80,Topmost=true};other.Show();other.Activate();WindowsServices.FocusFixture(new WindowInteropHelper(other).Handle);await Task.Delay(100);
    writer=new ClipboardCodeWriter(hwnd,"new user copy");stopped=false;try{await writer.WriteAsync("x");}catch(InvalidOperationException){stopped=true;}other.Close();
    Check(stopped,"focus loss stops input");
    last=NativeWindowMethods.GetClipboardSequenceNumber();Console.WriteLine($"Passed {checks} typing checks including live editor paste, indentation, pause/resume, reset, clipboard ownership, focus and speed.");exit=0;
   }catch(Exception ex){Console.Error.WriteLine(ex);last=NativeWindowMethods.GetClipboardSequenceNumber();}
   finally{if(saved!=null&&NativeWindowMethods.GetClipboardSequenceNumber()==last)Clipboard.SetDataObject(saved,true);window.Close();}
  };
  app.Run(window);return exit;
 }
}
