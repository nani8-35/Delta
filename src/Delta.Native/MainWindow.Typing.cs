using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Delta.Native;

public partial class MainWindow
{
    private readonly TypingBuffer typingBuffer = new();
    private Task? typingTask;
    private readonly SemaphoreSlim typingStepQueue = new(1, 1);
    private int typingResetVersion;

    private async Task StepCopiedCharacterAsync()
    {
        int version = typingResetVersion;
        await typingStepQueue.WaitAsync();
        try {
            if (!quitting && version == typingResetVersion)
                await RunTypingMasterAsync(false, singleCharacter: true);
        } finally { typingStepQueue.Release(); }
    }

    private Task InsertClipboardIntoExternal() => RunTypingMasterAsync(true);
    private async void InsertClick(object sender, RoutedEventArgs e) => await Guard(InsertClipboardIntoExternal);
    private async void StartTypingClick(object sender, RoutedEventArgs e) => await Guard(() => RunTypingMasterAsync(false));
    private void PauseTypingClick(object sender, RoutedEventArgs e)
    {
        typingCancellation?.Cancel();
        typingResetVersion++; // Cancel queued manual steps without clearing progress.
        SetStatus("Typing paused. Start resumes from the next character.");
    }
    private void ResetTypingClick(object sender, RoutedEventArgs e)
    {
        typingCancellation?.Cancel();
        typingResetVersion++;
        typingBuffer.Reset(); typingText = ""; typingPosition = 0;
        SetStatus("Typing buffer cleared. Copy code, then use Start or Insert. Existing editor text is unchanged.");
    }

    private IntPtr ResolveTypingTarget()
    {
        IntPtr foreground = WindowsServices.GetForegroundWindow();
        WindowsServices.GetWindowThreadProcessId(foreground, out uint pid);
        if (foreground != IntPtr.Zero && pid != Environment.ProcessId) typingTarget = foreground;
        if (typingTarget == IntPtr.Zero || !WindowsServices.IsWindow(typingTarget))
            throw new InvalidOperationException("Click the destination code editor once, then click Start or Insert.");
        WindowsServices.GetWindowThreadProcessId(typingTarget, out pid);
        if (pid == Environment.ProcessId) throw new InvalidOperationException("Choose an editor outside Delta.");
        return typingTarget; // Pin the destination; the foreground observer cannot redirect this run.
    }

    private static async Task<string> ReadCopiedCodeAsync(CancellationToken token)
    {
        for (int attempt = 0; ; attempt++) {
            token.ThrowIfCancellationRequested();
            try {
                if (!Clipboard.ContainsText()) throw new InvalidOperationException("Copy code first using the code block's Copy button.");
                string text = Clipboard.GetText();
                if (text.Length == 0) throw new InvalidOperationException("The clipboard is empty. Copy code first.");
                return text;
            } catch (ExternalException) when (attempt < 8) { await Task.Delay(50, token); }
        }
    }

    private async Task FocusTypingTargetAsync(IntPtr target, CancellationToken token)
    {
        if (WindowsServices.GetForegroundWindow() != target) {
            if (!WindowsServices.SetForegroundWindow(target))
                throw new InvalidOperationException("Click the destination editor first, then retry.");
            await Task.Delay(100, token);
        }
        token.ThrowIfCancellationRequested();
        if (WindowsServices.GetForegroundWindow() != target)
            throw new InvalidOperationException("The destination editor does not have focus.");
    }

    private async Task RunTypingMasterAsync(bool pasteAll, bool singleCharacter = false)
    {
        if (typingCancellation != null) { SetStatus("Typing is already running. Use Pause first."); return; }
        var request = new CancellationTokenSource();
        typingCancellation = request;
        try {
            string clipboard = await ReadCopiedCodeAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!pasteAll && (typingBuffer.Text.Length == 0 || (typingBuffer.Complete && clipboard != typingBuffer.Text))) {
                typingBuffer.Load(clipboard); typingText = clipboard; typingPosition = 0;
            }
            if (!pasteAll && typingBuffer.Complete) { SetStatus("Typing completed. Use Reset to load copied code again."); return; }
            IntPtr target = ResolveTypingTarget();
            typingTask = ExecuteTypingMasterAsync(target, clipboard, pasteAll, request.Token, singleCharacter);
            await typingTask;
        } finally {
            if (ReferenceEquals(typingCancellation, request)) { typingCancellation = null; typingTask = null; }
            request.Dispose();
        }
    }

    private async Task ExecuteTypingMasterAsync(IntPtr target, string clipboard, bool pasteAll, CancellationToken token, bool singleCharacter = false)
    {
        ClipboardCodeWriter? writer = null;
        try {
            await FocusTypingTargetAsync(target, token);
            writer = new ClipboardCodeWriter(target, clipboard);
            if (pasteAll) {
                token.ThrowIfCancellationRequested();
                await writer.WriteAsync(clipboard);
                SetStatus("Copied code pasted into the destination editor.");
            } else if (singleCharacter) {
                await typingBuffer.StepAsync(writer.WriteAsync, token);
                typingPosition = typingBuffer.Position;
                if (!quitting) SetStatus(typingBuffer.Complete ? "Typing completed. Reset loads copied code again."
                    : "One character entered. Alt+, enters the next; Start continues automatically.");
            } else {
                SetStatus("Typing copied code. Pause stops; Start resumes. Speed can be changed while typing.");
                await typingBuffer.RunAsync(writer.WriteAsync, () => Speed.Value, token);
                typingPosition = typingBuffer.Position;
                if (!quitting) SetStatus("Typing completed.");
            }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            typingPosition = typingBuffer.Position;
        } finally {
            if (writer != null) await writer.RestoreAsync();
        }
    }
}
