using System.Threading;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Delta.Native;
internal static class SelfTest
{
    public static async System.Threading.Tasks.Task<int> Ocr(string directory)
    {
        Directory.CreateDirectory(directory);
        try {
            using var bitmap = new System.Drawing.Bitmap(900, 150); using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
                graphics.Clear(System.Drawing.Color.White); using var font = new System.Drawing.Font("Arial", 36); graphics.DrawString("DELTA OCR TEST 123", font, System.Drawing.Brushes.Black, 20, 35);
            }
            using var png = new MemoryStream(); bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            string text = await WindowsServices.Recognize(png.ToArray()); bool passed = text.Contains("DELTA", StringComparison.OrdinalIgnoreCase) && text.Contains("123");
            File.WriteAllText(Path.Combine(directory, "ocr-smoke.json"), JsonSerializer.Serialize(new { passed, text, engine = "Windows.Media.Ocr; synthetic image, no network" }));
            return passed ? 0 : 1;
        } catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "ocr-smoke.json"), JsonSerializer.Serialize(new { passed = false, error = ex.Message })); return 1; }
    }
    private sealed class MockTranscription : System.Net.Http.HttpMessageHandler
    {
        public int Requests;
        protected override async System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            string base64 = json.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("data").GetString()!;
            byte[] wav = Convert.FromBase64String(base64); if (Encoding.ASCII.GetString(wav, 0, 4) != "RIFF") throw new Exception("Invalid WAV payload");
            Requests++;
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"synthetic voice test\"}]}}]}") };
        }
    }
    public static async System.Threading.Tasks.Task<int> Audio(string directory)
    {
        Directory.CreateDirectory(directory); var handler = new MockTranscription(); int submissions = 0; string message = "";
        try {
            using var service = new VoiceService(handler); service.SetPause(.3); service.Notice += value => message = value;
            service.BindTarget = () => System.Threading.Tasks.Task.FromResult<Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task>>((text, token) => { if (text == "synthetic voice test") submissions++; return System.Threading.Tasks.Task.CompletedTask; });
            var pcm = new byte[16000 * 2 * 2];
            for (int i = 0; i < pcm.Length / 2; i++) { short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 16000) * 12000); pcm[i * 2] = (byte)sample; pcm[i * 2 + 1] = (byte)(sample >> 8); }
            using var stream = new NAudio.Wave.RawSourceWaveStream(new MemoryStream(pcm), new NAudio.Wave.WaveFormat(16000, 16, 1));
            using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var output = devices.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            using var playback = new NAudio.Wave.WasapiOut(output, NAudio.CoreAudioApi.AudioClientShareMode.Shared, false, 100);
            playback.Init(stream); service.Start(ApiSettings.Parse("fake_test_key_123456"), output.ID); playback.Play();
            await System.Threading.Tasks.Task.Delay(15000); service.Stop(); playback.Stop();
            File.WriteAllText(Path.Combine(directory, "audio-smoke.json"), JsonSerializer.Serialize(new { requests = handler.Requests, submissions, passed = handler.Requests > 0 && submissions > 0, capturedBytes = service.CapturedBytes, maxPeak = service.MaximumPeak, speechStarts = service.SpeechStarts, speechSegments = service.SpeechSegments, format = service.CaptureFormat, outputDevice = service.OutputDeviceName, source = "WASAPI render-loopback only", message, provider = "mock; no network request" }));
            return submissions > 0 ? 0 : 1;
        } catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "audio-smoke.json"), JsonSerializer.Serialize(new { passed = false, error = ex.Message })); return 1; }
    }
    public static int CheckKeyFile(string path, string directory)
    {
        Directory.CreateDirectory(directory);
        try {
            var settings = ApiSettings.LoadFile(path);
            File.WriteAllText(Path.Combine(directory,"key-file-check.json"), JsonSerializer.Serialize(new { parsed = true, networkRequestMade = false }));
            return 0;
        } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) {
            File.WriteAllText(Path.Combine(directory,"key-file-check.json"), JsonSerializer.Serialize(new { parsed = false, networkRequestMade = false, error = ex.Message }));
            return 1;
        }
    }
    private sealed class TranscriptionResponses(params int[] statuses) : System.Net.Http.HttpMessageHandler
    {
        public int Requests;
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token)
        {
            int code = statuses[Math.Min(Requests++,statuses.Length-1)];
            return System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)code) {
                Content = new System.Net.Http.StringContent("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":true,\"text\":\"omit\"},{\"text\":\"recognized test\"}]}}]}")
            });
        }
    }
    private static void CheckTranscription(Action<bool,string> check)
    {
        var settings = ApiSettings.Parse("fake_test_key_123456");
        var recover = new TranscriptionResponses(503,200);
        using (var client = new SpeechTranscriber(recover,(_,_)=>System.Threading.Tasks.Task.CompletedTask)) {
            string text = client.TranscribeWav(new byte[44],settings,System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            check(text=="recognized test" && recover.Requests==2,"Transient Google error retries and thought text excluded");
        }
        var unavailable = new TranscriptionResponses(503);
        using (var client = new SpeechTranscriber(unavailable,(_,_)=>System.Threading.Tasks.Task.CompletedTask)) {
            try { client.TranscribeWav(new byte[44],settings,System.Threading.CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("503 unexpectedly succeeded"); }
            catch (InvalidOperationException ex) { check(unavailable.Requests==3 && ex.Message.Contains("503"),"503 retries bounded at three"); }
        }
        var unauthorized = new TranscriptionResponses(403);
        using (var client = new SpeechTranscriber(unauthorized,(_,_)=>System.Threading.Tasks.Task.CompletedTask)) {
            try { client.TranscribeWav(new byte[44],settings,System.Threading.CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("403 unexpectedly succeeded"); }
            catch (InvalidOperationException ex) { check(unauthorized.Requests==1 && ex.Message.Contains("403"),"Credential errors not retried"); }
        }
    }
    public static async System.Threading.Tasks.Task<int> LiveNetwork(string keyPath, string wavPath, string directory)
    {
        Directory.CreateDirectory(directory);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long firstAudio = -1, firstText = -1, firstFinal = -1;
        var finals = new StringBuilder(); string failure = ""; bool connected = false;
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
        try {
            var settings = ApiSettings.LoadFile(keyPath);
            byte[] wav = File.ReadAllBytes(wavPath); byte[]? pcm = null;
            for (int offset=12; offset+8<=wav.Length;) {
                int length = BitConverter.ToInt32(wav,offset+4);
                if (length<0 || (long)offset+8+length>wav.Length) throw new InvalidDataException("Invalid test WAV.");
                if (Encoding.ASCII.GetString(wav,offset,4)=="data") { pcm = wav.AsSpan(offset+8,length).ToArray(); break; }
                offset += 8+length+(length&1);
            }
            if (pcm==null) throw new InvalidDataException("No PCM data in test WAV.");
            using var client = new LiveSpeechClient((text,final) => {
                if (firstText<0) firstText=clock.ElapsedMilliseconds;
                if (final) { if (firstFinal<0) firstFinal=clock.ElapsedMilliseconds; finals.Append(text); }
                return System.Threading.Tasks.Task.CompletedTask;
            },message => failure=message);
            await client.ConnectAsync(settings.Key,timeout.Token); connected=true;
            for (int offset=0;offset<pcm.Length;offset+=3200) {
                if (firstAudio<0) firstAudio=clock.ElapsedMilliseconds;
                if (!client.QueuePcm(pcm.AsSpan(offset,Math.Min(3200,pcm.Length-offset)).ToArray())) throw new InvalidOperationException("Live queue full.");
                await System.Threading.Tasks.Task.Delay(100,timeout.Token);
            }
            await client.FinishAsync();
            bool passed = connected && finals.ToString().Contains("train",StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(directory,"live-network.json"),JsonSerializer.Serialize(new { passed, connected, firstTextAfterAudioMs = firstText<0 ? -1 : firstText-firstAudio, firstFinalAfterAudioMs = firstFinal<0 ? -1 : firstFinal-firstAudio, transcript = finals.ToString(), failure, source = "generated spoken test phrase; no system audio captured" }));
            return passed ? 0 : 1;
        } catch (Exception) {
            File.WriteAllText(Path.Combine(directory,"live-network.json"),JsonSerializer.Serialize(new { passed=false, connected, failure = failure.Length>0 ? failure : "Live diagnostic connection or streaming failed.", source = "generated spoken test phrase" }));
            return 1;
        }
    }
    public static int Run(string directory)
    {
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
        void Reject(string value) { try { ApiSettings.Parse(value); } catch { count++; return; } throw new Exception("Invalid configuration accepted"); }
        try
        {
            Check(ApiSettings.Parse("\uFEFF  fake_test_key_123456  ").Key == "fake_test_key_123456", "BOM and whitespace");
            Check(ApiSettings.Parse("{\"key\":\"fake_test_key_123456\",\"model\":\"custom-model\"}").Model == "custom-model", "Model override");
            Check(ApiSettings.Parse("'fake_test_key_123456'").Key == "fake_test_key_123456", "Quoted key");
            Check(ApiSettings.Parse("GEMINI_API_KEY=\"fake_test_key_123456\"").Key == "fake_test_key_123456", "Named key assignment");
            Check(ApiSettings.Parse("{\"api_key\":\"fake_test_key_123456\"}").Key == "fake_test_key_123456", "JSON api_key alias");
            Check(ApiSettings.Parse("{\"apiKey\":\"fake_test_key_123456\"}").Key == "fake_test_key_123456", "JSON apiKey alias");
            Check(ApiSettings.Parse("\u200Efake_test_\u200Bkey_123456\u2060").Key == "fake_test_key_123456", "Invisible paste characters");
            Check(ApiSettings.Parse("\u201Cfake_test_key_123456\u201D").Key == "fake_test_key_123456", "Smart quotes");
            Check(ApiSettings.Parse("API key: fake_test_key_123456").Key == "fake_test_key_123456", "Human key label");
            string authKey = "AQ." + new string('x', 50);
            Check(ApiSettings.Parse(authKey).Key == authKey, "Google AQ-dot auth key");
            Check(ApiSettings.Parse("{\"key\":\"" + authKey + "\"}").Key == authKey, "JSON AQ-dot auth key");
            Check(ApiSettings.Parse("GEMINI_API_KEY=" + authKey).Key == authKey, "Assigned AQ-dot auth key");
            var mode = new ScreenshotSettings(); var autoMode = mode.Snapshot(); Check(mode.CanSubmit(autoMode), "Screenshot auto-submit defaults on");
            mode.Set(false); Check(!mode.CanSubmit(autoMode), "Switch off revokes pending send"); var uploadOnly = mode.Snapshot(); Check(!mode.CanSubmit(uploadOnly), "Upload-only never sends");
            mode.Set(true); Check(!mode.CanSubmit(uploadOnly), "Enabling later cannot send an upload-only capture"); Check(!mode.CanSubmit(autoMode), "Off-on cannot revive canceled send"); Check(mode.CanSubmit(mode.Snapshot()), "Next screenshot follows current mode");
            var ids = new System.Collections.Generic.HashSet<int>(); var chords = new System.Collections.Generic.HashSet<(uint,uint)>();
            foreach (var shortcut in ShortcutCatalog.All) { Check(ids.Add(shortcut.Id), "Unique shortcut id"); Check(chords.Add((shortcut.Modifiers,shortcut.Key)), "Unique shortcut chord"); Check(ShortcutCatalog.ActionFor(shortcut.Key, shortcut.Modifiers) == shortcut.Action, shortcut.Display); }
            Check(ShortcutCatalog.ActionFor(0x42,1)=="screenshot" && ShortcutCatalog.ActionFor(0x42,5)=="crop", "Full and cropped capture shortcuts");
            Check(ShortcutCatalog.ActionFor(0x58,1)=="ocr", "OCR shortcut");
            Check(ShortcutCatalog.ActionFor(0xBC,1)=="typing-insert" && ShortcutCatalog.ActionFor(0xBC,2)=="typing-insert", "Both clipboard shortcuts");
            Check(ShortcutCatalog.ActionFor(0x09,2)=="next-tab" && ShortcutCatalog.ActionFor(0x09,6)=="previous-tab", "Tab shortcuts");
            Reject("bad.key"); Reject("{\"key\":\"fake_test_key_123456\",\"endpoint\":\"http://generativelanguage.googleapis.com/a\"}");
            Reject("{\"key\":\"fake_test_key_123456\",\"endpoint\":\"https://evil.example/a\"}");
            Reject("{\"key\":\"fake_test_key_123456\",\"model\":\"../wrong\"}");
            var temp = Path.Combine(directory, "self-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try {
                mode.Set(false); mode.Save(temp); Check(!ScreenshotSettings.Load(temp).AutoSubmit,"Upload-only setting persists"); mode.Set(true); mode.Save(temp); Check(ScreenshotSettings.Load(temp).AutoSubmit,"Automatic setting persists");
                foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode }) { File.WriteAllText(Path.Combine(temp, "API.txt"), "fake_test_key_123456", encoding); Check(ApiSettings.Load(temp).Key == "fake_test_key_123456", "Key encoding"); }
                File.Move(Path.Combine(temp, "API.txt"), Path.Combine(temp, "API.txt.txt"));
                try { ApiSettings.Load(temp); throw new Exception("Duplicate extension not detected"); } catch (FileNotFoundException e) { Check(e.Message.Contains("API.txt.txt"), "Duplicate extension"); }
                string noBom = Path.Combine(temp, "no-bom-key.txt");
                foreach (var encoding in new Encoding[] { new UnicodeEncoding(false,false), new UnicodeEncoding(true,false) }) {
                    File.WriteAllText(noBom, "fake_test_key_123456", encoding);
                    Check(ApiSettings.LoadFile(noBom).Key == "fake_test_key_123456", "UTF-16 without BOM");
                }
                string aqFile = Path.Combine(temp, "aq-key.txt");
                File.WriteAllText(aqFile, authKey);
                Check(ApiSettings.LoadFile(aqFile).Key == authKey, "AQ-dot file round trip");
                string hiddenExtension = Path.Combine(temp, "API.txt.txt");
                Check(ApiSettings.LoadFile(hiddenExtension).Key == "fake_test_key_123456", "Explicit double-extension key file");
                var voicePrefs = new VoicePreferences();
                Check(voicePrefs.ResolveKeyFile(temp) == hiddenExtension, "Auto-resolve hidden extension");
                string selected = Path.Combine(temp, "selected-key.json");
                File.WriteAllText(selected, "{\"key\":\"fake_test_key_123456\"}");
                voicePrefs.KeyFile = selected; voicePrefs.OutputDeviceId = "test-output-only";
                voicePrefs.Save(temp);
                var reloaded = VoicePreferences.Load(temp);
                Check(reloaded.ResolveKeyFile(temp) == selected && reloaded.OutputDeviceId == "test-output-only", "Selected key path and output persist");
                Check(!File.ReadAllText(Path.Combine(temp,"voice-settings.json")).Contains("fake_test_key"), "Voice preferences never store key contents");
                reloaded.KeyFile = Path.Combine(temp, "moved-away.txt");
                Check(reloaded.ResolveKeyFile(temp) == hiddenExtension, "Stale selection falls back to beside-executable key");
                File.WriteAllText(selected, new string('a',16001));
                try { ApiSettings.LoadFile(selected); throw new Exception("Oversized key accepted"); } catch (InvalidDataException) { count++; }
            } finally { Directory.Delete(temp, true); }
            Check(LiveSpeechClient.Parse("{\"setupComplete\":{}}").Setup,"Live setup handshake parsing");
            var interim = LiveSpeechClient.Parse("{\"serverContent\":{\"interimInputTranscription\":{\"text\":\"partial words\"}}}");
            Check(interim.Interim=="partial words" && interim.Final=="","Interim preview never treated as final text");
            var final = LiveSpeechClient.Parse("{\"serverContent\":{\"inputTranscription\":{\"text\":\"final words\"},\"turnComplete\":true}}");
            Check(final.Final=="final words" && final.TurnComplete,"Final transcription parsing");
            Check(LiveSpeechClient.Parse("{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"text\":\"assistant output\"}]}}}").Final=="","Assistant responses never inserted as dictation");
            Check(new VoicePreferences().LiveDictation && new VoicePreferences().AutoSend,"Live dictation defaults to automatic send");
            {
                string shown=""; int sends=0;
                var draft = new DictationDraft((text,_)=>{shown=text;return Task.CompletedTask;},_=>{sends++;return Task.CompletedTask;});
                var ct=CancellationToken.None;
                draft.SpeechResumed();
                draft.TranscriptAsync("What is",false,ct).GetAwaiter().GetResult();
                Check(shown=="What is" && sends==0,"Partial dictation reaches composer immediately");
                draft.TranscriptAsync("What is your name?",false,ct).GetAwaiter().GetResult();
                Check(shown=="What is your name?","Partial hypotheses replace rather than duplicate");
                draft.SilenceAsync(ct).GetAwaiter().GetResult();
                Check(sends==0,"Silence waits for final transcription");
                draft.SpeechResumed();
                draft.TranscriptAsync("What is your name?",true,ct).GetAwaiter().GetResult();
                Check(sends==0,"Resumed speech cancels pending send");
                draft.SilenceAsync(ct).GetAwaiter().GetResult();
                draft.SilenceAsync(ct).GetAwaiter().GetResult();
                Check(sends==1,"Quiet finalized draft sends exactly once");
                draft.SpeechResumed();
                draft.TranscriptAsync("Second question.",true,ct).GetAwaiter().GetResult();
                draft.SilenceAsync(ct).GetAwaiter().GetResult();
                Check(sends==2 && shown=="Second question.","Continuous dictation starts a fresh message");
                int attempts=0; string recovered="";
                var retryDraft=new DictationDraft((text,_)=>{
                    if(++attempts==1) throw new InvalidOperationException("Temporary editor replacement");
                    recovered=text;return Task.CompletedTask;
                },_=>Task.CompletedTask);
                try { retryDraft.TranscriptAsync("Retained speech",true,ct).GetAwaiter().GetResult(); }
                catch(InvalidOperationException) { }
                retryDraft.SilenceAsync(ct).GetAwaiter().GetResult();
                Check(attempts==2 && recovered=="Retained speech","Temporary delivery failure retries retained transcript at silence");
                var detector = new PlaybackSilence(); int quiet=0;
                detector.OneSecondQuiet+=()=>quiet++;
                var speechFrame=new byte[640];for(int n=0;n<640;n+=2){speechFrame[n]=0; speechFrame[n+1]=16;}
                detector.Push(speechFrame);
                for(int n=0;n<49;n++)detector.Push(new byte[640]);
                Check(quiet==0,"No send before one second quiet");
                detector.Push(new byte[640]); detector.Push(new byte[640]);
                Check(quiet==1,"Exactly 50 quiet frames trigger once");
            }
            using (var requested = new System.Threading.CancellationTokenSource()) {
                int starts=0, stops=0, notices=0, polls=0;
                var continuous = new ContinuousVoiceRunner(_ => {
                    starts++;
                    if (starts==1) throw new InvalidOperationException("simulated outage");
                    if (starts==3) requested.Cancel();
                    return System.Threading.Tasks.Task.CompletedTask;
                },()=>starts==2 && polls++<2,()=>{stops++;return System.Threading.Tasks.Task.CompletedTask;},_=>notices++,
                (_,token)=>{token.ThrowIfCancellationRequested();return System.Threading.Tasks.Task.CompletedTask;});
                continuous.RunAsync(requested.Token).GetAwaiter().GetResult();
                Check(starts==3 && stops>=3 && notices>=2,"Voice remains requested ON across failures until manual cancellation");
                int before=starts;
                continuous.RunAsync(requested.Token).GetAwaiter().GetResult();
                Check(starts==before,"Voice never reconnects after manual OFF");
            }
            CheckTranscription(Check);
            var fastSegmenter = new SpeechSegmenter { MaximumSeconds = 1 }; int fastSegments = 0; fastSegmenter.Segment += _ => fastSegments++;
            var toneFrame = new byte[640]; for (int i = 1; i < toneFrame.Length; i += 2) toneFrame[i] = 8;
            for (int i = 0; i < 50; i++) fastSegmenter.Push(toneFrame);
            Check(fastSegments == 1,"Continuous voice chunk flushes after one second");
            var segmenter = new SpeechSegmenter { PauseFrames = 15 }; int segments = 0; byte[]? segment = null; segmenter.Segment += bytes => { segments++; segment = bytes; };
            var silence = new byte[640]; var sound = new byte[640]; for (int i = 0; i < sound.Length; i += 2) { sound[i] = 0; sound[i + 1] = 8; }
            for (int i = 0; i < 100; i++) segmenter.Push(silence); Check(segments == 0, "Silence ignored");
            for (int i = 0; i < 10; i++) segmenter.Push(sound); for (int i = 0; i < 15; i++) segmenter.Push(silence);
            Check(segments == 1, "Speech submitted once after pause");
            Check(segment!.Length >= 25 * 640, "Preroll retained");
            var wav = SpeechSegmenter.Wave(segment); Check(Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 24) == 16000 && BitConverter.ToInt16(wav, 22) == 1 && BitConverter.ToInt32(wav, 40) == segment.Length, "WAV format");
            segmenter.Push(sound); for (int i = 0; i < 20; i++) segmenter.Push(silence); Check(segments == 1, "Click/noise rejected");
            for (int i = 0; i < 10; i++) segmenter.Push(sound); segmenter.Reset(); for (int i = 0; i < 30; i++) segmenter.Push(silence); Check(segments == 1, "Stop discards pending speech");
            for (int i = 0; i < 620; i++) segmenter.Push(sound); Check(segments == 2, "Long speech bounded");
            count += Task.Run(()=>AccountClientTests.Run(directory)).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(directory, "self-test.json"), JsonSerializer.Serialize(new { passed = count, failed = 0 })); return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "self-test.json"), JsonSerializer.Serialize(new { passed = count, failed = 1, error = ex.Message })); return 1; }
    }
}










