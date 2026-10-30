using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Delta.Native;
public sealed class VoiceService : IDisposable
{
    private WasapiLoopbackCapture? capture;
    private MMDevice? playbackDevice;
    private LiveSpeechClient? live;
    private CancellationTokenSource? liveLifetime;
    private static readonly byte[] SilentFrame = new byte[640];
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private readonly byte[] liveBuffer = new byte[3200];
    private int liveOffset; private int liveOverflow;
    private readonly PlaybackSilence playbackSilence = new();
    private DictationDraft? draft;
    public Func<Task<DictationCallbacks>>? BindDraft;
    public bool DictationMode { get; private set; }
    public bool AutoSend { get; private set; } = true;
    public string InterimTranscript { get; private set; } = "";
    public async Task StartAsync(ApiSettings settings, string? outputDeviceId, bool streaming, bool autoSend, CancellationToken startToken = default)
    {
        if (active) return;
        if (live != null) throw new InvalidOperationException("Wait for the previous Live session to finish closing.");
        DictationMode = streaming; AutoSend = autoSend; InterimTranscript = ""; liveOverflow = 0;
        if (!streaming) { Start(settings,outputDeviceId); return; }
        liveLifetime = new CancellationTokenSource(); var lifetime = liveLifetime.Token;
        Report("Connecting to live dictation…");
        try {
            var target = await (BindDraft?.Invoke() ?? throw new InvalidOperationException("Open the destination chat first."));
            draft = new DictationDraft(target.Update,target.Send) { AutoSend = autoSend };
            lifetime.ThrowIfCancellationRequested();
            live = new LiveSpeechClient(async (text,final) => {
                if (lifetime.IsCancellationRequested) return;
                InterimTranscript = final ? "" : text;
                if (final) { LastTranscript = text; SpeechSegments++; }
                try {
                    await draft.TranscriptAsync(text,final,lifetime);
                    LastError = ""; Report("Dictating into chat. Send after 1 second of quiet.");
                } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception ex) { LastError = ex.Message; Report("Recognizing audio; " + ex.Message); }
            }, message => { if (lifetime.IsCancellationRequested) return; LastError = message; Report(message); Stop(); Stopped?.Invoke(); });
            using var connectToken = CancellationTokenSource.CreateLinkedTokenSource(lifetime,startToken);
            await live.ConnectAsync(settings.Key,connectToken.Token);
            startToken.ThrowIfCancellationRequested();
            lifetime.ThrowIfCancellationRequested();
            Start(settings,outputDeviceId);
            Report("Live dictation connected. Listening to system playback.");
        } catch { Stop(); throw; }
    }
    private void ProcessPcmFrame(byte[] pcm)
    {
        if (!DictationMode) { segmenter.Push(pcm); return; }
        if (live == null) return;
        playbackSilence.Push(pcm);
        Buffer.BlockCopy(pcm,0,liveBuffer,liveOffset,pcm.Length); liveOffset += pcm.Length;
        if (liveOffset == liveBuffer.Length) {
            if (!live.QueuePcm((byte[])liveBuffer.Clone())) {
                LastError = "Live audio could not keep up. Restart System audio; no stale audio will be replayed.";
                Report(LastError);
                var failedSession = live;
                if (Interlocked.Exchange(ref liveOverflow,1)==0)
                    ThreadPool.QueueUserWorkItem(_ => { if (ReferenceEquals(live,failedSession)) { Stop(); Stopped?.Invoke(); } });
                return;
            }
            liveOffset = 0;
        }
    }
    public string OutputDeviceName { get; private set; } = "";
    public long CapturedBytes { get; private set; }
    public double PeakLevel { get; private set; }
    public double MaximumPeak { get; private set; }
    public int SpeechStarts { get; private set; }
    public int SpeechSegments { get; private set; }
    public string CaptureFormat { get; private set; } = "";
    private readonly SpeechSegmenter segmenter = new() { MaximumSeconds = 1, PauseFrames = 50 };
    private readonly SpeechTranscriber transcriber;
    public VoiceService(HttpMessageHandler? handler = null) {
        transcriber = new SpeechTranscriber(handler);
        playbackSilence.Speech += () => draft?.SpeechResumed();
        playbackSilence.OneSecondQuiet += () => {
            var session = live; var currentDraft = draft; var token = liveLifetime?.Token ?? CancellationToken.None;
            if (session == null || currentDraft == null) return;
            if (liveOffset > 0) {
                session.QueuePcm(liveBuffer.AsSpan(0,liveOffset).ToArray()); liveOffset=0;
            }
            session.QueueUtteranceEnd();
            _ = FlushQuiet(currentDraft,token);
        };
    }
    private async Task FlushQuiet(DictationDraft currentDraft,CancellationToken token) {
        try { await currentDraft.SilenceAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { LastError=ex.Message; Report("Recognizing audio; "+ex.Message); }
    }
    public string LastTranscript { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public string CurrentStage { get; private set; } = "Off";
    public DateTime StageSince { get; private set; } = DateTime.UtcNow;
    private void Report(string message) { if(CurrentStage==message)return; CurrentStage = message; StageSince = DateTime.UtcNow; Notice?.Invoke(message); }
    private CancellationTokenSource? cancellation;
    private sealed record Chunk(byte[] Pcm, Task<Func<string, CancellationToken, Task>> Target);
    private Channel<Chunk>? queue;
    private Task? worker;
    private System.Threading.Timer? silenceTimer;
    private readonly byte[] frame = new byte[640]; private int frameOffset;
    private double resamplePosition; private double sampleSum; private int sampleCount;
    private DateTime lastPacket; private bool active;
    private readonly object audioGate = new();
    private Task<Func<string, CancellationToken, Task>>? pendingTarget;
    public event Action<string>? Notice;
    public event Action? Stopped;
    public Func<Task<Func<string, CancellationToken, Task>>>? BindTarget;
    public bool Active => active;
    public void SetPause(double seconds) => segmenter.PauseFrames = Math.Clamp((int)(seconds * 50), 15, 150);
    public void Start(ApiSettings settings, string? outputDeviceId = null)
    {
        if (active) return;
        cancellation = new CancellationTokenSource(); var token = cancellation.Token;
        queue = Channel.CreateBounded<Chunk>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        var currentQueue = queue;
        segmenter.Reset(); frameOffset = 0; resamplePosition = sampleSum = 0; sampleCount = 0;
        liveOffset = 0; playbackSilence.Reset();
        if (!DictationMode) segmenter.Segment += Enqueue;
        if (!DictationMode) segmenter.SpeechStarted += SpeechStarted;
        try
        {
            using (var devices = new MMDeviceEnumerator()) {
                playbackDevice = string.IsNullOrWhiteSpace(outputDeviceId)
                    ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : devices.GetDevice(outputDeviceId);
            }
            if (playbackDevice.DataFlow != DataFlow.Render || playbackDevice.State != DeviceState.Active)
                throw new InvalidOperationException("Select an active playback device in Voice setup. Microphone devices are not supported.");
            OutputDeviceName = playbackDevice.FriendlyName;
            CapturedBytes = 0; PeakLevel = MaximumPeak = 0; SpeechStarts = SpeechSegments = 0;
            // Loopback only. Keep the endpoint's native mix format and convert received
            // playback samples to the segmenter's 16 kHz mono format below.
            capture = new WasapiLoopbackCapture(playbackDevice);
            CaptureFormat = capture.WaveFormat.ToString();
            capture.DataAvailable += DataAvailable;
            var recording = capture;
            capture.RecordingStopped += (_, e) => { if (active && ReferenceEquals(capture, recording)) { Notice?.Invoke("Audio device stopped. " + e.Exception?.Message + " Switch Voice off and on."); Stop(); Stopped?.Invoke(); } };
            active = true; lastPacket = DateTime.UtcNow;
            if (!DictationMode) worker = ProcessQueue(settings, currentQueue.Reader, token);
            capture.StartRecording();
            DateTime warning = DateTime.MinValue;
            silenceTimer = new System.Threading.Timer(_ => {
                lock (audioGate) {
                    if (!active || !ReferenceEquals(capture, recording)) return;
                    // WASAPI can stop emitting packets when playback stops. Feed the
                    // segmenter's clock so the last sentence still gets flushed.
                    if (DateTime.UtcNow - lastPacket > TimeSpan.FromMilliseconds(60)) { PeakLevel = 0; ProcessPcmFrame(SilentFrame); }
                    if (DateTime.UtcNow - lastPacket > TimeSpan.FromSeconds(6) && DateTime.UtcNow - warning > TimeSpan.FromSeconds(6)) { warning = DateTime.UtcNow; Notice?.Invoke("No playback packets. Play computer audio; if the output device changed, restart Voice."); }
                }
            }, null, 20, 20);
            LastError = ""; Report("Listening to system audio: " + OutputDeviceName);
        }
        catch { Stop(); throw; }
    }
    private void Enqueue(byte[] pcm)
    {
        SpeechSegments++;
        var target = pendingTarget ?? Task.FromException<Func<string, CancellationToken, Task>>(new InvalidOperationException("No selected chat input."));
        if (queue?.Writer.TryWrite(new Chunk(pcm, target)) != true) Notice?.Invoke("Voice queue is full. Audio was not submitted; check your network.");
        pendingTarget = null;
    }
    private void SpeechStarted()
    {
        SpeechStarts++;
        pendingTarget = BindTarget?.Invoke() ?? Task.FromException<Func<string, CancellationToken, Task>>(new InvalidOperationException("No selected chat input."));
        _ = pendingTarget.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    private void DataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (audioGate) {
        if (!active || capture == null || !ReferenceEquals(sender, capture)) return;
        lastPacket = DateTime.UtcNow; CapturedBytes += e.BytesRecorded; double peak = 0;
        var format = capture.WaveFormat; int stride = format.BlockAlign, sampleBytes = format.BitsPerSample / 8;
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == FloatSubFormat);
        for (int offset = 0; offset + stride <= e.BytesRecorded; offset += stride)
        {
            double mono = 0;
            for (int channel = 0; channel < format.Channels; channel++)
            {
                int i = offset + channel * sampleBytes;
                mono += floating && sampleBytes == 4 ? BitConverter.ToSingle(e.Buffer, i) : sampleBytes switch {
                    2 => BitConverter.ToInt16(e.Buffer, i) / 32768d,
                    3 => ((e.Buffer[i] | e.Buffer[i + 1] << 8 | e.Buffer[i + 2] << 16) << 8 >> 8) / 8388608d,
                    4 => BitConverter.ToInt32(e.Buffer, i) / 2147483648d,
                    _ => 0
                };
            }
            mono /= format.Channels; peak = Math.Max(peak, Math.Abs(mono)); sampleSum += mono; sampleCount++;
            resamplePosition += 16000d;
            if (resamplePosition >= format.SampleRate)
            {
                resamplePosition -= format.SampleRate;
                short sample = (short)Math.Clamp(sampleSum / sampleCount * 32767, short.MinValue, short.MaxValue);
                sampleSum = 0; sampleCount = 0;
                frame[frameOffset++] = (byte)sample; frame[frameOffset++] = (byte)(sample >> 8);
                if (frameOffset == frame.Length) { ProcessPcmFrame(DictationMode ? frame : (byte[])frame.Clone()); frameOffset = 0; }
            }
        }
        PeakLevel = peak; MaximumPeak = Math.Max(MaximumPeak, peak);
        }
    }
    private async Task ProcessQueue(ApiSettings settings, ChannelReader<Chunk> reader, CancellationToken token)
    {
        try
        {
            await foreach (var chunk in reader.ReadAllAsync(token))
            {
                try
                {
                    // Capture the destination before the network request. Navigation or a
                    // tab switch invalidates the closure; no late transcript reaches another chat.
                    var submit = await chunk.Target;
                    token.ThrowIfCancellationRequested();
                    string text = await transcriber.TranscribeWav(SpeechSegmenter.Wave(chunk.Pcm), settings, token, Report);
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(text)) { Report("No spoken words recognized. Listening for more system audio."); continue; }
                    LastTranscript = text;
                    Report("Transcript ready. Sending to the selected chat…");
                    await submit(text, token);
                    LastError = "";
                    Report("Transcript submitted. Listening to system audio.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { if (!token.IsCancellationRequested) { LastError = ex.Message; Report(ex.Message); } }
            }
        }
        catch (OperationCanceledException) { }
    }
    private void StopCapture()
    {
        active = false; silenceTimer?.Dispose(); silenceTimer = null;
        var old = capture; capture = null;
        if (old != null) { old.DataAvailable -= DataAvailable; try { old.StopRecording(); } catch { } old.Dispose(); }
        playbackDevice?.Dispose(); playbackDevice = null; PeakLevel = 0;
        lock (audioGate) { segmenter.Segment -= Enqueue; segmenter.SpeechStarted -= SpeechStarted; segmenter.Reset(); pendingTarget = null; }
    }
    public async Task StopAsync()
    {
        var session = live;
        if (session == null) { Stop(); return; }
        StopCapture();
        if (liveOffset > 0) {
            var remaining = new byte[liveOffset]; Buffer.BlockCopy(liveBuffer,0,remaining,0,liveOffset);
            session.QueuePcm(remaining); liveOffset = 0;
        }
        Report("Finishing live dictation…");
        await session.FinishAsync();
        if (ReferenceEquals(live,session)) Stop();
    }
    public void Stop()
    {
        StopCapture();
        cancellation?.Cancel(); queue?.Writer.TryComplete();
        liveLifetime?.Cancel(); live?.Dispose(); live = null;
    }
    public void Dispose() { Stop(); transcriber.Dispose(); cancellation?.Dispose(); }
}








