# Delta Browser 2.1.11

Windows x64 WPF/WebView2 application, with the current Typing Master and Alt+M fixes. This repository contains source only. Release installers, runtime binaries, local credentials, and build caches are excluded.

## Typing Master

Copy plain-text code, click the destination editor, and use:

- **Alt+,**: enter one Unicode character/text unit and stop. Hold Alt and tap comma for successive characters; holding comma does not repeat.
- **Start**: automatic character entry from the same position. Default speed is 10%; the existing slider adjusts it during typing.
- **Pause**: stop and retain progress; cancel queued manual steps.
- **Reset**: clear the loaded typing buffer and progress without deleting existing editor text or clearing the user's clipboard.
- **Insert / Ctrl+,**: paste the full clipboard text.

The foreground destination is pinned for each run. Text is transferred through native clipboard paste to preserve whitespace and avoid key-driven indentation rules. Clipboard ownership changes stop the run and preserve the newer copy. Editors may still apply format-on-paste rules. LeetCode-specific behavior has not been tested.

## Build

Requirements: Windows 10/11 x64 and .NET 10 SDK. The installed app also needs Microsoft Edge WebView2 Runtime. Windows OCR requires an installed OCR language.

```powershell
dotnet restore src/Delta.Native/Delta.Native.csproj --locked-mode
dotnet build src/Delta.Native/Delta.Native.csproj -c Release --no-restore
./Build-Release.ps1
```

`Build-Release.ps1` publishes a self-contained app, creates the installer payload ZIP, and builds a single EXE under `outputs/Delta-2.1.11-Installer/`. Account authentication remains required; this repository contains no login credentials or service secrets. Voice key files are supplied locally by the user.

## Verification

```powershell
dotnet run --project verification/TypingMasterChecks/Checks.csproj -c Release
dotnet run --project verification/AltMToggleChecks/Checks.csproj -c Release
dotnet run --project verification/ShortcutChecks/Checks.csproj -c Release
dotnet run --project verification/WindowPolicyChecks/Checks.csproj -c Release
```

Typing checks include live synthetic editor interactions; run with an interactive Windows desktop. Clipboard/editor checks report a skip when Windows denies foreground focus. No credentials or real webpage submission are used. Build artifacts and test outputs should not be committed.

Earlier implementation/verification notes are retained alongside the app source. Capture exclusion is limited to supported Windows capture paths; non-activating windows and modifier handling cannot guarantee another application will never detect focus changes.
