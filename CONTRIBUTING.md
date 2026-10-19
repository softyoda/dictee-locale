# Contributing

Issues and pull requests are welcome. Describe the Windows version, GPU, target editor and exact reproduction steps. Use a synthetic sentence; do not upload private dictations or recordings.

## Development

1. Clone this repository and run `setup.ps1` as described in the README.
2. Quit the app from its tray menu before rebuilding or running GPU tests.
3. Edit `src/WindowsApp` (WPF/.NET) or `backend` (Python).
4. Build:

```powershell
dotnet publish .\src\WindowsApp\WindowsApp.csproj -c Release -o .\app --self-contained false
```

Core behavior to preserve: offline inference, no recorded history, guarded text insertion, no synthetic Enter in production, no replay across destinations, and the existing Ctrl+Shift+Alt+D shortcut. English translations live in `src/WindowsApp/Strings.en.json`; settings persist only locally.

## Verification

```powershell
# Pure parsing/punctuation tests: no models or GPU needed.
python -m unittest discover -s tests -p test_text.py
# Dedicated interactive test windows, not a user's editor.
.\app\DicteeLocale.exe --self-test
.\app\DicteeLocale.exe --interaction-test
# Model checks, using synthetic fixtures; do not run alongside the app.
.\.venv\Scripts\python.exe .\tests\test_engine.py
# Leaves the app running, microphone inactive.
.\app\DicteeLocale.exe --smoke-test
```

Reports appear in ignored `test-results/`. A synthetic franglais test phrase is also present in Whisper's prompt; it is a regression fixture, not an independent accuracy benchmark. CPU mode and every third-party editor have not been validated. Interactive UI tests require a Windows desktop and exclusive focus.

## Commits from the installed checkout

The source checkout can also be the local installation. `app/`, `.venv/`, `runtime/`, `models/` and `settings.json` are ignored. Commit sources/documentation only:

```powershell
git status
git add src backend tests scripts setup.ps1 README.md
git commit -m "Describe the behavior changed"
git push origin main
```

For outside contributors, push to your fork and open a pull request. Maintainers can use `scripts/Build-Release.ps1 -Version 0.1.0` to build a self-contained Windows ZIP with SHA-256 checksums. Release archives contain binaries and installer files, not weights, environments or local settings. A tag matching `v*` runs the release workflow.


Parsec changes: `--interaction-test` also verifies scan-code selection replacement, punctuation/case, no provisional preview, unsupported-character preflight, no remote rewrites, interruption, and local shortcut suppression. With Parsec running, `--parsec-probe` verifies detection and opaque-window metadata without typing. Client-to-host transport needs a manual check in a disposable remote editor. Never use a user's conversation as the automated test target.


Use `app/DicteeLocale.exe --text-tests` for pure C# document reconciliation checks. This mode creates no windows, installs no hooks, loads no models and skips the instance mutex, so it can run alongside the app without taking focus. It verifies rich-editor NBSP/paragraph-sentinel acknowledgement, continuation, revisions and edit guards. Interactive UI tests require explicit authorization on the maintainer's working desktop.
