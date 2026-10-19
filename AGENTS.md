# Agent instructions

## Purpose

Maintain a Windows dictation app with progressive insertion, French/English code-switching, and local cleanup. The public repository is `softyoda/dictee-locale`; the maintainer's active installation/checkout is `C:\Tools\tts_local`. Keep generated tools, models and environments under the installation root. Do not commit any of them.

The user's shortcut is **Ctrl+Shift+Alt+D** (Ctrl+Maj+Alt+D in French, modifiers 7, key 68). Do not reintroduce former F-key shortcuts. README and contributor documentation are English; the application interface supports English and French.

## Work safely

- Read README and CONTRIBUTING before changes. `git status` and `git diff` describe what can be committed. The folder is now a repository with a public remote; do not assume publish authorization for future turns unless requested.
- No remote inference, telemetry, audio/transcript history, clipboard insertion or production Enter simulation.
- Refuse passwords/read-only fields. Selected text may be replaced when the user selects it. Only revise the owned dictation span; manual edits detach it.
- Invalidate the destination/generation immediately on mouse press. Capture after release + 200 ms; do not clear new audio after capture. Late inference must not replay text into a new destination. Cancellation must reach HTTP calls.
- UI Automation work runs outside the toolbar thread with bounded waits. Recheck Owned/focus/modifiers before SendInput. A timed-out task must never inject later. Wait for editor acknowledgement before adopting its new baseline.
- Enter without Shift stops, hides and skips cleanup while passing the original key through once. Cleanup stop uses the existing shortcut. Opening settings also stops listening but preserves displayed text and opens a normal usable window.
- Preserve the small non-activating toolbar, hidden at idle, no separate transcript/copy control, near the active field. English strings live in Strings.en.json. Settings language changes must apply and persist without restart.
- Startup is opt-in, only the dedicated HKCU Run value. Quit releases only this app's resources/process tree. Keep the custom microphone icon.
- Invalid/empty/destructive cleanup JSON preserves raw text. Long cleanup uses guarded local edits. Franglais and meaningful negations must be preserved.
- Do not claim accuracy on a user's voice, all editors or CPU performance from synthetic/GPU tests.

## Build and verify

Quit the running app before rebuilding or GPU tests. Use `dotnet publish src/WindowsApp/WindowsApp.csproj -c Release -o app --self-contained false`. Release builder produces a separate self-contained win-x64 archive. Never delete or redownload weights for ordinary UI edits.

Run appropriate checks from CONTRIBUTING. UI tests use dedicated editors with exclusive Windows focus; they must not insert into a user's chat. Smoke tests leave the app running with the microphone off. Do not load two copies of the models on GPU. Test reports, screenshots outside docs, local settings and environments are ignored.

Use UTF-8 for PowerShell file reads/writes. Avoid non-ASCII literal text piped through Python stdin in Windows PowerShell; write a UTF-8 file or use Unicode escapes. Check resolved targets before any recursive cleanup; do not clean the installation root.


## Approved Parsec mode

The user explicitly requested a client-side Parsec mode: keep models/microphone here, no remote installation/mic or second popup. In this opt-in mode only, bypass local UIA field checks for a positively identified parsecd/MTY_Window streaming window. The remote field/caret/selection/password state cannot be inspected; do not claim those protections apply remotely. Never add an unguarded fallback for arbitrary unsupported apps.

Only finalized phrases are transcribed/cleaned, then appended via physical keyboard scan codes matching the selected remote layout. Never backspace or rewrite remote text, paste via clipboard, restore raw remote text, inject Enter or resend a partially typed phrase. Validate every character/scan code before sending any input; check Owned/foreground between characters and always release simulated modifiers. Stop immediately when detached; partial insertion cannot be rolled back. Keep cleanup before insertion, and generation/token guards after every await. Enter discards pending audio. Local editor behavior is unchanged.

Native keyboard hook handles Ctrl+Shift+Alt+D locally for Parsec, suppresses D down/up and repeats, and marks its own scan-code events to avoid mistaking dictated input for a shortcut. Tests use an owned editor; the passive Parsec probe never injects. Document that real client-to-host delivery remains a manual verification. Fresh installations default to disabled; the maintainer explicitly enabled this mode locally.


## User requirement: work without disturbing the desktop

The user explicitly requires repairs to continue in the background while they work. Do not launch interactive self-tests, interaction tests, smoke-test previews or any other window that takes focus unless the user explicitly authorizes a desktop test. Do not type into their active application. Prefer `--text-tests` (pure reconciliation tests, no window/hooks/models/mutex) and Python pure tests, with builds in ignored staging folders. Only replace/restart the running app when its toolbar is hidden (idle). Keep the corrected app hidden at startup. Full Slack verification remains a manual user check; do not claim it was automated in their chat.
