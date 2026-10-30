using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Delta.Native;
public sealed class LiveSpeechClient : IDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<byte[]?> outbound = Channel.CreateBounded<byte[]?>(new BoundedChannelOptions(50) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Func<string,bool,Task> transcript;
    private readonly Action<string> failure;
    private readonly TaskCompletionSource setup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource endSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finalAfterEnd = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? sender, receiver;
    private bool ending, disposed;
    public LiveSpeechClient(Func<string,bool,Task> transcript, Action<string> failure) { this.transcript = transcript; this.failure = failure; }
    public async Task ConnectAsync(string key, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token,stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try {
            // The credential goes in a request header, never into a logged URL or page.
            socket.Options.SetRequestHeader("x-goog-api-key",key);
            await socket.ConnectAsync(new Uri("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent"),deadline.Token);
            await SendJson(new { setup = new { model = "models/gemini-3.5-transcribe-live",
                generationConfig = new { responseModalities = new[] { "TEXT" } },
                inputAudioTranscription = new { languageCodes = Array.Empty<string>() } } }, deadline.Token);
            receiver = ReceiveLoop();
            await setup.Task.WaitAsync(deadline.Token);
            sender = SendLoop();
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { Dispose(); throw; }
        catch { Dispose(); throw new InvalidOperationException("Live transcription could not connect. Check Google Live model access, quota and your connection. Choose Request mode in Voice setup if needed."); }
    }
    public bool QueuePcm(byte[] pcm) => !disposed && !ending && outbound.Writer.TryWrite(pcm);
    public bool QueueUtteranceEnd() => QueuePcm(Array.Empty<byte>());
    private Task SendJson(object value, CancellationToken token) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))), WebSocketMessageType.Text, true, token);
    private async Task SendLoop()
    {
        try {
            await foreach (var pcm in outbound.Reader.ReadAllAsync(stop.Token)) {
                if (pcm == null) {
                    await SendJson(new { realtimeInput = new { audioStreamEnd = true } },stop.Token);
                    endSent.TrySetResult(); break;
                }
                if (pcm.Length == 0) { await SendJson(new { realtimeInput = new { audioStreamEnd = true } },stop.Token); continue; }
                await SendJson(new { realtimeInput = new { audio = new { data = Convert.ToBase64String(pcm), mimeType = "audio/pcm;rate=16000" } } },stop.Token);
            }
        } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { Fail("Live audio connection was interrupted. Turn System audio off/on to reconnect."); }
    }
    public readonly record struct Update(bool Setup, string Interim, string Final, bool TurnComplete, bool Error);
    public static Update Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (root.TryGetProperty("error",out _)) return new(false,"","",false,true);
        if (root.TryGetProperty("setupComplete",out _)) return new(true,"","",false,false);
        if (!root.TryGetProperty("serverContent",out var content)) return new(false,"","",false,false);
        string Text(string name) => content.TryGetProperty(name,out var part) && part.TryGetProperty("text",out var text) ? text.GetString() ?? "" : "";
        return new(false,Text("interimInputTranscription"),Text("inputTranscription"),content.TryGetProperty("turnComplete",out var done) && done.ValueKind==JsonValueKind.True,false);
    }
    private async Task ReceiveLoop()
    {
        var buffer = new byte[16384];
        try {
            while (!stop.IsCancellationRequested) {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer),stop.Token);
                    if (result.MessageType==WebSocketMessageType.Close) {
                        if (!ending) Fail("Google closed the Live session. Check model access/quota, then reconnect.");
                        setup.TrySetException(new InvalidOperationException("Live session closed."));
                        return;
                    }
                    message.Write(buffer,0,result.Count);
                    if (message.Length > 1_048_576) throw new InvalidDataException();
                } while (!result.EndOfMessage);
                var update = Parse(Encoding.UTF8.GetString(message.ToArray()));
                if (update.Error) { Fail("Google rejected the Live request. Check model access and quota."); return; }
                if (update.Setup) setup.TrySetResult();
                if (update.Interim.Length>0) await transcript(update.Interim,false);
                if (update.Final.Length>0) { await transcript(update.Final,true); if (ending) finalAfterEnd.TrySetResult(); }
                if (update.TurnComplete && ending) finalAfterEnd.TrySetResult();
            }
        } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { if (!disposed) Fail("Live transcription stopped unexpectedly. The last draft remains in the chat input."); }
    }
    private void Fail(string message) { setup.TrySetException(new InvalidOperationException(message)); if (!disposed) failure(message); }
    public async Task FinishAsync()
    {
        if (disposed) return;
        ending = true;
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            await outbound.Writer.WriteAsync(null,timeout.Token);
            await endSent.Task.WaitAsync(timeout.Token);
            await Task.WhenAny(finalAfterEnd.Task,Task.Delay(3500,timeout.Token));
        } catch (OperationCanceledException) { }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed=true;
        outbound.Writer.TryComplete(); stop.Cancel(); socket.Abort(); socket.Dispose();
    }
}

