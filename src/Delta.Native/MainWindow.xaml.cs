using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Delta.Native;
public partial class MainWindow : Window
{
    private sealed class Tab(string name, string url, Brush color)
    {
        public string Name = name; public string Url = url; public Brush Color = color;
        public WebView2CompositionControl? View; public Button? Button; public long Navigation; public bool Initialized; public Task? Initialization; public int InitializationAttempts;
    }
    private readonly List<Tab> tabs = new();
    private Tab? current; private long selectionVersion;
    private CoreWebView2Environment? environment;
    private WindowsServices.ForegroundObserver? foregroundObserver;
    private readonly VoiceService voice = new();
    private CancellationTokenSource? voiceRequest;
    private Task? voiceRunTask;
    private string voiceRecoveryStatus = "Preparing system audio...";
    private System.Windows.Forms.NotifyIcon? tray;
    private HwndSource? source; private bool loaded, capturing, quitting, keyboardEnabled = true;
    private HwndSource? mouseActivationSource;
    private LowLevelShortcutHook? shortcutHook;
    private bool toggleFallbackRegistered;
    private bool localSmoke;
    private bool passiveOverlay; private long originalOverlayBits;
    private ScreenshotSettings screenshotSettings = new();
    private string bridge = "";
    private string bridgeBootstrap = "";
    private const string BridgeNames = "selectTarget,focusTarget,submitTarget,prepareAttachments,attachmentReady,clearJob,attachInPage,voiceText,voiceComparable,updateVoiceDraft,voiceDraftReady";
    private System.Windows.Threading.DispatcherTimer? audioMeter;
    private string typingText = ""; private int typingPosition; private IntPtr typingTarget;
    private CancellationTokenSource? typingCancellation;
    private readonly HashSet<int> registeredShortcuts = new();
    private readonly List<string> shortcutConflicts = new();
    private readonly Dictionary<string,int> shortcutErrors = new();
    private WindowStyle savedStyle; private ResizeMode savedResize; private WindowState savedState; private bool fullscreen;
    private readonly bool automationMode;
    private readonly TaskCompletionSource startupReady = new();
    public MainWindow(bool automation = false)
    {
        automationMode = automation;
        InitializeComponent();
        Speed.Value = 6; // 10% of the existing 1–60 speed range.
        ShowActivated = false;
        Closed += (_, _) => {
            shortcutHook?.Dispose();
            shortcutHook = null;
            mouseActivationSource?.RemoveHook(WndProc);
            mouseActivationSource = null;
        };
        WindowsServices.ProtectWindow(this,(ok,detail)=>{
            CaptureProtectionLabel.Text=ok ? "Capture protection: ON" : "Capture protection: FAILED";
            CaptureProtectionLabel.Foreground=ok ? Brushes.MediumAquamarine : Brushes.OrangeRed;
            CaptureProtectionLabel.ToolTip=detail+" Some capture methods may bypass Windows protection.";
        });
        if (automationMode) { IsEnabled = false; ShowActivated = false; }
        voice.Notice += message => Dispatcher.BeginInvoke(() => SetStatus(message));
        voice.Stopped += () => Dispatcher.BeginInvoke(() => { if (VoiceToggle.IsChecked == true && !quitting) { voiceRecoveryStatus = "System audio ON — session interrupted; reconnecting..."; SetStatus(voiceRecoveryStatus); } });
        voice.BindTarget = () => Dispatcher.InvokeAsync(BindVoiceTarget).Task.Unwrap();
        voice.BindDraft = () => Dispatcher.InvokeAsync(BindLiveDraft).Task.Unwrap();
        audioMeter = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        audioMeter.Tick += (_,_) => {
            VoiceLevel.Value = voice.Active ? voice.PeakLevel : 0;
            string stage = VoiceToggle.IsChecked == true ? (voice.Active ? voice.CurrentStage + " (" + (int)(DateTime.UtcNow-voice.StageSince).TotalSeconds + "s)" : voiceRecoveryStatus) : "Audio off";
            if (VoiceStage.Text != stage) VoiceStage.Text=stage;
            audioMeter.Interval=TimeSpan.FromMilliseconds(IsVisible?120:500);
            VoiceBatchLabel.Text = voice.DictationMode ? "Live dictation · 100ms audio" : "Audio batches: 1s";
            VoiceStage.ToolTip = string.IsNullOrEmpty(voice.LastError) ? voice.CurrentStage : "Last issue: " + voice.LastError;
        };
        PreviewTextInput += (_,e)=>{if(!keyboardEnabled)e.Handled=true;};
        Closed += (_,_) => audioMeter.Stop();
        Activated += (_, _) => RegisterFocusedShortcuts(!passiveOverlay);
        Deactivated += async (_, _) => { RegisterFocusedShortcuts(false); await Task.Delay(250); var foreground = WindowsServices.GetForegroundWindow(); WindowsServices.GetWindowThreadProcessId(foreground, out uint pid); if (pid != Environment.ProcessId) typingTarget = foreground; };
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        // Retain existing SourceInitialized subscribers, including capture protection.
        base.OnSourceInitialized(e);
        mouseActivationSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        mouseActivationSource?.AddHook(WndProc);
        try { NativeWindowMethods.HideFromWindowSwitchers(new WindowInteropHelper(this).Handle); }
        catch (Win32Exception ex) { App.Log("Window switcher styles: " + ex.Message); }
    }
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeWindowMethods.WmMouseActivate)
        {
            // Deliver the click without transferring foreground activation.
            handled = true;
            return new IntPtr(NativeWindowMethods.MaNoActivate);
        }
        return IntPtr.Zero;
    }
    private void SetStatus(string text) { if (!quitting) Status.Text = text; }
    private async Task Guard(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { SetStatus(ex.Message); App.Log(ex.GetType().Name + ": " + ex.Message); } }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return;
        loaded = true;
        try { screenshotSettings = ScreenshotSettings.Load(App.DataFolder); ScreenshotAutoSubmitToggle.IsChecked = screenshotSettings.AutoSubmit; }
        catch (Exception ex) { SetStatus("Screenshot setting could not be loaded; auto-submit is on."); App.Log(ex.GetType().Name + ": Could not load screenshot setting."); }
        var hwnd = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(hwnd); source.AddHook(WindowMessage);
        if (!automationMode) RegisterToggleFallback();
        if (!automationMode) foreach (var shortcut in ShortcutCatalog.All.Where(s => s.Global)) RegisterShortcut(shortcut);
        if (!automationMode) foreach (var shortcut in ShortcutCatalog.All.Where(s => !s.Global)) RegisterShortcut(shortcut);
        RegisterFocusedShortcuts(IsActive);
        foregroundObserver ??= new WindowsServices.ForegroundObserver(window=>typingTarget=window);
        tray = new System.Windows.Forms.NotifyIcon { Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico")), Text = "Delta — Alt+M to show or hide", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip(); menu.Items.Add("Show / hide Delta", null, (_, _) => Dispatcher.Invoke(ToggleWindow)); menu.Items.Add("Toggle click-through (Alt+P)", null, (_, _) => Dispatcher.Invoke(() => SetPassiveOverlay(!passiveOverlay))); menu.Items.Add("Edit Delta", null, (_, _) => Dispatcher.Invoke(() => { SetPassiveOverlay(false); Show(); Activate(); })); menu.Items.Add("Restore window size", null, (_, _) => Dispatcher.Invoke(() => { SetPassiveOverlay(false); WindowState = WindowState.Normal; Width = 1000; Height = 620; Show(); Activate(); })); menu.Items.Add("Quit Delta", null, (_, _) => Dispatcher.Invoke(Close)); tray.ContextMenuStrip = menu; tray.MouseClick += (_, args) => { if (args.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(ToggleWindow); };
        await Guard(async () => {
            bridge = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "chat-bridge.js"));
            bridgeBootstrap="(()=>{window.__deltaBridge=(()=>{"+bridge+";return Object.freeze({"+BridgeNames+"});})();})();";
            Directory.CreateDirectory(App.DataFolder);
            environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataFolder, "WebView2")).WaitAsync(TimeSpan.FromSeconds(20));
            AddTab("ChatGPT", "https://chatgpt.com", Brushes.MediumAquamarine);
            AddTab("Gemini", "https://gemini.google.com", Brushes.CornflowerBlue);
            AddTab("Claude", "https://claude.ai", Brushes.Coral);
            AddTab("Google Meet", "https://meet.google.com", Brushes.MediumSeaGreen);
            AddTab("Grok", "https://grok.com", Brushes.Silver);
            await SelectTab(tabs[0]);
            SetStatus(shortcutConflicts.Count == 0 ? "Ready. Alt+M shows/hides Delta. Ctrl+/ lists shortcuts." : "Some shortcuts could not register. Ctrl+/ shows Windows error details; sidebar controls remain available.");
        });
        startupReady.TrySetResult();
    }
    private void AddTab(string name, string url, Brush color)
    {
        if (tabs.Count >= 30) throw new InvalidOperationException("Close a tab before adding another.");
        var tab = new Tab(name, url, color);
        var panel = new DockPanel { LastChildFill=true };
        var marker=new System.Windows.Shapes.Rectangle {Width=12,Height=12,Fill=color,RadiusX=2,RadiusY=2,Margin=new Thickness(7,0,0,0)};DockPanel.SetDock(marker,Dock.Right);panel.Children.Add(marker);
        panel.Children.Add(new TextBlock { Text = name });
        var button = new Button {Content=panel,Style=(Style)FindResource("AssistantTab")};
        button.Click += async (_, _) => await Guard(() => SelectTab(tab));
        var closeMenu = new ContextMenu(); var close = new MenuItem { Header = "Close tab" }; close.Click += async (_, _) => await Guard(() => CloseTab(tab)); closeMenu.Items.Add(close); button.ContextMenu = closeMenu;
        tab.Button = button; tabs.Add(tab); TabStrip.Children.Add(button);
    }
    private async Task CloseTab(Tab tab)
    {
        int index = tabs.IndexOf(tab); if (index < 0) return;
        bool selected = current == tab;
        if (selected) { selectionVersion++; current = null; }
        tab.Navigation++; tab.View?.Dispose(); if (tab.View != null) BrowserHost.Children.Remove(tab.View);
        tabs.Remove(tab); if (tab.Button != null) TabStrip.Children.Remove(tab.Button);
        if (tabs.Count == 0) AddTab("New tab", "https://chatgpt.com", Brushes.Gray);
        if (selected) await SelectTab(tabs[Math.Min(index, tabs.Count - 1)]);
    }
    private async Task SelectTab(Tab tab)
    {
        if (quitting) return;
        if (current != tab) selectionVersion++;
        current = tab;
        foreach (var t in tabs) { if (t.View != null) t.View.Visibility = t == tab ? Visibility.Visible : Visibility.Collapsed; if (t.Button != null) t.Button.Background = new SolidColorBrush(t == tab ? Color.FromRgb(52, 83, 121) : Color.FromRgb(36, 54, 77)); }
        foreach(var item in tabs)if(item.Button!=null)item.Button.BorderBrush=item==tab?Brushes.DodgerBlue:new SolidColorBrush(Color.FromRgb(40,59,83));
        Address.Text = tab.Url;
        UpdateNavigationButtons();
        if (tab.Initialized) return;
        try { await (tab.Initialization ??= InitializeTab(tab)); }
        catch { tab.Initialization=null; throw; }
    }
    private async Task InitializeTab(Tab tab)
    {
        tab.InitializationAttempts++;
        var view = tab.View;
        if (view == null) { view = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent }; tab.View = view; BrowserHost.Children.Add(view); }
        await view.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(20));
        if (quitting || !tabs.Contains(tab)) return;
        view.Visibility = current == tab ? Visibility.Visible : Visibility.Collapsed;
        var core = view.CoreWebView2;
        if (core == null) throw new InvalidOperationException("Browser initialization did not complete. Press Reload to retry.");
        await core.AddScriptToExecuteOnDocumentCreatedAsync(bridgeBootstrap);
        await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__deltaKeyboardOn=true;for(const kind of ['keydown','keyup','keypress'])document.addEventListener(kind,event=>{if(window.__deltaKeyboardOn===false&&event.isTrusted){event.preventDefault();event.stopImmediatePropagation();}},true);");
        core.ContentLoading += async (_,_) => await Guard(()=>ApplyKeyboard(tab));
        core.HistoryChanged += (_,_)=>{if(current==tab)UpdateNavigationButtons();};
        tab.Initialized = true;
        core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = false; core.Settings.AreHostObjectsAllowed = false;
        core.NavigationStarting += (_, args) => { tab.Navigation++; if (!IsWeb(args.Uri) && !(localSmoke && args.Uri == "about:blank")) args.Cancel = true; };
        core.SourceChanged += (_, _) => { tab.Url = core.Source; if (current == tab) {Address.Text = tab.Url;UpdateNavigationButtons();} };
        core.NavigationCompleted += async (_, args) => { if (!args.IsSuccess) SetStatus("Page load failed: " + args.WebErrorStatus); else await Guard(async () => {await ApplyKeyboard(tab);await ApplyCursor(tab);}); };
        core.NewWindowRequested += (_, args) => { args.Handled = true; if (IsWeb(args.Uri)) Dispatcher.BeginInvoke(async () => await Guard(async () => { AddTab(new Uri(args.Uri).Host, args.Uri, Brushes.Gray); await SelectTab(tabs[^1]); })); };
        core.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; if (args.PermissionKind is CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera) { using var deferral = args.GetDeferral(); if (MessageBox.Show(this, new Uri(args.Uri).Host + " requests " + args.PermissionKind + " access.", "Website permission", MessageBoxButton.YesNo) == MessageBoxResult.Yes) args.State = CoreWebView2PermissionState.Allow; args.SavesInProfile = false; } };
        core.DownloadStarting += (_, args) => {
            try {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string folder = Path.Combine(desktop, "Delta Downloads");
                Directory.CreateDirectory(folder);
                string name = Path.GetFileName(args.ResultFilePath);
                if (string.IsNullOrWhiteSpace(name)) name = "download";
                string path = Path.Combine(folder, name);
                for (int suffix = 1; File.Exists(path) || Directory.Exists(path); suffix++)
                    path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + " (" + suffix + ")" + Path.GetExtension(name));
                args.ResultFilePath = path;
                args.Handled = true;
                var download = args.DownloadOperation;
                download.StateChanged += (_, _) => {
                    if (download.State == CoreWebView2DownloadState.Completed) SetStatus("Downloaded to Desktop / Delta Downloads: " + Path.GetFileName(download.ResultFilePath));
                    else if (download.State == CoreWebView2DownloadState.Interrupted) SetStatus("Download interrupted: " + download.InterruptReason);
                };
                SetStatus("Downloading to Desktop / Delta Downloads: " + Path.GetFileName(path));
            } catch (Exception ex) { args.Cancel = true; SetStatus("Download could not start: " + ex.Message); }
        };
        core.ProcessFailed += (_, args) => SetStatus("Browser process stopped: " + args.ProcessFailedKind + ". Reload this tab.");
        view.PreviewKeyDown += (_, args) => { if (!keyboardEnabled) args.Handled = true; };
        core.Navigate(tab.Url);
    }
    private static bool IsWeb(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") && uri.UserInfo.Length == 0;
    private static string ResolveAddress(string address) { address = address.Trim(); if (IsWeb(address)) return address; if (!address.Contains(' ') && address.Contains('.') && IsWeb("https://" + address)) return "https://" + address; return "https://www.google.com/search?q=" + Uri.EscapeDataString(address); }
    private async Task<JsonElement> Script(Tab tab, string expression)
    {
        if (tab.View?.CoreWebView2 == null) throw new InvalidOperationException("Wait for the browser tab to initialize.");
        string invocation="(()=>{if(!window.__deltaBridge)return {__deltaBridgeMissing:true};const {"+BridgeNames+"}=window.__deltaBridge;return ("+expression+");})()";
        string result=await tab.View.ExecuteScriptAsync(invocation).WaitAsync(TimeSpan.FromSeconds(5));
        var parsed=JsonSerializer.Deserialize<JsonElement>(result);
        if(parsed.ValueKind==JsonValueKind.Object && parsed.TryGetProperty("__deltaBridgeMissing",out _)) {
            await tab.View.ExecuteScriptAsync(bridgeBootstrap).WaitAsync(TimeSpan.FromSeconds(5));
            result=await tab.View.ExecuteScriptAsync(invocation).WaitAsync(TimeSpan.FromSeconds(5));
            parsed=JsonSerializer.Deserialize<JsonElement>(result);
        }
        return parsed;
    }
    private sealed record Destination(Tab Tab, long Selection, long Navigation, string Token);
    private async Task<Destination> Target()
    {
        var tab = current ?? throw new InvalidOperationException("Open a chat first.");
        var dest = new Destination(tab, selectionVersion, tab.Navigation, Guid.NewGuid().ToString("N"));
        if (!(await Script(tab, $"selectTarget({JsonSerializer.Serialize(dest.Token)})")).GetBoolean()) throw new InvalidOperationException("Click the chatbot input first.");
        Validate(dest); return dest;
    }
    private void Validate(Destination dest) { if (quitting || current != dest.Tab || selectionVersion != dest.Selection || dest.Tab.Navigation != dest.Navigation) throw new InvalidOperationException("Chat changed. Automatic submission canceled."); }
    private async Task WaitForAttachment(Destination dest, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30); DateTime? readySince = null;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested(); Validate(dest);
            bool ready = (await Script(dest.Tab, $"attachmentReady({JsonSerializer.Serialize(dest.Token)})")).GetBoolean();
            if (!ready) readySince = null; else readySince ??= DateTime.UtcNow;
            if (readySince != null && DateTime.UtcNow - readySince >= TimeSpan.FromMilliseconds(600)) return;
            await Task.Delay(150, token);
        }
        throw new TimeoutException("Screenshot was attached, but upload completion was not confirmed. Check the chat before sending.");
    }
    private async Task<bool> Submit(Destination dest, bool attachments, CancellationToken token, Func<bool>? canSubmit = null)
    {
        if (attachments) await WaitForAttachment(dest, token);
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested(); Validate(dest);
            if (canSubmit != null && !canSubmit()) return false;
            Validate(dest); var result = await Script(dest.Tab, $"submitTarget({JsonSerializer.Serialize(dest.Token)})");
            if (result.GetProperty("submitted").GetBoolean()) return true;
            if (!result.TryGetProperty("pending", out var pending) || !pending.GetBoolean()) throw new InvalidOperationException("This chat did not accept auto-submit. Use its Send button.");
            await Task.Delay(150, token);
        }
        throw new TimeoutException("Upload or Send timed out. Check the attachment, then use Send.");
    }
    private async Task InsertText(Destination dest, string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Validate(dest);
        if (!(await Script(dest.Tab, $"focusTarget({JsonSerializer.Serialize(dest.Token)})")).GetBoolean()) throw new InvalidOperationException("Chat input disappeared.");
        Validate(dest);
        var inserted = await Script(dest.Tab, $"document.execCommand('insertText',false,{JsonSerializer.Serialize(text)})");
        if (inserted.ValueKind != JsonValueKind.True) throw new InvalidOperationException("The chat input did not accept text.");
    }
    private Task<DictationCallbacks> BindLiveDraft()
    {
        var tab = current ?? throw new InvalidOperationException("Open the destination chat first.");
        var origin = new Destination(tab,selectionVersion,tab.Navigation,"");
        string originalUrl = tab.Url;
        Destination? destination = null;
        bool awaitingCreatedConversation=false;
        void CheckOrigin(CancellationToken token) {
            token.ThrowIfCancellationRequested(); Validate(origin);
            if (tab.Url != originalUrl && awaitingCreatedConversation &&
                Uri.TryCreate(originalUrl,UriKind.Absolute,out var before) &&
                Uri.TryCreate(tab.Url,UriKind.Absolute,out var after) &&
                before.Host == "chatgpt.com" && before.AbsolutePath == "/" &&
                after.Scheme == before.Scheme && after.Host == before.Host && after.AbsolutePath.StartsWith("/c/")) {
                originalUrl=tab.Url; awaitingCreatedConversation=false;
            }
            if (tab.Url != originalUrl) throw new InvalidOperationException("Conversation changed. Turn System audio off and on in the destination chat.");
        }
        async Task Update(string text,CancellationToken token) {
            await Dispatcher.InvokeAsync(async () => {
                CheckOrigin(token);
                destination ??= await Target();
                for (int attempt=0; ; attempt++) {
                    CheckOrigin(token);
                    var result = await Script(tab,$"updateVoiceDraft({JsonSerializer.Serialize(destination.Token)},{JsonSerializer.Serialize(text)})");
                    if (result.GetProperty("ok").GetBoolean()) break;
                    string reason=result.GetProperty("reason").GetString() ?? "input";
                    if(reason=="edited") throw new InvalidOperationException("The draft contains a manual edit. Turn System audio off/on to resume delivery; your edit is preserved.");
                    if(attempt>=3) throw new InvalidOperationException("Waiting for the chat editor. Recognition continues; delivery will retry.");
                    await Task.Delay(80,token);
                }
            }).Task.Unwrap();
        }
        async Task Send(CancellationToken token) {
            await Dispatcher.InvokeAsync(async () => {
                CheckOrigin(token);
                if (destination == null || !(await Script(tab,$"voiceDraftReady({JsonSerializer.Serialize(destination.Token)})")).GetBoolean())
                    throw new InvalidOperationException("Chat draft changed; automatic send paused.");
                awaitingCreatedConversation=Uri.TryCreate(originalUrl,UriKind.Absolute,out var sourceUri) && sourceUri.Host=="chatgpt.com" && sourceUri.AbsolutePath=="/";
                await Submit(destination,false,token);
                await Script(tab,$"clearJob({JsonSerializer.Serialize(destination.Token)})");
                destination=null;
                // ChatGPT assigns a conversation URL after the first successful send.
                originalUrl=tab.Url;
            }).Task.Unwrap();
        }
        return Task.FromResult(new DictationCallbacks(Update,Send));
    }
    private Task<Func<string, CancellationToken, Task>> BindVoiceTarget() => BindVoiceTargetFor(voice.AutoSend);
    private Task<Func<string, CancellationToken, Task>> BindVoiceTargetFor(bool autoSend)
    {
        var tab = current ?? throw new InvalidOperationException("Open the destination chat first.");
        var origin = new Destination(tab, selectionVersion, tab.Navigation, "");
        string originalUrl = tab.Url;
        Func<string,CancellationToken,Task> submit = async (text, token) => await Dispatcher.InvokeAsync(async () => {
            token.ThrowIfCancellationRequested(); Validate(origin);
            if (tab.Url != originalUrl) throw new InvalidOperationException("The conversation changed. Turn System audio off and on in the destination chat.");
            // React chat pages can replace the editor during a network request.
            // Bind the fresh editor only after transcription, while preserving the
            // original tab/navigation identity captured when speech began.
            var destination = await Target();
            try { await InsertText(destination, text + (autoSend ? "" : " "), token); if (autoSend) await Submit(destination, false, token); }
            finally { if (destination.Tab.View?.CoreWebView2 != null) await Script(destination.Tab, $"clearJob({JsonSerializer.Serialize(destination.Token)})"); }
        }).Task.Unwrap();
        return Task.FromResult(submit);
    }
    private async Task TakeScreenshot(bool crop, bool ocr)
    {
        if (passiveOverlay && (crop || ocr)) { SetStatus("Crop selection needs normal interaction. Press Alt+P to exit click-through first."); return; }
        if (capturing) { SetStatus("Wait for the current capture or upload to finish."); return; } capturing = true;
        var mode = screenshotSettings.Snapshot();
        Destination? dest = null;
        try
        {
            dest = await Target();
            var bytes = await WindowsServices.CaptureAsync(this);
            if (crop || ocr) { var selection = new CropWindow(bytes) { Owner = this }; if (selection.ShowDialog() != true || selection.Result == null) return; bytes = selection.Result; }
            Validate(dest);
            if (ocr)
            {
                SetStatus("Recognizing text..."); string text = await WindowsServices.Recognize(bytes); Validate(dest);
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("No readable text found.");
                await SubmitOcrText(dest, text); SetStatus("OCR text submitted.");
            }
            else
            {
                await UploadScreenshot(dest, bytes, mode);
            }
        }
        finally { capturing = false; if (dest?.Tab.View?.CoreWebView2 != null) await Script(dest.Tab, $"clearJob({JsonSerializer.Serialize(dest.Token)})"); }
    }
    private async Task SubmitOcrText(Destination dest, string text)
    {
        await InsertText(dest, text, CancellationToken.None);
        await Submit(dest, false, CancellationToken.None);
    }
    private async Task<bool> UploadScreenshot(Destination dest, byte[] bytes, ScreenshotSettings.CaptureMode mode)
    {
        Validate(dest);
        string name = "Delta-screenshot-" + DateTime.Now.ToString("HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..6] + ".png";
        if (!(await Script(dest.Tab, $"prepareAttachments({JsonSerializer.Serialize(dest.Token)},[{JsonSerializer.Serialize(name)}])")).GetBoolean()) throw new InvalidOperationException("The chat input changed before upload.");
        var images = new[] { new { name, dataURL = "data:image/png;base64," + Convert.ToBase64String(bytes) } };
        Validate(dest);
        var result = await Script(dest.Tab, $"attachInPage({JsonSerializer.Serialize(images)},{JsonSerializer.Serialize(dest.Tab.Url)})");
        if (result.GetProperty("method").GetString() is "unsupported" or "page-changed") throw new InvalidOperationException("This page did not accept the screenshot.");
        SetStatus("Uploading screenshot...");
        await WaitForAttachment(dest, CancellationToken.None);
        bool sent = screenshotSettings.CanSubmit(mode) && await Submit(dest, false, CancellationToken.None, () => screenshotSettings.CanSubmit(mode));
        SetStatus(sent ? "Screenshot uploaded and submitted." : "Screenshot uploaded only. Press Send in the chat when ready.");
        return sent;
    }
    private void ScreenshotAutoSubmitChanged(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;
        ScreenshotAutoSubmitToggle.Content = ScreenshotAutoSubmitToggle.IsChecked == true ? "Screenshot Auto-Submit: ON" : "Screenshot Auto-Submit: OFF";
        screenshotSettings.Set(ScreenshotAutoSubmitToggle.IsChecked == true);
        try { screenshotSettings.Save(App.DataFolder); }
        catch (Exception) { SetStatus("Screenshot mode changed, but could not be saved for the next launch."); return; }
        SetStatus(screenshotSettings.AutoSubmit ? "Screenshots: upload and auto-submit. OCR text also auto-submits." : "Screenshots: upload only. OCR text still auto-submits.");
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x312 && (int)wParam == 1 && toggleFallbackRegistered) {
            handled = true; _ = Guard(() => RunShortcut("toggle-window")); return IntPtr.Zero;
        }
        if ((passiveOverlay || !keyboardEnabled) && message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (shortcutHook == null && message == 0x312 && registeredShortcuts.Contains((int)wParam)) {
            var shortcut = ShortcutCatalog.All.FirstOrDefault(s => s.Id == (int)wParam);
            if (shortcut != null) { handled = true; _ = Guard(() => RunShortcut(shortcut.Action)); }
        }
        return IntPtr.Zero;
    }
    private void RegisterToggleFallback()
    {
        if (source == null || toggleFallbackRegistered) return;
        toggleFallbackRegistered = WindowsServices.RegisterHotKey(source.Handle, 1, 0x4001, 0x4D);
        if (!toggleFallbackRegistered) App.Log("Alt+M fallback registration failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
    private void RegisterShortcut(DeltaShortcut shortcut)
    {
        if (source == null || registeredShortcuts.Contains(shortcut.Id)) return;
        try {
            shortcutHook ??= new LowLevelShortcutHook(TryHandleShortcut);
            registeredShortcuts.Add(shortcut.Id);
            shortcutConflicts.Remove(shortcut.Display);
            shortcutErrors.Remove(shortcut.Display);
        } catch (Exception ex) {
            App.Log("Shortcut dispatcher: " + ex.Message);
            shortcutErrors[shortcut.Display] = ex.InnerException is Win32Exception native ? native.NativeErrorCode : -1;
            if (!shortcutConflicts.Contains(shortcut.Display)) shortcutConflicts.Add(shortcut.Display);
        }
    }
    private void RegisterFocusedShortcuts(bool active)
    {
        if (source == null || quitting || automationMode) return;
        // All catalog shortcuts now use one universal hook, regardless of focus.
        // Keep this entry point for existing activation and keyboard event handlers.
        foreach (var shortcut in ShortcutCatalog.All) RegisterShortcut(shortcut);
    }
    private bool TryHandleShortcut(uint key, uint modifiers)
    {
        string? action = ShortcutCatalog.ActionFor(key, modifiers);
        if (action == null || Dispatcher.HasShutdownStarted) return false;
        // Never block the native hook on UI work, navigation, or modal dialogs.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => {
            if (!quitting) _ = Guard(() => RunShortcut(action));
        }));
        return true;
    }
    private async Task RunShortcut(string action)
    {
        switch (action) {
            case "passive-overlay": SetPassiveOverlay(!passiveOverlay); break;
            case "toggle-window": ToggleWindow(); break;
            case "screenshot": await TakeScreenshot(false, false); break;
            case "crop": await TakeScreenshot(true, false); break;
            case "ocr": await TakeScreenshot(true, true); break;
            case "keyboard-toggle": KeyboardToggle.IsChecked = KeyboardToggle.IsChecked != true; break;
            case "typing-insert": await InsertClipboardIntoExternal(); break;
            case "typing-step": await StepCopiedCharacterAsync(); break;
            case "opacity-down": WindowOpacity.Value = Math.Max(WindowOpacity.Minimum, WindowOpacity.Value - .1); break;
            case "opacity-up": WindowOpacity.Value = Math.Min(1, WindowOpacity.Value + .1); break;
            case "new-tab": if (environment == null) throw new InvalidOperationException("Browser is still starting."); AddTab("New tab", "https://chatgpt.com", Brushes.Gray); await SelectTab(tabs[^1]); break;
            case "close-tab": if (current != null) await CloseTab(current); break;
            case "address": Show(); Address.Focus(); Address.SelectAll(); break;
            case "reload": await NavigateCurrent("reload"); break;
            case "next-tab": case "previous-tab": if (current != null) await SelectTab(tabs[(tabs.IndexOf(current) + tabs.Count + (action == "next-tab" ? 1 : -1)) % tabs.Count]); break;
            case "zoom-in": if (current?.View != null) current.View.ZoomFactor = Math.Min(3, current.View.ZoomFactor + .1); break;
            case "zoom-out": if (current?.View != null) current.View.ZoomFactor = Math.Max(.25, current.View.ZoomFactor - .1); break;
            case "zoom-reset": if (current?.View != null) current.View.ZoomFactor = 1; break;
            case "shortcuts": MessageBox.Show(this, string.Join("\n", ShortcutCatalog.All.Select(s => s.Display + " — " + s.Action)) + (shortcutErrors.Count == 0 ? "" : "\n\nUnavailable on this launch:\n" + string.Join("\n", shortcutErrors.Select(p => p.Key + ": Windows error " + p.Value))), "Delta shortcuts"); break;
            case "fullscreen": if (!fullscreen) { savedStyle = WindowStyle; savedResize = ResizeMode; savedState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; fullscreen = true; } else { WindowState = WindowState.Normal; WindowStyle = savedStyle; ResizeMode = savedResize; WindowState = savedState; fullscreen = false; } break;
        }
    }
    private async Task InsertClipboardCharactersIntoExternal()
    {
        if (typingCancellation != null) throw new InvalidOperationException("Pause typing before inserting clipboard text.");
        string text = System.Windows.Clipboard.GetText();
        if (text.Length == 0) throw new InvalidOperationException("Copy text or code first.");
        var foreground = WindowsServices.GetForegroundWindow(); WindowsServices.GetWindowThreadProcessId(foreground, out uint pid);
        if (pid != Environment.ProcessId && foreground != IntPtr.Zero) typingTarget = foreground;
        if (typingTarget == IntPtr.Zero || !WindowsServices.IsWindow(typingTarget)) throw new InvalidOperationException("Click the external editor once before inserting.");
        typingCancellation = new CancellationTokenSource(); var token = typingCancellation.Token;
        Hide(); SetStatus("Focus the external editor. Inserting clipboard text in one second.");
        try { await Task.Delay(1000, token); foreach (char value in text) { token.ThrowIfCancellationRequested(); WindowsServices.TypeCharacter(typingTarget, value); await Task.Delay(1, token); } }
        finally { typingCancellation.Dispose(); typingCancellation = null; }
    }
    internal async Task WriteSmokeResult(string directory)
    {
        string startupStatus = Status.Text;
        double startupOpacity=Opacity;
        bool voiceDraftPassed = false; bool liveComposerPassed=false; bool liveEditGuardPassed=false; bool liveRepeatPassed=false; bool richEditorPassed=false; bool richEditorRebindPassed=false; bool captureProtectionPassed=false; bool captureExclusionPixelsPassed=false; string? captureExclusionPixel=null; bool capturePersistencePassed=false; bool dialogCaptureProtectionPassed=false; bool voiceConversationGuardPassed = false;
        bool voiceRebindPassed = false;
        bool voiceValidationPassed = false;
        bool overlayPassed = false; string? overlayError = null;
        bool resizePassed = false; bool smallWindowPassed = false;
        bool downloadPassed = false; bool transparencyPassed = false; string? transparencyError = null;
        bool textPassed = false, imagePassed = false, stalePassed = false, uploadOnlyPassed = false, nextScreenshotPassed = false, ocrAutoSubmitPassed = false, revokePassed = false, capturePassed = false, shortcutActionsPassed = false;
        string? testError = null, captureError = null;
        try {
            await startupReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (current?.View?.CoreWebView2 is not { } core) throw new InvalidOperationException("Browser did not initialize.");
            localSmoke = true;
            var ready = new TaskCompletionSource();
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) { ready.TrySetResult(); }
            core.NavigationCompleted += Completed;
            string fixtureFolder = Path.Combine(directory, "fixture"); Directory.CreateDirectory(fixtureFolder);
            await File.WriteAllTextAsync(Path.Combine(fixtureFolder, "index.html"), "<!doctype html><html><body><form><textarea id='prompt-textarea'></textarea><input type='file' accept='image/png' onchange=\"let p=document.createElement('div');p.dataset.testid='attachment';p.setAttribute('aria-busy','true');this.form.append(p);let n=this.files[0].name;setTimeout(()=>{p.textContent=n;p.setAttribute('aria-busy','false')},window.uploadDelay||0)\"><button type='button' data-testid='send-button' onclick='window.submissions=(window.submissions||0)+1'>Send</button></form></body></html>");
            core.SetVirtualHostNameToFolderMapping("delta-test.example", fixtureFolder, CoreWebView2HostResourceAccessKind.DenyCors);
            try { core.Navigate("https://delta-test.example/index.html"); await ready.Task.WaitAsync(TimeSpan.FromSeconds(6)); }
            finally { core.NavigationCompleted -= Completed; }
            for (int retry = 0; retry < 50; retry++) {
                if ((await current.View.ExecuteScriptAsync("!!document.querySelector('textarea')?.getClientRects().length").WaitAsync(TimeSpan.FromSeconds(2))) == "true") break;
                await Task.Delay(100);
            }
            var dest = await Target();
            await InsertText(dest, "native bridge test", CancellationToken.None);
            await Submit(dest, false, CancellationToken.None);
            textPassed = (await Script(current, "document.querySelector('textarea').value==='native bridge test' && window.submissions===1")).GetBoolean();
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jFZkAAAAASUVORK5CYII=");
            ScreenshotAutoSubmitToggle.IsChecked = true;
            var imageDest = await Target();
            imagePassed = await UploadScreenshot(imageDest, png, screenshotSettings.Snapshot()) && (await Script(current, "window.submissions===2 && document.querySelector('input').files.length===1")).GetBoolean();
            ScreenshotAutoSubmitToggle.IsChecked = false;
            var uploadDest = await Target();
            uploadOnlyPassed = !await UploadScreenshot(uploadDest, png, screenshotSettings.Snapshot()) && (await Script(current, "window.submissions===2 && document.querySelector('input').files.length===1")).GetBoolean() && !ScreenshotSettings.Load(App.DataFolder).AutoSubmit;
            await Script(current, "document.querySelector('textarea').value=''");
            var ocrDest = await Target(); await SubmitOcrText(ocrDest, "OCR text check");
            ocrAutoSubmitPassed = (await Script(current, "window.submissions===3 && document.querySelector('textarea').value==='OCR text check'")).GetBoolean();
            ScreenshotAutoSubmitToggle.IsChecked = true;
            var nextDest = await Target();
            nextScreenshotPassed = await UploadScreenshot(nextDest, png, screenshotSettings.Snapshot()) && (await Script(current, "window.submissions===4")).GetBoolean();
            await Script(current, "window.uploadDelay=1200");
            var revokeDest = await Target(); var uploading = UploadScreenshot(revokeDest, png, screenshotSettings.Snapshot());
            await Task.Delay(250); ScreenshotAutoSubmitToggle.IsChecked = false; ScreenshotAutoSubmitToggle.IsChecked = true;
            revokePassed = !await uploading && (await Script(current, "window.submissions===4")).GetBoolean();
            await Script(current, "clearJob(" + JsonSerializer.Serialize(revokeDest.Token) + ")");
            try {
                captureProtectionPassed=WindowsServices.CaptureAffinity(this)==WindowsServices.ExcludeFromCapture;
                capturePassed = (await WindowsServices.CaptureAsync(this)).Length > 100;
                capturePersistencePassed=WindowsServices.CaptureAffinity(this)==WindowsServices.ExcludeFromCapture;
            } catch (Exception ex) { captureError = ex.Message; }
            double zoom = current.View.ZoomFactor; await RunShortcut("zoom-in"); bool zoomChanged = current.View.ZoomFactor > zoom; await RunShortcut("zoom-reset");
            double opacity = WindowOpacity.Value; await RunShortcut("opacity-down"); bool opacityChanged = WindowOpacity.Value < opacity; WindowOpacity.Value = opacity;
            shortcutActionsPassed = zoomChanged && opacityChanged && current.View.ZoomFactor == 1;
            string downloadName = "Delta-smoke-" + Guid.NewGuid().ToString("N") + ".txt";
            string downloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Delta Downloads", downloadName);
            try {
                await Script(current, "(()=>{const a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['Delta download test'],{type:'text/plain'}));a.download=" + JsonSerializer.Serialize(downloadName) + ";document.body.append(a);a.click();a.remove();return true;})()");
                for (int attempt = 0; attempt < 60 && !File.Exists(downloadPath); attempt++) await Task.Delay(100);
                if (File.Exists(downloadPath)) {
                    for (int attempt = 0; attempt < 20; attempt++) {
                        try { downloadPassed = await File.ReadAllTextAsync(downloadPath) == "Delta download test"; if (downloadPassed) break; } catch (IOException) { }
                        await Task.Delay(100);
                    }
                }
            } finally { if (File.Exists(downloadPath)) File.Delete(downloadPath); }
            // Compare actual screen pixels over a controlled backdrop, including the browser.
            var backdrop = new Window { Left = Left, Top = Top, Width = ActualWidth, Height = ActualHeight, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Background = Brushes.Magenta };
            double originalOpacity = WindowOpacity.Value;
            try {
                await Script(current, "document.body.style.background='white';document.body.style.margin='0';true");
                backdrop.Show(); Topmost = false; Topmost = true; Activate();
                WindowOpacity.Value = 1; await Task.Delay(700);
                var point = BrowserHost.PointToScreen(new System.Windows.Point(BrowserHost.ActualWidth / 2, BrowserHost.ActualHeight / 2));
                System.Drawing.Color Pixel() { using var b = new System.Drawing.Bitmap(1,1); using var g = System.Drawing.Graphics.FromImage(b); g.CopyFromScreen((int)point.X,(int)point.Y,0,0,new System.Drawing.Size(1,1)); return b.GetPixel(0,0); }
                var excluded=Pixel(); captureExclusionPixel=excluded.ToString();
                captureExclusionPixelsPassed=excluded.R>230 && excluded.B>230 && excluded.G<30;
                Hide(); Show(); await Task.Delay(150);
                capturePersistencePassed &= WindowsServices.CaptureAffinity(this)==WindowsServices.ExcludeFromCapture;
                // Disable only inside this opt-in fixture to measure visible opacity.
                WindowsServices.SetCaptureAffinity(this,0); await Task.Delay(300);
                var opaque = Pixel();
                WindowOpacity.Value = .5; await Task.Delay(700); var half = Pixel();
                transparencyPassed = opaque.R > 230 && opaque.G > 230 && opaque.B > 230 && half.R > 230 && half.B > 230 && half.G > 80 && half.G < 180;
                transparencyError = transparencyPassed ? null : $"Opaque={opaque}, half={half}";
            } catch (Exception ex) { transparencyError = ex.Message; }
            finally { WindowsServices.SetCaptureAffinity(this,WindowsServices.ExcludeFromCapture); WindowOpacity.Value = originalOpacity; backdrop.Close(); }
            double savedLeft = Left, savedTop = Top, savedWidth = Width, savedHeight = Height;
            try {
                resizePassed = true;
                foreach (string edge in new[] { "L", "R", "T", "B", "LT", "RT", "LB", "RB" }) {
                    Left = savedLeft; Top = savedTop; Width = 1000; Height = 620; UpdateLayout();
                    ResizeFromEdge(edge, 20, 15); UpdateLayout();
                    double expectedWidth = edge.Contains('L') ? 980 : edge.Contains('R') ? 1020 : 1000;
                    double expectedHeight = edge.Contains('T') ? 605 : edge.Contains('B') ? 635 : 620;
                    resizePassed &= Math.Abs(ActualWidth - expectedWidth) < 1 && Math.Abs(ActualHeight - expectedHeight) < 1;
                    resizePassed &= Math.Abs(Left - savedLeft - (edge.Contains('L') ? 20 : 0)) < 1 && Math.Abs(Top - savedTop - (edge.Contains('T') ? 15 : 0)) < 1;
                }
                Width = 1000; Height = 620; UpdateLayout();
                ResizeFromEdge("RB", -880, -540); UpdateLayout();
                smallWindowPassed = ActualWidth <= 121 && ActualHeight <= 81 && MinWidth == 1 && MinHeight == 1 && ResizeHandles.Children.Count == 8;
            } finally { Left = savedLeft; Top = savedTop; Width = savedWidth; Height = savedHeight; UpdateLayout(); }
            var overlayTargetButton = new Button { Content = "Delta local input test target" };
            int targetClicks = 0;
            overlayTargetButton.Click += (_,_) => targetClicks++;
            bool originalEnabled = IsEnabled;
            var overlayTarget = new Window { Left = Left, Top = Top, Width = ActualWidth, Height = ActualHeight, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Content = overlayTargetButton };
            try {
                IsEnabled = true; Hide(); overlayTarget.Topmost = true; overlayTarget.Show(); await Task.Delay(300);
                var seedPoint = overlayTarget.PointToScreen(new System.Windows.Point(overlayTarget.ActualWidth / 2, overlayTarget.ActualHeight / 2));
                await WindowsServices.ClickFixtureAt((int)seedPoint.X, (int)seedPoint.Y);
                overlayTarget.Topmost = false; targetClicks = 0;
                var targetHwnd = new WindowInteropHelper(overlayTarget).Handle;
                var ownHwnd = new WindowInteropHelper(this).Handle;
                bool targetFocused = WindowsServices.GetForegroundWindow() == targetHwnd;
                long initialBits = WindowsServices.OverlayStyleBits(ownHwnd);
                await RunShortcut("passive-overlay");
                ToggleWindow(); await Task.Delay(200);
                bool stylesApplied = passiveOverlay && WindowsServices.OverlayStyleBits(ownHwnd) == WindowsServices.OverlayStyleMask;
                bool activationUnchanged = WindowsServices.GetForegroundWindow() == targetHwnd;
                var point = BrowserHost.PointToScreen(new System.Windows.Point(BrowserHost.ActualWidth / 2, BrowserHost.ActualHeight / 2));
                int clicksBefore = targetClicks;
                if (targetFocused && activationUnchanged)
                    await WindowsServices.ClickFixtureAt((int)point.X, (int)point.Y);
                bool clickedThrough = targetClicks > clicksBefore && WindowsServices.GetForegroundWindow() == targetHwnd;
                ToggleWindow(); ToggleWindow(); await Task.Delay(200);
                bool reshowKeptFocus = IsVisible && WindowsServices.GetForegroundWindow() == targetHwnd;
                await RunShortcut("passive-overlay");
                bool restored = !passiveOverlay && WindowsServices.OverlayStyleBits(ownHwnd) == initialBits;
                overlayPassed = targetFocused && stylesApplied && activationUnchanged && clickedThrough && reshowKeptFocus && restored;
                if (!overlayPassed) overlayError = $"targetFocused={targetFocused}, stylesApplied={stylesApplied}, activationUnchanged={activationUnchanged}, clickedThrough={clickedThrough}, targetClicks={targetClicks}, reshowKeptFocus={reshowKeptFocus}, restored={restored}";
            } catch (Exception ex) { overlayError = ex.Message; }
            finally { SetPassiveOverlay(false); overlayTarget.Close(); IsEnabled = originalEnabled; if (!IsVisible) Show(); }
            string badKeyFile = Path.Combine(fixtureFolder, "invalid-key.txt");
            await File.WriteAllTextAsync(badKeyFile, "private-test-marker\ninvalid second line");
            var voiceSetup = new VoiceSetupWindow(new VoicePreferences { KeyFile = badKeyFile }) { Owner = this };
            try {
                voiceSetup.Show(); await Task.Delay(200);
                dialogCaptureProtectionPassed=WindowsServices.CaptureAffinity(voiceSetup)==WindowsServices.ExcludeFromCapture;
                voiceValidationPassed = !voiceSetup.SaveSelection() && voiceSetup.IsVisible && voiceSetup.ValidationMessage.Contains("Key format not recognized") && !voiceSetup.ValidationMessage.Contains("private-test-marker");
                voiceSetup.UpdateLayout();
                var setupBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)voiceSetup.ActualWidth, (int)voiceSetup.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                setupBitmap.Render(voiceSetup);
                var setupEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); setupEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(setupBitmap));
                using var setupStream = File.Create(Path.Combine(directory, "voice-setup-preview.png")); setupEncoder.Save(setupStream);
            } finally { voiceSetup.Close(); }
            var voiceSend = await BindVoiceTarget();
            int previousSends = (await Script(current, "window.submissions||0")).GetInt32();
            await Script(current, "(()=>{const old=document.querySelector('textarea');const fresh=old.cloneNode(true);fresh.value='';old.replaceWith(fresh);return true;})()");
            await voiceSend("Replaced editor voice test", CancellationToken.None);
            voiceRebindPassed = (await Script(current, "document.querySelector('textarea').value==='Replaced editor voice test' && window.submissions===" + (previousSends+1))).GetBoolean();
            var draftInsert = await BindVoiceTargetFor(false);
            int draftSends = (await Script(current, "window.submissions||0")).GetInt32();
            await Script(current, "document.querySelector('textarea').value=''");
            await draftInsert("Live dictation draft", CancellationToken.None);
            voiceDraftPassed = (await Script(current, "document.querySelector('textarea').value==='Live dictation draft ' && window.submissions===" + draftSends)).GetBoolean();
            var pendingDraft = await BindVoiceTargetFor(false);
            string draftBeforeSwitch = (await Script(current,"document.querySelector('textarea').value")).GetString() ?? "";
            selectionVersion++;
            try { await pendingDraft("Must not reach another chat",CancellationToken.None); }
            catch (InvalidOperationException) { voiceConversationGuardPassed = (await Script(current,"document.querySelector('textarea').value")).GetString()==draftBeforeSwitch; }
            await Script(current,"document.querySelector('textarea').value='Existing note'");
            var liveCallbacks=await BindLiveDraft();
            var liveDraft=new DictationDraft(liveCallbacks.Update,liveCallbacks.Send);
            int liveSends=(await Script(current,"window.submissions")).GetInt32();
            liveDraft.SpeechResumed();
            await liveDraft.TranscriptAsync("What is",false,CancellationToken.None);
            await liveDraft.TranscriptAsync("What is your name?",false,CancellationToken.None);
            liveComposerPassed=(await Script(current,"document.querySelector('textarea').value==='Existing note What is your name?' && window.submissions==="+liveSends)).GetBoolean();
            await liveDraft.TranscriptAsync("What is your name?",true,CancellationToken.None);
            await liveDraft.SilenceAsync(CancellationToken.None);
            await Script(current,"document.querySelector('textarea').value=''");
            liveDraft.SpeechResumed();
            await liveDraft.TranscriptAsync("Second question",true,CancellationToken.None);
            await liveDraft.SilenceAsync(CancellationToken.None);
            liveRepeatPassed=(await Script(current,"document.querySelector('textarea').value==='Second question' && window.submissions==="+(liveSends+2))).GetBoolean();
            await Script(current,"document.querySelector('textarea').value=''");
            var editedCallbacks=await BindLiveDraft();
            await editedCallbacks.Update("Dictated",CancellationToken.None);
            await Script(current,"document.querySelector('textarea').value='My edit'");
            try { await editedCallbacks.Update("Replacement",CancellationToken.None); }
            catch(InvalidOperationException) { liveEditGuardPassed=(await Script(current,"document.querySelector('textarea').value==='My edit'")).GetBoolean(); }
            await Script(current,"(()=>{document.querySelector('textarea').remove();const e=document.createElement('div');e.id='prompt-textarea';e.contentEditable='true';e.setAttribute('role','textbox');e.style.minHeight='50px';document.querySelector('form').prepend(e);window.realExec=document.execCommand.bind(document);document.execCommand=(...args)=>{const result=window.realExec(...args);if(args[0]==='insertText'){e.innerHTML='<p>'+e.textContent.replace(/ /g,'&nbsp;')+'</p><p><br></p>';return false;}return result;};return true;})()");
            var richCallbacks=await BindLiveDraft();
            await richCallbacks.Update("Just go",CancellationToken.None);
            await richCallbacks.Update("Just go back",CancellationToken.None);
            richEditorPassed=(await Script(current,"voiceComparable(document.querySelector('#prompt-textarea').innerText)==='Just go back'")).GetBoolean();
            await Script(current,"(()=>{document.execCommand=window.realExec;const e=document.querySelector('#prompt-textarea');e.replaceWith(e.cloneNode(true));return true;})()");
            await richCallbacks.Update("Just go back and continue",CancellationToken.None);
            await richCallbacks.Send(CancellationToken.None);
            richEditorRebindPassed=(await Script(current,"voiceComparable(document.querySelector('#prompt-textarea').innerText)==='Just go back and continue'")).GetBoolean();
            var previousTab=current;
            AddTab("Initialization race","about:blank",Brushes.Gray);
            var raceTab=tabs[^1];
            await Task.WhenAll(SelectTab(raceTab),SelectTab(raceTab),SelectTab(raceTab));
            bool singleInitialization=raceTab.InitializationAttempts==1;
            await SelectTab(previousTab!);await CloseTab(raceTab);
            var measure=System.Diagnostics.Stopwatch.StartNew();
            const int rounds=60;
            for(int n=0;n<rounds;n++)await Script(current,"voiceComparable('Delta benchmark "+n+"')");
            double cachedMs=measure.Elapsed.TotalMilliseconds;measure.Restart();
            for(int n=0;n<rounds;n++)await current.View!.ExecuteScriptAsync("(()=>{"+bridge+";return voiceComparable('Delta benchmark "+n+"');})()");
            double legacyMs=measure.Elapsed.TotalMilliseconds;
            await Script(current,"(window.__bridgeIdentity=window.__deltaBridge,true)");
            await Script(current,"voiceComparable('cache stays installed')");
            bool cacheStable=(await Script(current,"window.__bridgeIdentity===window.__deltaBridge")).GetBoolean();
            await Script(current,"(delete window.__deltaBridge,true)");
            bool cacheRecovery=(await Script(current,"voiceComparable('Recovered')==='Recovered'")).GetBoolean();
            bool directEditorFastPath=(await Script(current,"(()=>{document.activeElement?.blur();const original=document.querySelectorAll;document.querySelectorAll=function(s){if(s==='*')throw Error('Full document scan');return original.call(this,s);};try{return selectTarget('fast-fixture');}finally{document.querySelectorAll=original;clearJob('fast-fixture');}})()")).GetBoolean();
            bool shadowEditorFallback=(await Script(current,"(()=>{document.activeElement?.blur();const old=document.querySelector('#prompt-textarea');old.contentEditable='false';old.removeAttribute('id');const host=document.createElement('div');document.body.append(host);const shadow=host.attachShadow({mode:'open'});shadow.innerHTML='<textarea></textarea>';try{return selectTarget('shadow-fixture')&&window.__deltaVoiceTargets.get('shadow-fixture')===shadow.querySelector('textarea');}finally{clearJob('shadow-fixture');host.remove();old.id='prompt-textarea';old.contentEditable='true';}})()")).GetBoolean();
            File.WriteAllText(Path.Combine(directory,"performance.json"),JsonSerializer.Serialize(new {rounds,cachedMs,legacyMs,cacheStable,cacheRecovery,singleInitialization,
                directEditorFastPath,shadowEditorFallback,
                legacyHelperCharacters=bridge.Length,cachedCallCharacters=BridgeNames.Length+160,idleAudioTimerStopped=audioMeter?.IsEnabled!=true,
                liveFrameCopiesAvoidedPerSecond=50}));
            bool wasEnabled=IsEnabled;IsEnabled=true;
            await File.WriteAllTextAsync(Path.Combine(fixtureFolder,"second.html"),"<!doctype html><title>Delta navigation two</title><textarea id='prompt-textarea'></textarea>");
            async Task AwaitPage(string expression) {
                for(int n=0;n<100;n++) {try{if((await Script(current!,expression)).ValueKind==JsonValueKind.True)return;}catch{} await Task.Delay(60);}
                throw new InvalidOperationException("Navigation fixture did not reach expected document.");
            }
            current!.View!.CoreWebView2.Navigate("https://delta-test.example/second.html");
            await AwaitPage("document.title==='Delta navigation two'");UpdateNavigationButtons();
            bool backAvailable=BackButton.IsEnabled;
            BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitPage("location.pathname==='/index.html'");UpdateNavigationButtons();
            bool forwardAvailable=ForwardButton.IsEnabled;
            ForwardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitPage("document.title==='Delta navigation two'");
            await Script(current,"window.__reloadDirty=true");
            RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitPage("document.title==='Delta navigation two'&&window.__reloadDirty===undefined");
            bool navigationControlsPassed=backAvailable&&forwardAvailable;
            var receiverText=new TextBox();var receiver=new Window{Title="Delta keyboard test receiver",Topmost=true,Width=300,Height=120,Content=receiverText,ShowInTaskbar=false};
            bool keyboardOffPassed=false,keyboardOnPassed=false;
            try {
                receiver.Show();receiver.Activate();receiverText.Focus();
                typingTarget=new WindowInteropHelper(receiver).Handle;
                KeyboardToggle.IsChecked=false;await Task.Delay(250);
                bool blockedStyle=(WindowsServices.OverlayStyleBits(new WindowInteropHelper(this).Handle)&0x08000000L)!=0;
                bool pageOff=(await Script(current,"window.__deltaKeyboardOn===false")).GetBoolean();
                receiver.Activate();receiverText.Focus();await Task.Delay(120);WindowsServices.TypeCharacter(typingTarget,'o');await Task.Delay(150);
                keyboardOffPassed=blockedStyle&&pageOff&&receiverText.Text=="o";
                KeyboardToggle.IsChecked=true;await Task.Delay(300);
                keyboardOnPassed=(WindowsServices.OverlayStyleBits(new WindowInteropHelper(this).Handle)&0x08000000L)==0 &&
                    (await Script(current,"window.__deltaKeyboardOn===true")).GetBoolean() && WindowsServices.GetForegroundWindow()==new WindowInteropHelper(this).Handle;
            } finally {KeyboardToggle.IsChecked=true;receiver.Close();IsEnabled=wasEnabled;}
            File.WriteAllText(Path.Combine(directory,"navigation-keyboard.json"),JsonSerializer.Serialize(new {navigationControlsPassed,keyboardOffPassed,keyboardOnPassed}));
            var stale = await Target(); selectionVersion++;
            try { Validate(stale); } catch (InvalidOperationException) { stalePassed = true; }
        } catch (Exception ex) { testError = ex.Message; }
        File.WriteAllText(Path.Combine(directory, "ui-smoke.json"), JsonSerializer.Serialize(new { startupOpacity, captureExclusionPixel, captureProtectionPassed, captureExclusionPixelsPassed, capturePersistencePassed, dialogCaptureProtectionPassed, richEditorPassed, richEditorRebindPassed, liveComposerPassed, liveEditGuardPassed, liveRepeatPassed, voiceConversationGuardPassed, voiceDraftPassed, voiceRebindPassed, voiceValidationPassed, overlayPassed, overlayError, resizePassed, smallWindowPassed, transparencyPassed, transparencyError, downloadPassed, toolbarToggleVisible = ScreenshotAutoSubmitToggle.IsVisible, toolbarToggleLabel = ScreenshotAutoSubmitToggle.Content.ToString(), windowVisible = IsVisible, showInTaskbar = ShowInTaskbar, tabCount = tabs.Count, browserInitialized = current?.View?.CoreWebView2 != null, startupStatus, textPassed, imagePassed, uploadOnlyPassed, nextScreenshotPassed, ocrAutoSubmitPassed, revokePassed, capturePassed, captureError, shortcutActionsPassed, stalePassed, physicalShortcutRegistrationTested = false, shortcutConflicts, shortcutErrors, testError, width = ActualWidth, height = ActualHeight }));
        // Design-only sample page, after all functional checks have been saved.
        IsEnabled=true;
        await File.WriteAllTextAsync(Path.Combine(directory,"fixture","preview.html"),"<!doctype html><html><head><meta charset=\"utf-8\"><style>body{margin:0;background:#000;color:#ddd;font-family:Segoe UI,Arial,sans-serif;height:100vh;display:grid;place-items:center}.sample{position:absolute;top:15px;right:20px;color:#66788c;font-size:11px}.chat{width:80%;text-align:center}h1{font-size:23px;font-weight:400;margin:0 0 38px}.composer{border-radius:30px;background:#202020;text-align:left;padding:20px 24px;color:#a5a5a5;font-size:14px}.plus{font-size:22px;margin-right:14px;color:#ddd}.voice{float:right;border-radius:20px;padding:2px 8px;background:#2078df;color:white}</style></head><body><span class='sample'>Delta interface preview · sample content</span><div class='chat'><h1>Where should we begin?</h1><div class='composer'><span class='plus'>+</span>Ask anything<span class='voice'>↑</span></div></div></body></html>");
        current!.View!.CoreWebView2.Navigate("https://delta-test.example/preview.html");
        for(int attempt=0;attempt<60;attempt++) {
            if(await current.View.ExecuteScriptAsync("!!document.querySelector('.sample')")=="true")break;
            await Task.Delay(60);
        }
        SetStatus("Delta interface preview - both windows start at 50% opacity");
        await Task.Delay(500);UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, "ui-preview.png")); encoder.Save(stream);
    }
    private void OverlayClick(object sender, RoutedEventArgs e) => SetPassiveOverlay(!passiveOverlay);
    private void SetPassiveOverlay(bool enabled)
    {
        if (enabled == passiveOverlay) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        long before = WindowsServices.OverlayStyleBits(hwnd);
        try {
            if (enabled) {
                if (capturing) throw new InvalidOperationException("Wait for the current capture to finish.");
                originalOverlayBits = before;
                WindowsServices.SetOverlayStyleBits(hwnd, WindowsServices.OverlayStyleMask);
                ShowActivated = false;
                passiveOverlay = true;
                RegisterFocusedShortcuts(false);
            } else {
                WindowsServices.SetOverlayStyleBits(hwnd, NativeWindowMethods.WsExNoActivate);
                passiveOverlay = false;
                ShowActivated = false;
                RegisterFocusedShortcuts(IsActive);
            }
            OverlayButton.Content = enabled ? "Click-through: ON (Alt+P)" : "Click-through: OFF";
            if (tray != null) tray.Text = enabled ? "Delta click-through — Alt+P to edit" : "Delta — Alt+M to show or hide";
            SetStatus(enabled
                ? (registeredShortcuts.Contains(9) || automationMode ? "Click-through ON. Keep your other app focused. Alt+P restores Delta controls." : "Click-through ON. Alt+P is unavailable; use Delta's tray menu → Edit Delta.")
                : "Click-through OFF. Delta controls are available.");
        } catch (Exception ex) {
            try { WindowsServices.SetOverlayStyleBits(hwnd, before); } catch { }
            SetStatus(ex.Message); App.Log("Overlay: " + ex.Message);
        }
    }
    private void ToggleWindow() { if (quitting) return; if (IsVisible) Hide(); else { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; WindowsServices.ShowWithoutActivation(new WindowInteropHelper(this).Handle); } }
    private void WindowClosing(object? sender, CancelEventArgs e) { quitting = true; shortcutHook?.Dispose(); shortcutHook = null; foregroundObserver?.Dispose(); voiceRequest?.Cancel(); voice.Dispose(); typingCancellation?.Cancel(); tray?.Dispose(); if (source != null) { if (toggleFallbackRegistered) { WindowsServices.UnregisterHotKey(source.Handle, 1); toggleFallbackRegistered = false; } registeredShortcuts.Clear(); source.RemoveHook(WindowMessage); } foreach (var tab in tabs) tab.View?.Dispose(); }
    private void DragWindow(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is Grid && e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void QuitClick(object sender, RoutedEventArgs e) => Close();
    private void ResizeDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromEdge((string)((System.Windows.Controls.Primitives.Thumb)sender).Tag, e.HorizontalChange, e.VerticalChange);
        e.Handled = true;
    }
    private void ResizeFromEdge(string edge, double dx, double dy)
    {
        if (WindowState != WindowState.Normal || fullscreen) return;
        double width = ActualWidth, height = ActualHeight;
        if (edge.Contains('L')) { double next = Math.Max(MinWidth, width - dx); Left += width - next; Width = next; }
        if (edge.Contains('R')) Width = Math.Max(MinWidth, width + dx);
        if (edge.Contains('T')) { double next = Math.Max(MinHeight, height - dy); Top += height - next; Height = next; }
        if (edge.Contains('B')) Height = Math.Max(MinHeight, height + dy);
    }
    private void ResizeClick(object sender, RoutedEventArgs e) { Width = Width < 1100 ? 1200 : 1000; Height = Height < 700 ? 760 : 620; }
    private void SpeedStepClick(object sender,RoutedEventArgs e) => Speed.Value=Math.Clamp(Speed.Value+double.Parse((string)((Button)sender).Tag,System.Globalization.CultureInfo.InvariantCulture),Speed.Minimum,Speed.Maximum);
    private void SupportClick(object sender,RoutedEventArgs e) => OpenSupport();
    internal static void OpenSupport() {try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mailto:delta@acranix.com"){UseShellExecute=true});}catch{MessageBox.Show("For help, email delta@acranix.com.","Delta support");}}
    private void SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (SpeedLabel != null) SpeedLabel.Text = Math.Round(e.NewValue / 60 * 100) + "%"; }
    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { Opacity = e.NewValue; if (OpacityLabel != null) OpacityLabel.Text = Math.Round(e.NewValue * 100) + "%"; }
    private void OpacityPreset(object sender, RoutedEventArgs e) => WindowOpacity.Value = double.Parse((string)((Button)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
    private void PauseChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (PauseLabel != null) PauseLabel.Text = e.NewValue.ToString("0.0") + "s"; if (voice != null) voice.SetPause(e.NewValue); }
    private async void KeyboardChanged(object sender, RoutedEventArgs e) {
        keyboardEnabled=KeyboardToggle?.IsChecked==true;
        if(!loaded || source==null)return;
        await Guard(async()=>{
            if(keyboardEnabled && passiveOverlay)SetPassiveOverlay(false);
            WindowsServices.SetOverlayStyleBits(source.Handle,passiveOverlay?WindowsServices.OverlayStyleMask:NativeWindowMethods.WsExNoActivate);
            ShowActivated=false;
            RegisterFocusedShortcuts(keyboardEnabled&&IsActive);
            if(!keyboardEnabled) {
                Keyboard.ClearFocus();
                bool returned=typingTarget!=IntPtr.Zero&&WindowsServices.IsWindow(typingTarget)&&WindowsServices.SetForegroundWindow(typingTarget);
                SetStatus(returned?"Keyboard OFF: typing goes to your other app.":"Keyboard OFF: click your other app to type there. Alt+K restores Delta keyboard input.");
            } else {
                Show();Activate();current?.View?.Focus();SetStatus("Keyboard ON: typing goes to Delta while Delta has focus.");
            }
            foreach(var tab in tabs)await ApplyKeyboard(tab);
            if(keyboardEnabled && current?.View?.CoreWebView2!=null)
                await Script(current,"(()=>{const token='keyboard-focus';if(!selectTarget(token))return false;const result=focusTarget(token);clearJob(token);return result;})()");
        });
    }
    private async Task ApplyKeyboard(Tab tab) {
        if(tab.View?.CoreWebView2!=null)
            await tab.View.ExecuteScriptAsync("window.__deltaKeyboardOn="+(keyboardEnabled?"true":"false"));
    }
    private async Task ApplyCursor(Tab tab) { if (tab.View?.CoreWebView2 == null) return; await tab.View.ExecuteScriptAsync("(()=>{let s=document.getElementById('__delta_cursor');if(!s){s=document.createElement('style');s.id='__delta_cursor';document.documentElement.append(s);}s.textContent=" + JsonSerializer.Serialize(CursorToggle.IsChecked == true ? "*{cursor:none!important}" : "") + ";})()"); }
    private async void CursorChanged(object sender, RoutedEventArgs e) { if (loaded) await Guard(async () => { foreach (var tab in tabs) await ApplyCursor(tab); }); }
    private void AccountSignOutClick(object sender,RoutedEventArgs e) => ((App)System.Windows.Application.Current).SignOut();
    private bool OpenVoiceSetup()
    {
        if (passiveOverlay) { SetStatus("Press Alt+P to exit click-through before changing Voice setup."); return false; }
        VoicePreferences preferences;
        try { preferences = VoicePreferences.Load(App.DataFolder); } catch { preferences = new(); }
        var setup = new VoiceSetupWindow(preferences) { Owner = this };
        return setup.ShowDialog() == true;
    }
    private void VoiceSetupClick(object sender, RoutedEventArgs e)
    {
        VoiceToggle.IsChecked = false;
        if (OpenVoiceSetup()) SetStatus("Voice setup saved. Turn System audio on to listen to playback.");
    }
    private void VoiceChanged(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;
        if(VoiceToggle.IsChecked==true) audioMeter?.Start();
        else {audioMeter?.Stop();VoiceLevel.Value=0;VoiceStage.Text="Audio off";}
        if (VoiceToggle.IsChecked != true) {
            voiceRequest?.Cancel();
            SetStatus("Stopping system audio and finishing already captured text...");
            return;
        }
        if (voiceRequest != null && !voiceRequest.IsCancellationRequested) return;
        var request = new CancellationTokenSource();
        var previous = voiceRunTask;
        voiceRequest = request; voiceRecoveryStatus = "Connecting system audio...";
        voiceRunTask = RunRequestedVoice(request, previous);
    }
    private async Task RunRequestedVoice(CancellationTokenSource request, Task? previous)
    {
        try {
            if (previous != null) await previous;
            var runner = new ContinuousVoiceRunner(async token => {
                token.ThrowIfCancellationRequested();
                voiceRecoveryStatus = "Connecting system audio...";
                var preferences = VoicePreferences.Load(App.DataFolder);
                string? path = preferences.ResolveKeyFile(AppContext.BaseDirectory);
                if (path == null) throw new FileNotFoundException("Choose your key file in Voice setup.");
                await voice.StartAsync(ApiSettings.LoadFile(path), preferences.OutputDeviceId, preferences.LiveDictation, preferences.AutoSend, token);
            }, () => voice.Active, () => voice.StopAsync(), message => {
                voiceRecoveryStatus = message; SetStatus(message);
            });
            await runner.RunAsync(request.Token);
        } catch (Exception ex) {
            // Keep the user's ON request intact even if stopping/cleanup failed.
            voiceRecoveryStatus = "System audio ON — " + ex.Message; SetStatus(voiceRecoveryStatus);
        } finally {
            if (ReferenceEquals(voiceRequest,request) && request.IsCancellationRequested && !quitting)
                SetStatus("System audio off. Final text is in the chat draft or Voice details.");
        }
    }
    private async void ScreenshotClick(object sender, RoutedEventArgs e) => await Guard(() => TakeScreenshot(false, false));
    private async void CropClick(object sender, RoutedEventArgs e) => await Guard(() => TakeScreenshot(true, false));
    private async void OcrClick(object sender, RoutedEventArgs e) => await Guard(() => TakeScreenshot(true, true));
    private void UpdateNavigationButtons() {
        var core=current?.View?.CoreWebView2;
        BackButton.IsEnabled=core?.CanGoBack==true;
        ForwardButton.IsEnabled=core?.CanGoForward==true;
        RefreshButton.IsEnabled=current!=null;
        BackButton.ToolTip=BackButton.IsEnabled?"Back":"No earlier page in this Delta tab";
        ForwardButton.ToolTip=ForwardButton.IsEnabled?"Forward":"No later page in this Delta tab";
    }
    private async Task NavigateCurrent(string action) {
        var tab=current;if(tab==null)return;
        if(!tab.Initialized)await SelectTab(tab);
        var core=tab.View?.CoreWebView2??throw new InvalidOperationException("Browser is not ready. Try again shortly.");
        if(action=="back") {if(core.CanGoBack)core.GoBack();else SetStatus("No earlier page in this Delta tab.");}
        else if(action=="forward") {if(core.CanGoForward)core.GoForward();else SetStatus("No later page in this Delta tab.");}
        else core.Reload();
        UpdateNavigationButtons();
    }
    private async void BackClick(object sender,RoutedEventArgs e)=>await Guard(()=>NavigateCurrent("back"));
    private async void ForwardClick(object sender,RoutedEventArgs e)=>await Guard(()=>NavigateCurrent("forward"));
    private async void ReloadClick(object sender,RoutedEventArgs e)=>await Guard(()=>NavigateCurrent("reload"));
    private async void AddressKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && current?.View?.CoreWebView2 != null) { e.Handled = true; await Guard(() => { current.View.CoreWebView2.Navigate(ResolveAddress(Address.Text)); return Task.CompletedTask; }); } }
    private async void NewTabClick(object sender, RoutedEventArgs e) => await Guard(async () => { if (environment == null) throw new InvalidOperationException("Browser is still starting."); AddTab("New tab", "https://chatgpt.com", Brushes.Gray); await SelectTab(tabs[^1]); });
    private async void CopyClick(object sender, RoutedEventArgs e) => await Guard(async () => { if (current != null) { var result = await Script(current, "window.getSelection().toString()"); System.Windows.Clipboard.SetText(result.GetString() ?? ""); } });
    private async void PasteClick(object sender, RoutedEventArgs e) => await Guard(async () => { var dest = await Target(); try { await InsertText(dest, System.Windows.Clipboard.GetText(), CancellationToken.None); } finally { await Script(dest.Tab, $"clearJob({JsonSerializer.Serialize(dest.Token)})"); } });
    private async void EnterClick(object sender, RoutedEventArgs e) => await Guard(async () => { var dest = await Target(); try { await Submit(dest, false, CancellationToken.None); } finally { await Script(dest.Tab, $"clearJob({JsonSerializer.Serialize(dest.Token)})"); } });
    private void EditTypingText(object sender, RoutedEventArgs e)
    {
        typingCancellation?.Cancel(); var dialog = new Window { Title = "Insert text", Width = 500, Height = 320, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Foreground, ShowInTaskbar = false, Topmost = true };
        WindowsServices.ProtectWindow(dialog);
        var panel = new DockPanel { Margin = new Thickness(12) }; var button = new Button { Content = "Use text", Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button); var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = typingText }; panel.Children.Add(text); dialog.Content = panel;
        button.Click += (_, _) => { typingText = text.Text; typingPosition = 0; dialog.DialogResult = true; }; dialog.ShowDialog(); SetStatus("Text loaded. Click the external editor once, return to Delta, then Start.");
    }
    private async void StartLegacyTypingClick(object sender, RoutedEventArgs e) => await Guard(async () => {
        if (typingCancellation != null || typingPosition >= typingText.Length) return;
        if (typingTarget == IntPtr.Zero || !WindowsServices.IsWindow(typingTarget)) throw new InvalidOperationException("Click the external editor once before starting.");
        typingCancellation = new CancellationTokenSource(); var token = typingCancellation.Token; Hide(); SetStatus("Typing starts in 3 seconds. Focus the external editor.");
        try { await Task.Delay(3000, token); while (typingPosition < typingText.Length) { token.ThrowIfCancellationRequested(); WindowsServices.TypeCharacter(typingTarget, typingText[typingPosition]); typingPosition++; await Task.Delay(Math.Max(15, (int)(1000 / Speed.Value)), token); } }
        finally { typingCancellation?.Dispose(); typingCancellation = null; }
    });
    private void PauseLegacyTypingClick(object sender, RoutedEventArgs e) { typingCancellation?.Cancel(); SetStatus("Typing paused."); }
    private void ResetLegacyTypingClick(object sender, RoutedEventArgs e) { typingCancellation?.Cancel(); typingPosition = 0; SetStatus("Typing reset."); }
    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (shortcutHook != null) {
            // Recognized physical keys were consumed before WPF; avoid a second
            // dispatch for injected input. Retain the existing Escape action.
            if (e.Key == Key.Escape) typingCancellation?.Cancel();
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key; var modifiers = Keyboard.Modifiers;
        uint nativeModifiers = ((modifiers & ModifierKeys.Alt) != 0 ? 1u : 0) | ((modifiers & ModifierKeys.Control) != 0 ? 2u : 0) | ((modifiers & ModifierKeys.Shift) != 0 ? 4u : 0) | ((modifiers & ModifierKeys.Windows) != 0 ? 8u : 0);
        string? action = ShortcutCatalog.ActionFor((uint)KeyInterop.VirtualKeyFromKey(key), nativeModifiers);
        if(!keyboardEnabled && !ShortcutCatalog.All.Any(shortcut=>shortcut.Global&&shortcut.Action==action)) {e.Handled=true;return;}
        if (action != null && !e.IsRepeat) { e.Handled = true; await Guard(() => RunShortcut(action)); }
        else if (key == Key.Escape) typingCancellation?.Cancel();
    }
}


























