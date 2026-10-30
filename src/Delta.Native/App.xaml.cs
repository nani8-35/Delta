using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Delta.Native;
public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private ApplicationWindowPolicy? windowPolicy;
    public static string DataFolder { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Delta Native");
    public static void Log(string text)
    {
        try { Directory.CreateDirectory(DataFolder); File.AppendAllText(Path.Combine(DataFolder, "startup.log"), $"{DateTimeOffset.Now:O} {text}\n"); } catch { }
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        windowPolicy = new ApplicationWindowPolicy();
        int dataIndex = Array.IndexOf(e.Args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length && Path.IsPathFullyQualified(e.Args[dataIndex + 1])) DataFolder = e.Args[dataIndex + 1];
        bool testing = Array.Exists(e.Args, arg => arg is "--login-smoke" or "--self-test" or "--smoke-test" or "--audio-smoke" or "--ocr-smoke" or "--key-file-check" or "--live-network-check");
#if !DELTA_DIAGNOSTICS
        // Production builds never open an unlocked fixture through command-line flags.
        if(testing) { Shutdown(2); return; }
#endif
        instance = new Mutex(true, testing ? "Local\\Delta.Native.Test." + Environment.ProcessId : "Local\\Delta.Native.Windows", out bool first);
        if (!first) { MessageBox.Show("Delta is already running. Press Alt+M or click its tray icon.", "Delta"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { Log(args.Exception.GetType().Name + ": " + args.Exception.Message); MessageBox.Show(args.Exception.Message, "Delta error"); args.Handled = true; };
        Log("Starting Delta Native " + typeof(App).Assembly.GetName().Version?.ToString(3));
        int liveCheck = Array.IndexOf(e.Args,"--live-network-check");
        if (liveCheck>=0) {
            if (liveCheck+2>=e.Args.Length) { Shutdown(1); return; }
            _ = LiveNetwork(e.Args[liveCheck+1],e.Args[liveCheck+2]); return;
        }
        int keyCheck = Array.IndexOf(e.Args, "--key-file-check");
        if (keyCheck >= 0) {
            if (keyCheck + 1 >= e.Args.Length) { Shutdown(1); return; }
            Shutdown(SelfTest.CheckKeyFile(e.Args[keyCheck+1], DataFolder)); return;
        }
        if (Array.IndexOf(e.Args, "--self-test") >= 0) { int code = SelfTest.Run(DataFolder); Shutdown(code); return; }
        if (Array.IndexOf(e.Args, "--audio-smoke") >= 0) { _ = AudioSmoke(); return; }
        if (Array.IndexOf(e.Args, "--ocr-smoke") >= 0) { _ = OcrSmoke(); return; }
        if(Array.IndexOf(e.Args,"--login-smoke")>=0) { _=LoginSmoke(); return; }
        if(!testing) { ShutdownMode=ShutdownMode.OnExplicitShutdown; _=RunAccountWithDiagnostics(); return; }
        var window = new MainWindow(Array.IndexOf(e.Args, "--smoke-test") >= 0); MainWindow = window; window.Show();
        if (Array.IndexOf(e.Args, "--smoke-test") >= 0) _ = Smoke(window);
    }
    private async System.Threading.Tasks.Task LoginSmoke() {
        using var account=new AccountClient();
        var login=new AccountLoginWindow(account);MainWindow=login;login.Show();
        await System.Threading.Tasks.Task.Delay(500);
        login.UpdateLayout();Directory.CreateDirectory(DataFolder);
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)login.ActualWidth,(int)login.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(login);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using(var stream=File.Create(Path.Combine(DataFolder,"login-preview.png")))encoder.Save(stream);
        bool browserOpened=false;foreach(Window window in Windows) if(window is Delta.Native.MainWindow)browserOpened=true;
#if DELTA_DIAGNOSTICS
        bool passwordTogglePassed=login.VerifyPasswordToggle();
#else
        bool passwordTogglePassed=false;
#endif
        File.WriteAllText(Path.Combine(DataFolder,"login-smoke.json"),System.Text.Json.JsonSerializer.Serialize(new {loginVisible=login.IsVisible,browserOpened,startupOpacity=login.Opacity,passwordTogglePassed,captureProtected=WindowsServices.CaptureAffinity(login)==WindowsServices.ExcludeFromCapture}));
        login.Close();Shutdown();
    }
    private CancellationTokenSource? accountSession;
    public void SignOut() => accountSession?.Cancel();
    private async System.Threading.Tasks.Task RunAccountWithDiagnostics()
    {
        try { await RunAccount(); }
        catch(Exception ex) { Log("Account startup failed: " + ex); MessageBox.Show("Delta could not open: " + ex.Message, "Delta startup error"); Shutdown(1); }
    }
    private async System.Threading.Tasks.Task RunAccount()
    {
        string message="";
        while(true) {
            using var account=new AccountClient();
            var login=new AccountLoginWindow(account,message);MainWindow=login;
            login.Loaded+=(_,_)=>Log("Sign-in window opened.");
            if(login.ShowDialog()!=true){Shutdown();return;}
            var window=new MainWindow(); MainWindow=window;
            using var lifetime=new CancellationTokenSource();accountSession=lifetime;
            bool userClosed=false;
            EventHandler closed=(_,_)=>{userClosed=true;lifetime.Cancel();};
            window.Closed+=closed;window.Show();
            message="You have signed out.";
            try {
                while(!lifetime.IsCancellationRequested) {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(60),lifetime.Token);
                    await account.Validate(lifetime.Token);
                }
            }catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
            catch {message="Your session could not be verified. Sign in again to continue.";}
            finally {
                accountSession=null;
                window.Closed-=closed;
                window.Close();
                await account.Logout();
            }
            if(userClosed){Shutdown();return;}
        }
    }
    private async System.Threading.Tasks.Task LiveNetwork(string keyPath,string wavPath) { int code = await SelfTest.LiveNetwork(keyPath,wavPath,DataFolder); Shutdown(code); }
    private async System.Threading.Tasks.Task AudioSmoke() { int code = await SelfTest.Audio(DataFolder); Shutdown(code); }
    private async System.Threading.Tasks.Task OcrSmoke() { int code = await SelfTest.Ocr(DataFolder); Shutdown(code); }
    private async System.Threading.Tasks.Task Smoke(MainWindow window)
    {
        await System.Threading.Tasks.Task.Delay(15000);
        try { Directory.CreateDirectory(DataFolder); await window.WriteSmokeResult(DataFolder); }
        catch (Exception ex) { Log("Smoke failed: " + ex.Message); }
        window.Close();
    }
    protected override void OnExit(ExitEventArgs e) { windowPolicy?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}


