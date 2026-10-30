# Delta 2.1.7 - navigation and keyboard routing

Back/Forward follow this Delta tab's history; Refresh reloads it. Keyboard ON focuses Delta. OFF returns focus to your last outside app when Windows allows it; otherwise click the other app once. Alt+K restores Delta input. ON does not block typing in another app after you switch to it. Existing UI, login and capture protection are retained.

# Delta 2.1.6 — responsiveness update

Preserves the browser UI, required account login, one-second quiet auto-send and capture protection.

Changes:
- Installs the chat bridge once per document instead of resending all helper functions for each call. Missing bridges recover automatically.
- Finds normal chat inputs directly; scans shadow trees only when needed.
- Avoids duplicate initialization and event handlers on rapid repeated tab selection.
- Stops the audio status timer when System audio is OFF; uses a slower meter timer while hidden.
- Avoids redundant repeated status notifications.
- Reuses live PCM input and silence buffers while retaining required queue copies.
- Encodes screenshot PNGs on a worker after capturing pixels on the UI thread.

These reduce repeated local work. AI service response time and website/network loading are outside Delta's control. The script microbenchmark is not a measurement of overall app speed. No background tabs are forcibly suspended, so ongoing conversations and playback keep their behavior.

Close the previous Delta before installing or opening this version. Existing account and voice settings are retained.

# Delta 2.1.5 — account sign-in

The existing Delta 2.1.4 browser layout and features are retained. A dark username/password window appears first. Sign in with your existing delta.acranix.com account before using the browser. The sidebar adds Sign out.

Setup: close the old Delta, extract this complete ZIP to a new folder and run Delta.exe. Internet is required for sign-in and ongoing session validation. No account password or session token is saved. The installation identity is encrypted for the current Windows user in the existing Delta Native settings folder.

Accounts use the current website service: active admins and trials follow their existing policies; monthly accounts send an installation identity and may require device activation by your administrator. Paused/expired/revoked sessions are rejected. Sessions are checked every 60 seconds; validation failure closes Delta and returns to login. Network requests time out after 20 seconds. No offline account access is provided.

The website source was recovered from your connected Sites account; the published website and its access policy were not changed. The application connects only to https://delta.acranix.com for account operations and does not follow credential redirects. Passwords and session tokens are not sent to AI chat pages.

Production builds disable diagnostic command-line modes that bypass normal sign-in. For developer testing only, build with -p:DeltaDiagnostics=true. Account checks are client access controls, not hardware attestation or tamper-proof licensing.

# Delta 2.1.4 — capture protection

Capture protection starts automatically when Delta's window is created. The sidebar shows Capture protection: ON, or FAILED with error details. Protection stays enabled during and after Delta screenshots, hide/show, resizing and normal use. Delta's voice setup, crop selection and Insert text windows also request protection.

Uses Windows WDA_EXCLUDEFROMCAPTURE. Requires Windows 10 version 2004 or newer. Supported Windows capture paths omit Delta while it remains visible locally; capture programs may instead show blank content or reject capture of the protected window. This is not a guarantee against every recorder, capture driver, remote-desktop implementation or camera. Windows-owned dialogs, browser-owned popup windows, tray menus and notifications are outside the custom-window coverage. Confirm the result using your actual sharing/recording application before relying on it.

Close the previous Delta, extract the complete ZIP to a new folder, and run Delta.exe. Existing voice setup and other settings remain available.

# Delta 2.1.3 — chat delivery recovery

Fixes a draft ownership bug: rich editors can accept insertText while returning false, or normalize spaces/paragraphs. Delta now verifies the inserted text instead of trusting the command return value, and records the actual accepted draft. Editor replacement is rebound safely; brief unavailable-editor errors retry automatically. An unsuccessful final delivery is retried at the next silence boundary. Actual manual text edits continue to block replacement and automatic sending to protect your work.

Use this version by closing the previous Delta, extracting the complete Windows ZIP to a new folder and running its Delta.exe. Existing voice setup is retained. Keep System audio ON while dictating into the selected chat.

# System audio update — 2.1.2

Turn System audio ON with the destination chat selected. Live partial words appear directly in its input. After one second of quiet playback, the finalized transcript sends automatically. Continued speech keeps the draft open. Listening stays ON between messages and reconnects after interruptions until manually turned OFF.

Voice details and the sidebar transcript were removed. Voice setup retains the optional automatic-send checkbox; enable "Send after 1 second of silence". The one-second quiet interval does not guarantee one-second network transcription or delivery. Background playback noise can delay silence detection. If you edit the dictated draft yourself, automatic replacement and sending pause to protect your edit; turn System audio off and on to start again. Open a different chat only after turning System audio off.

# Delta Native for Windows - 2.1.2 preview

Windows x64 build from the existing Delta Native C# / WPF / WebView2 source.

