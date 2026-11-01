# Delta for macOS — development preview 0.1.0

Native SwiftUI/AppKit + WKWebView foundation, separate from the Windows app. Requires macOS 14 or newer and the Xcode command-line tools. No third-party Swift packages or embedded API keys.

## Implemented source

- Account login against the existing Delta service, active-session validation, periodic revalidation, and logout. Passwords/session tokens stay in memory; the installation identity uses Keychain. The server receives `client=desktop`, matching Windows; actual Mac account/device approval needs live testing.
- Persistent WKWebView website sessions, five default tabs, add/select/close, URL/search navigation, back/forward/refresh, popup-to-tab routing, web file selection, and user-chosen download destinations.
- In-page Copy, Paste, Enter and typing controls. Start/Pause/Reset/manual character steps share a Unicode-aware cursor. Default automatic speed is 10%. Tab/navigation identity guards delayed insertion; editing the target inside the same webpage is not yet guarded.
- Normal floating window/opacity controls and optional click-through, recoverable from a visible menu-bar item. Standard Dock presence is retained. Menu shortcuts are app-local.
- Permission-based primary-display screenshot export and Vision OCR, with a text preview and confirmation before in-page insertion. Capture may include Delta. No automatic OCR submission.
- Pure XCTest cases for typing state, Unicode, line endings, speed limits, and URL handling.

## Build on a Mac

```sh
cd macos/Delta
swift test
bash scripts/build-app.sh
open "dist/Delta Browser.app"
```

Open `Package.swift` in Xcode for debugging. Run the `.app` for stable bundle identity, Keychain, and permission testing. The build script puts the editor bridge in `Contents/Resources` and verifies it with `--verify-resources`. SwiftPM's resource bundle is used when running from the package. Builds use an ad-hoc signature by default; do not treat that as Developer ID signing/notarization or a public installer. Provide `DELTA_SIGNING_IDENTITY` only through your own secure signing environment. No Gatekeeper bypass instructions are provided.

## Permissions

Screen Recording is requested only when Screenshot/OCR is used. On denial, enable Delta in System Settings → Privacy & Security → Screen Recording, then reopen it. Site camera/microphone prompts are WebKit/OS controlled. No Accessibility or Input Monitoring access is requested because external typing/global interception are not implemented.

## Not yet Windows parity

- External-editor typing, global shortcuts and held-key/manual-step queuing.
- Region selection, automatic screenshot attachment/attachment readiness, and screenshot auto-submit mode.
- System-playback capture, transcription, voice preferences, and reconnect handling.
- Site-specific/shadow-DOM editor integration; some sites reject synthetic Enter or require separate OAuth/browser handling.
- Cursor overlay, website zoom controls, capture-privacy equivalents, universal binaries, signed/notarized PKG/DMG, updater, and full visual parity.

These are not hidden behind functioning-looking placeholder controls. The UI labels the typing destination as the current webpage and lists unavailable features. No process disguises, exam-browser workarounds, protected-content capture, or security-policy bypasses are included.

## Validation status

Created on Windows, which cannot compile AppKit/WebKit/ScreenCaptureKit. GitHub macOS CI is the compilation/test gate. Do not describe this preview as tested or ready for end users until CI and the checklist below pass on a real Mac.

- Launch the app bundle; verify resources load.
- Valid/invalid login, device approval, expired account, unavailable service, logout and revalidation.
- Website sign-in, default tabs, navigation, tab switching, popup login, file upload, download.
- Selected-text Copy; textarea/contenteditable Paste and Enter; typing cursor, reset during pending insertion, navigation cancellation.
- Screenshot permission allow/deny; screenshot PNG export; OCR preview cancel/insert; destination changed during OCR.
- Opacity/float/click-through recovery through menu bar; normal Quit and closing/reopening.
- Confirm privacy prompts and Keychain identity survive a signed relaunch. Test Apple silicon and Intel separately before claiming either architecture.
