# Delta 2.1.1 — continuous System audio request

Changed:
- Individual device/cloud session failures no longer uncheck System audio.
- A controller keeps the user's ON request separate from the health of the current session.
- Failed startup, device loss and Live session closure trigger another connection attempt after five seconds while ON.
- Connection attempts accept cancellation so manual OFF does not leave background retry work.
- Quick OFF/ON transitions wait for the previous session to finish cleanup before starting the next.
- Cleanup errors are reported and do not permanently terminate the retry controller.
- Manual OFF stops new capture and retries, with bounded finalization of already captured text.
- Existing saved auto-submit preference is retained and applied at reconnect.

Verification:
- Windows x64 Release/self-contained publish succeeded.
- 134 core checks passed, including simulated startup failure/session loss while ON and no restart after manual cancellation.
- Local UI regression fixture passed draft insertion, fresh-editor lookup, cross-conversation protection, setup validation, click-through focus, resizing, transparency, screenshots, OCR text submission and downloads.
- No new live cloud session or actual hardware disconnection was induced for this controller-only update. The successful compiled Live transcription test from 2.1.0 remains the protocol evidence.

Limits:
The app cannot capture or transcribe during a real device/network/service outage. It now keeps the ON request and retries visibly instead of silently turning the switch OFF. Audio from an interrupted connection is not replayed, and speech during recovery may be missed. Closing Delta also ends listening.

Use:
Close old Delta, extract the full new ZIP into a new folder and run Delta.exe. Keep Live dictation selected in Voice setup, and enable Automatically send finalized phrases if desired. Turn System audio ON once. Turn it OFF when finished.

Unsigned Windows x64 preview requiring WebView2 Evergreen Runtime.