## Click-through mode (new in 2.0.5)

1. Open and configure the chat inside Delta, including opacity, size and screenshot mode.
2. Click Chrome or your other working app.
3. Press Alt+P while that app is focused. Delta stays visible, but clicks go to the app beneath.
4. Press Alt+P again to restore normal Delta controls, then click Delta if you want to edit it.

The toolbar shows Click-through ON/OFF. The tray menu also has Toggle click-through and Edit Delta. Restore window size exits click-through. The mode always starts OFF on a new launch. If Alt+P is unavailable, use the tray menu to recover.

While ON, you cannot click, type, drag or resize Delta directly. Alt+M shows/hides the overlay without requesting activation; global opacity shortcuts and full screenshots remain available. Region crop and OCR selection require exiting click-through first. Voice can continue with its selected delivery mode; authenticated website behavior still needs user verification.

This mode avoids asking Windows to activate Delta during passive use. It does not hide browser focus events, change Chrome or a website, or guarantee that activity is undetectable. Ordinary interaction with Delta can cause a detectable focus change. Chrome must remain focused for its page to retain focus.

## Continuous System audio (2.1.1)

System audio stays requested ON until you manually switch it OFF or close Delta. An individual Google or playback session ending no longer unchecks the switch. While ON, Delta reports the interruption and retries connection/device startup every five seconds. It rereads saved Voice setup on each attempt, and retains the auto-send preference.

OFF cancels pending connection attempts and future retries, stops new capture and permits the existing short finalization of already captured text. No old audio is replayed after a reconnect. Speech during a disconnection can be missed; the ON switch represents your request to listen, and Reconnecting indicates that capture/transcription is temporarily unavailable.

## Live system-audio dictation (2.1.0)

This release adds a continuous Google Live transcription connection using your existing Google AI Studio key.

1. Open Voice setup and select your key file and playback device.
2. Select Live dictation â€” streaming.
3. Enable Send after 1 second of silence for automatic submission (the default).
4. Save setup, open the destination chat, select its input, and turn System audio ON.
5. Play speech through the selected output. Live words appear directly in the selected chat input, with partial words replaced as recognition improves.
6. Keep System audio ON. After one second of quiet, finalized words send automatically; turn OFF when finished.

Disable Send after 1 second of silence if you want to review a draft manually. The screenshot auto-submit toggle is independent of this choice.

Live mode streams PCM in 100 ms audio packets. That packet size is not a promise of 100 ms transcript latency. In the compiled-app test, first text arrived 670 ms after streaming audio started; the finalized generated phrase matched the spoken sample. Your speed depends on network conditions, speech and Google's service.

The chat input holds the current draft. Conversation changes block delivery into the wrong chat. Live mode uses the dedicated gemini-3.5-transcribe-live model, requires access to it, and reports disconnections rather than silently replaying stale audio. Select Request mode manually if Live access is unavailable.

Capture remains playback-only; a physical microphone is not opened. The connection streams system playback only while you enable it. Turning OFF stops new capture immediately and allows a short bounded finalization period for already captured audio. No recordings are saved.

## One-second request-mode batches and transcription feedback (2.0.9)

System audio now flushes continuous speech in one-second batches instead of waiting up to twelve seconds. The default silence wait remains one second. This is capture batching time, not a guarantee of one-second cloud transcription. Short chunks can cut across words and increase request count; if the cloud cannot keep up, the existing bounded queue can fill.

Transient Google HTTP 408/500/502/503/504 failures and connection timeouts receive at most three attempts. Each attempt has a 20-second deadline. Credential and quota errors are reported without repeated retries. A stage label shows attempts and elapsed time.

The status line reports delivery problems. Delta does not save transcripts to disk.

When a transcript is ready, Delta finds the fresh chat input while preserving the original tab, navigation and conversation URL. Switching conversations cancels automatic delivery rather than sending into another chat.

## Google authorization-key compatibility (2.0.8)

The validator now accepts Google's newer authorization keys containing a period, including AQ. keys, as well as legacy keys. Older builds rejected this format before making a network request. Keep your existing key file and reselect it in Voice setup if needed.

## Key-file validation fix (2.0.7)

Invalid key-file contents now show guidance inside Voice setup and leave the window open; they no longer escape into a Delta error popup. This release also recognizes copied smart quotes, invisible formatting marks and UTF-16 plain-text files saved without a BOM. Labels such as API key: are supported. No key contents are displayed or logged.

## Voice setup (new in 2.0.6)

Click **Voice setup**, select your existing Google AI Studio key file, choose the speakers/headphones currently playing audio, then save. Turn **System audio** ON. The meter shows incoming playback level.

