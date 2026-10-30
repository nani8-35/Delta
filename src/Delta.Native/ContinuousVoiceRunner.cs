using System;
using System.Threading;
using System.Threading.Tasks;

namespace Delta.Native;
// The user's requested state is independent of an individual device/cloud session.
public sealed class ContinuousVoiceRunner
{
    private readonly Func<CancellationToken,Task> start;
    private readonly Func<bool> running;
    private readonly Func<Task> stop;
    private readonly Action<string> status;
    private readonly Func<TimeSpan,CancellationToken,Task> delay;
    public ContinuousVoiceRunner(Func<CancellationToken,Task> start, Func<bool> running, Func<Task> stop, Action<string> status, Func<TimeSpan,CancellationToken,Task>? delay = null)
    {
        this.start=start; this.running=running; this.stop=stop; this.status=status;
        this.delay=delay ?? ((time,token)=>Task.Delay(time,token));
    }
    private async Task StopSafely()
    {
        try { await stop(); }
        catch (Exception ex) { status("System audio session cleanup reported: " + ex.Message); }
    }
    public async Task RunAsync(CancellationToken token)
    {
        try {
            string lastIssue = "";
            while (!token.IsCancellationRequested) {
                try {
                    await start(token);
                    while (!token.IsCancellationRequested && running())
                        await delay(TimeSpan.FromMilliseconds(400),token);
                } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { lastIssue = ex.Message; status("System audio remains ON. " + lastIssue); }
                if (token.IsCancellationRequested) break;
                await StopSafely();
                status("System audio ON — reconnecting in 5 seconds. " + lastIssue + " Switch OFF to stop.");
                await delay(TimeSpan.FromSeconds(5),token);
            }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { await StopSafely(); }
    }
}

