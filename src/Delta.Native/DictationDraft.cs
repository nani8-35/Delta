using System;
using System.Threading;
using System.Threading.Tasks;

namespace Delta.Native;
public sealed class DictationDraft
{
    private readonly Func<string,CancellationToken,Task> update;
    private readonly Func<CancellationToken,Task> send;
    private readonly SemaphoreSlim gate = new(1,1);
    private string confirmed = "", interim = "";
    private volatile bool quiet;
    private bool deliveryPending;
    public bool AutoSend { get; set; } = true;
    public DictationDraft(Func<string,CancellationToken,Task> update, Func<CancellationToken,Task> send) { this.update=update; this.send=send; }
    public void SpeechResumed() => quiet=false;
    public async Task TranscriptAsync(string text, bool final, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await gate.WaitAsync(token);
        try {
            if (final) { confirmed = Join(confirmed,text); interim=""; } else interim=text;
            deliveryPending=true;
            await update(Join(confirmed,interim),token);
            deliveryPending=false;
            await SendIfReady(token);
        } finally { gate.Release(); }
    }
    public async Task SilenceAsync(CancellationToken token)
    {
        quiet=true;
        await gate.WaitAsync(token);
        try {
            if (deliveryPending) { await update(Join(confirmed,interim),token); deliveryPending=false; }
            await SendIfReady(token);
        } finally { gate.Release(); }
    }
    private async Task SendIfReady(CancellationToken token)
    {
        if (!AutoSend || !quiet || confirmed.Length==0 || interim.Length!=0) return;
        await send(token);
        confirmed=""; interim="";
    }
    private static string Join(string left,string right) => left.Length==0 ? right.Trim() : right.Length==0 ? left : left.TrimEnd()+" "+right.TrimStart();
}
public sealed class PlaybackSilence
{
    private bool speaking;
    private int quietFrames;
    public event Action? Speech;
    public event Action? OneSecondQuiet;
    public void Push(byte[] pcm)
    {
        double energy=0;
        for(int i=0;i+1<pcm.Length;i+=2) { double v=(short)(pcm[i]|pcm[i+1]<<8)/32768d; energy+=v*v; }
        bool voiced = Math.Sqrt(energy/Math.Max(1,pcm.Length/2))>=0.0015;
        if(voiced) { quietFrames=0; if(!speaking) {speaking=true; Speech?.Invoke();} }
        else if(speaking && ++quietFrames>=50) {speaking=false;quietFrames=0;OneSecondQuiet?.Invoke();}
    }
    public void Reset() {speaking=false;quietFrames=0;}
}
public sealed record DictationCallbacks(Func<string,CancellationToken,Task> Update, Func<CancellationToken,Task> Send);