The saved setting contains only the file location and playback-device ID, not the credential. The key file is read again when System audio is enabled. You no longer have to move the file beside Delta.exe. API.txt beside the actual executable and API.txt.txt are still discovered automatically. Supported file contents: one key, a quoted key, GEMINI_API_KEY=..., GOOGLE_API_KEY=..., API_KEY=..., or JSON containing key/api_key/apiKey and an optional model.

Capture uses WASAPI render-loopback only. Microphone devices are not listed or opened. Choose the same output device used by the app playing the sound. If you switch headphones or the default output device, switch System audio OFF, update Voice setup, and switch it ON again.

Voice transcribes playback into the selected AI chat using Google's API. It does not create a virtual microphone for websites. No key is included; a valid Google AI Studio credential and model access are required for live transcription.

## Changes in 2.0.6

- Added a key-file picker, persisted selected-file location, clearer errors and hidden-extension handling.
- Added playback-device selection and a system-audio level meter.
- Capture retains the output device's native mix format and converts samples into the speech format locally.

- Resize from all four edges and corners. Removed the old large minimum dimensions; the technical minimum is 1 x 1 logical pixel. Very small windows clip their controls. Right-click the Delta tray icon and choose Restore window size to return to 1000 x 620.

- Fixed whole-window opacity using WebView2CompositionControl and a transparency-enabled WPF window. Browser content now participates in window compositing.

- Visible top-toolbar Screenshot Auto-Submit ON/OFF switch, with space for its full label.
- ON attaches screenshots and submits after attachment readiness; OFF attaches only.
- The setting persists. Switching OFF revokes an in-flight automatic send.
- OCR text auto-submits independently of this switch.
- Repeated captures use unique attachment names and reject overlapping capture jobs.
- Existing shortcuts and capture exclusion remain in place; capture does not hide/show Delta.
- Voice ignores delayed stop notifications from a previous recording session.
- Browser downloads save to Desktop / Delta Downloads, with numbered names for existing files and footer completion/failure messages.

## Verification for this release

See Verification.md for current results. 134 core checks passed. The real Windows
loopback test captured playback, formed a speech segment, and made one mock
transcription request/submission. No live API call was made and live API credentials,
website accounts, speech accuracy and physical shortcut presses remain unverified.
Local UI regression checks cover opacity, resizing, screenshots, OCR text submission,
click-through focus, and downloads.

## Run

Extract the whole Windows ZIP and run `Delta.exe`. Keep all DLLs and the
`Assets` directory beside it. The .NET runtime is included. Microsoft Edge
WebView2 Evergreen Runtime must be installed (normally present on Windows 11).
Runtime installation information: https://developer.microsoft.com/microsoft-edge/webview2/

The app has no taskbar button. Use **Alt+M** or the tray icon to show or hide
it; use the tray menu or the top-right close button to quit. Do not delete or
move companion files while the app is running.

## Voice

Use Voice setup to select your Google AI Studio key file, or put it in `API.txt` **beside Delta.exe**. A plain
single-line key works; UTF-8 and BOM-marked UTF-16 files are supported. Files
accidentally named `API.txt.txt` are also recognized. No key is bundled.
Settings are re-read each time System audio is switched on.

Optional JSON format:

```json
{"key":"YOUR_GOOGLE_AI_STUDIO_KEY","model":"gemini-3.8-flash"}
```

Open the destination chat and click its input, play computer audio, then
enable Voice. **WASAPI loopback captures computer playback, not a microphone.**
Playback samples are converted locally to 16 kHz mono PCM. Speech uses a short preroll,
an adjustable silence interval, a 12-second segment limit, a bounded queue,
and cancellation when stopped. Each segment binds to its original chat before
transcription; a tab change or navigation prevents delayed auto-submission.
Playback and audio are sent to Google only while Voice is on. No recordings
are saved. Silence/no-packet handling also flushes a final spoken sentence.
Changing the default output device requires switching Voice off and on.

The model is configurable because availability, quotas, cost, and accuracy vary.
This preview does not establish transcription accuracy or latency against a
real speech corpus. It uses the existing Google provider rather than creating
or provisioning a new paid account.

## Controls

| Control | Behavior |
|---|---|
| Browser tabs | ChatGPT, Gemini, Claude, Google Meet, Grok; + adds a tab; right-click closes |
| Address bar | URL or Google search; back, forward, reload |
| Copy / Paste / Enter | Selected page text, clipboard text into chat, invoke chat Send |
| System audio | Stream playback dictation into the selected chat draft; optional auto-send in Voice setup |
| Auto-submit | Screenshots ON: upload and send; OFF: upload only to the active chat |
| Screenshot | Virtual desktop screenshot; upload and follow the Auto-submit switch |
| sscrop | Select a region; upload and follow the same Auto-submit switch |
| OCR | Select a region, recognize using installed Windows OCR languages, submit text |
| Insert / Start / Pause / Reset | Load text, type into the external editor, pause, reset position |
| Speed | External typing rate |
| Pause | Speech silence interval before segmentation |
| Opacity | Window opacity; applies to the whole composed window, including browser content |
| Keyboard | Toggle ordinary keyboard input in the browser |
| Hide Cursor | Hide pointer styling inside website documents |
| Resize | Toggle window size; all edges and corners can be dragged |

