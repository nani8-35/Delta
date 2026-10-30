using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;

internal static class Program {
 [STAThread] static void Main(string[] args) {
  if(args.Contains("--verify-payload")){using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("Delta.Payload.zip")!;using var z=new ZipArchive(s);if(!z.Entries.Any(e=>e.FullName.EndsWith("/Delta.exe"))||!z.Entries.Any(e=>e.FullName.EndsWith("/Assets/chat-bridge.js")))Environment.Exit(1);return;}
  var app=new Application();
  var panel=new StackPanel{Margin=new Thickness(24)};
  panel.Children.Add(new TextBlock{Text="Install Delta Browser 2.1.11",FontSize=22,Margin=new Thickness(0,0,0,16)});
  panel.Children.Add(new TextBlock{Text="Installs Delta for your Windows account and adds a desktop shortcut. Microsoft Edge WebView2 Runtime is required.",TextWrapping=TextWrapping.Wrap});
  var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,12)};panel.Children.Add(status);
  var button=new Button{Content="Install Delta",Height=36};panel.Children.Add(button);
  var window=new Window{Title="Delta 2.1.11 Setup",Width=460,Height=300,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Topmost=true,ShowActivated=true,Content=panel};
  string? installedRoot=null;
  bool installing=false;
  window.Closing+=(_,e)=>{if(installing)e.Cancel=true;};
  button.Click+=async(_,_)=>{
   if(installedRoot!=null){Process.Start(new ProcessStartInfo(Path.Combine(installedRoot,"Delta.exe")){UseShellExecute=true,WorkingDirectory=installedRoot});window.Close();return;}
   button.IsEnabled=false;installing=true;status.Text="Installing…";
   try{
    var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Delta Browser");
    await Task.Run(()=>Install(root));
    status.Text="Installed. Open Delta from the desktop shortcut.";button.Content="Open Delta";button.IsEnabled=true;
    installedRoot=root; installing=false;
    Process.Start(new ProcessStartInfo(Path.Combine(root,"Delta.exe")){UseShellExecute=true,WorkingDirectory=root});window.Close();
   }catch(Exception ex){installing=false;status.Text="Installation failed: "+ex.Message;button.IsEnabled=true;}
  };
  app.Run(window);
 }
 static void Install(string root){
  Directory.CreateDirectory(root);
  using var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("Delta.Payload.zip")!;
  using var zip=new ZipArchive(payload);
  foreach(var entry in zip.Entries){
   const string prefix="Delta-2.1.11-Windows-x64/";
   if(!entry.FullName.StartsWith(prefix,StringComparison.Ordinal))throw new IOException("Unexpected installer payload.");
   var relative=entry.FullName[prefix.Length..];if(relative.Length==0)continue;
   var target=Path.GetFullPath(Path.Combine(root,relative));
   if(!target.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid payload path.");
   if(entry.Name.Length==0){Directory.CreateDirectory(target);continue;}
   Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,true);
  }
  object? shell=null,link=null;
  try{
   shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
   dynamic s=shell!;link=s.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Delta Browser.lnk"));
   dynamic l=link!;l.TargetPath=Path.Combine(root,"Delta.exe");l.WorkingDirectory=root;l.IconLocation=Path.Combine(root,"Delta.exe");l.Save();
  }finally{if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
 }
}
