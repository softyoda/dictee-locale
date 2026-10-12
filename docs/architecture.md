# Architecture

`src/WindowsApp` is a .NET 8 WPF/NAudio application. `backend/server.py` is a private loopback inference server using faster-whisper/CTranslate2 and Transformers/PyTorch. Downloads are isolated to setup; normal inference sets offline/telemetry-disabled environment flags.

Audio is mono PCM16 at 16 kHz. The timer redecodes approximately every 750 ms, ends segments after 850 ms silence or 20 seconds, and retains a half-second silence preroll. A semaphore serializes UI inference and a server lock serializes Whisper/Qwen GPU work.

`Native.cs` installs keyboard/mouse hooks on a dedicated STA message thread. `TextTarget` tracks focus, value and caret using UI Automation. Provider calls run off the toolbar thread with bounded waits; the insertion task must check ownership/focus before SendInput. Only the owned dictation suffix, or a newly captured selection, may be replaced. Empty recognition never deletes an initial selection. Windows double-click boundary spaces are retained.

A mouse press detaches the current destination immediately and invalidates its generation. Mouse release captures a new field after 200 ms; superseded clicks and late inference results cannot replay old text. Private HTTP requests take a cancellation token. Enter without Shift passes through once and hides/stops without cleanup or late insertion.

The non-activating toolbar uses native caret/field bounds for placement, preferring above the field and clamping to the monitor work area. Settings is a separate normal window. Opening it stops listening while keeping displayed text. `Ui.cs` and the embedded `Strings.en.json` supply English/French interface text. Recognition stays multilingual.

Cleanup up to 500 characters generates guarded JSON text. Longer inputs ask Qwen for bounded unique/nonoverlapping local edits. Invalid responses keep the raw text. Spoken question-mark commands are converted before/after cleanup. No dictated instruction is meant to be executed by the cleaner.

`StartupRegistration.cs` modifies only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DicteeLocale`, when the user toggles startup. The default is off. Quit releases hooks, hotkey, microphone, tray resources and the app's own Python process tree. No system-wide uninstall or unrelated process termination is performed.

Installer/model/cache directories are private to the installation. NuGet and the source-build .NET SDK are standard developer dependencies. Downloaded model revisions are pinned; weights and synthetic test reports are excluded from source control.