The screenshot **Auto-submit** switch is in the top toolbar. It defaults
to ON and is remembered between launches. It affects both Screenshot and sscrop.
Every new screenshot uses the current switch setting. Switching OFF before Send
cancels that capture's automatic send; turning it back ON does not revive a
pending capture. Upload-only attachments stay in the chat draft for manual Send.
**OCR text always auto-submits**, independently of the screenshot switch.

| Shortcut | Action | Scope |
|---|---|---|
| Alt+M | Show / hide Delta | Global |
| Alt+B | Full screenshot | Global |
| Alt+Shift+B | Cropped screenshot | Global |
| Alt+X | OCR region and submit text | Global |
| Alt+K | Keyboard toggle | Global |
| Alt+, | Insert clipboard into external editor | Global |
| Alt+Left / Alt+Right | Reduce / increase opacity | Global |
| Insert | Show / hide Delta | Delta focused |
| Ctrl+T / Ctrl+W | New / close tab | Delta focused |
| Ctrl+L / Ctrl+R | Address bar / reload | Delta focused |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab | Delta focused |
| Ctrl++ / Ctrl+= / Ctrl+Numpad+ | Zoom in | Delta focused |
| Ctrl+- / Ctrl+Numpad- | Zoom out | Delta focused |
| Ctrl+0 | Reset zoom | Delta focused |
| Ctrl+, | Insert clipboard into external editor | Delta focused |
| Ctrl+/ | List shortcuts and registration errors | Delta focused |
| F11 | Toggle fullscreen | Delta focused |
| Esc | Cancel typing / crop selection | Delta focused |

Focused shortcuts register only while Delta is active, including when the
embedded website has focus. Clipboard, undo, and editing shortcuts pass through
to the website normally. Close older Delta versions before testing global keys.

Screenshots use Windows capture exclusion for Delta rather than hiding and
re-showing the app. A region-selection overlay displays a frozen capture.
Chat attachments require a supported page input; auto-submit waits for new
attachment previews and enabled Send controls. Website changes can break these
adapters. Failures appear in the footer, with text/attachments left for manual Send.

## Known preview limitations

- This is not a feature-parity or production certification. Mac support is absent.
- Embedded-browser sign-in can be restricted by some identity providers.
- Account login, live provider transcription, real audio accuracy, physical
  multimonitor screenshots, website upload completion, external typing, meeting
  permissions, keyboard interception, and opacity need hands-on Windows verification.
- The UI follows the reference layout but has not been certified pixel-identical.
- The previous version's profile selector and some other advanced
  commands are not all migrated; do not delete the previous app/data yet.
- No drivers, process hiding, security bypass switches, or sandbox disabling.
- WebView2 runtime is a prerequisite and is not bundled in this preview.

## Build

Install the .NET 10 SDK. In this directory:

```powershell
dotnet restore Delta.Native.csproj --locked-mode
dotnet publish Delta.Native.csproj -c Release -r win-x64 --self-contained true -o publish
```

Pinned package versions and hashes are in `packages.lock.json`.
The `Assets/chat-bridge.js` file reuses the pure DOM target, attachment, and
submission adapters from the existing Delta codebase; it has no Node/Electron
dependency or privileged host bridge. Do not add host objects to untrusted sites.

## Diagnostics

```powershell
.\Delta.exe --self-test --data-dir C:\path\to\writable-tests
.\Delta.exe --smoke-test --data-dir C:\path\to\writable-tests
.\Delta.exe --audio-smoke --data-dir C:\path\to\writable-tests
.\Delta.exe --ocr-smoke --data-dir C:\path\to\writable-tests
```

The first writes `self-test.json` for API-file encodings/validation and speech
segmentation/WAV tests without an API request or audio capture. The second opens
the native window, attempts WebView2 startup, writes `ui-smoke.json` and an internal
UI rendering after 15 seconds, then exits. Use a folder you own. Normal application
data and cookies are stored in `%LOCALAPPDATA%\Delta Native`. Error summaries are
written to `startup.log`; API keys and recordings are not logged.

The audio smoke test plays a synthetic tone through Windows, captures it
with the actual Voice service, and injects a mock provider response. It writes
`audio-smoke.json`. It makes no live API call and does not measure real speech
accuracy. Close test windows before rebuilding their executable folder.
The OCR smoke test recognizes an in-memory synthetic image locally using Windows OCR.











