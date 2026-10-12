<div align="center">
  <img src="src/WindowsApp/Assets/dictee.png" width="88" alt="Local Dictation microphone icon">
  <h1>Local Dictation</h1>
  <p>Speak into any compatible Windows text field. Keep your voice on your computer.</p>
  <p><a href="https://github.com/softyoda/dictee-locale/releases/latest">Download for Windows</a> · <a href="#installation">Install</a> · <a href="#usage">Use</a> · <a href="CONTRIBUTING.md">Contribute</a></p>
</div>

A small Windows dictation toolbar with live text insertion, French/English code-switching, and local AI cleanup. Select a word, dictate its replacement, and keep speaking. Built with **Whisper large-v3-turbo** for transcription and **Qwen3-4B-Instruct-2507** for cleanup.

- Progressive dictation directly into the focused text field.
- French and English in the same sentence; no translation requested.
- Select one or more words to replace them with your next dictation.
- Remove fillers and apply explicit spoken corrections when you stop.
- English or French interface, with a working settings window.
- Optional startup at Windows sign-in and a tray menu to quit completely.
- Offline inference after installation. No cloud account or API key.

**Status:** early Windows x64 release. Tested on Windows 11 with an RTX 3090 (24 GB). Transcription accuracy and editor compatibility depend on your microphone, voice and application. This is a standalone app, not an official Paseo integration.

## Installation

### Recommended: Windows release

1. Download **`dictee-locale-0.1.0-windows-x64.zip`** from [Releases](https://github.com/softyoda/dictee-locale/releases/latest).
2. Extract the entire ZIP to a permanent, writable folder, for example `C:\Tools\dictee-locale`. Do not run it inside the ZIP.
3. Double-click **`Install.cmd`**. It installs private Python/dependencies and downloads both models. Keep the terminal open until it says **Ready**. A failed model download can be resumed by running it again.
4. Double-click **`Start.vbs`**. Look for the purple/turquoise microphone icon in the Windows notification area (possibly under the hidden-icons arrow). Models load before dictation becomes available.
5. Put the cursor in a text field and press **Ctrl+Shift+Alt+D**.

The release includes the .NET runtime; you do **not** need Git, Python or the .NET SDK. Installation needs Internet access; normal dictation does not. The installer uses official uv/PyTorch/Python downloads and Hugging Face model repositories. uv's bootstrap archive is checked against a pinned SHA-256.

The executable is not code-signed. Windows may warn when opening a downloaded release; verify the repository and release checksums before choosing to run it.

### Hardware

| Requirement | Notes |
| --- | --- |
| Windows 10/11 x64 | Windows 11 is the tested configuration. ARM64 is not supported by this release. |
| NVIDIA GPU and current driver | CUDA 12.8 PyTorch build. 16 GB+ VRAM recommended; measured/tested hardware has 24 GB. Smaller cards may run out of memory. |
| Free disk space | Allow roughly **25 GB** for models, Python, dependencies and download caches. |
| CPU fallback | Available, considerably slower and not validated on CPU here. Budget ample system RAM (32 GB recommended). |

For a CPU-only installation, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -Cpu -SkipBuild` from the extracted release folder. The same models are used.

### Build from source

Install Git and [.NET SDK 8 or later](https://dotnet.microsoft.com/download/dotnet/8.0), then:

```powershell
git clone https://github.com/softyoda/dictee-locale.git
cd dictee-locale
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1
.\Start.ps1
```

The installer bootstraps private Python and uv inside this folder. A source build requires the .NET 8 Desktop Runtime to run; the published release is self-contained. Close the running app before reinstalling or rebuilding. Keep the extracted installation in place: if you move it, rerun setup to recreate its Python environment and toggle startup off/on to update its path.

## Usage

| Action | Result |
| --- | --- |
| **Ctrl+Shift+Alt+D** | Start dictation; press again to finish and clean up. French keyboards display `Ctrl+Maj+Alt+D`. |
| **Enter** without Shift | Stop listening and hide the toolbar without cleanup. Your original Enter is passed to the editor; no late words are inserted. |
| Click another text field | Dictate at the new destination without replaying earlier text. |
| Double-click a word / select multiple words | Release the mouse, then speak to replace that selection. Surrounding text is preserved. |
| Settings gear | Opens settings, even during dictation: listening stops and the displayed text is kept. |
| **×** | Stop and hide the toolbar. The background app remains running. |
| Tray → **Quit completely** | Release the microphone, hotkey and models; exit the app. |

In **Settings**, select the interface language (**English** / **Français**), microphone and cleanup preference, then **Save**. New installations follow the Windows UI language (French or English); the setting persists locally. This does not lock the recognition language.

Right-click the microphone icon to enable/disable **Start with Windows**, open settings or retry registration of the hotkey. Startup is **off by default**. The keyboard shortcut only works while the app is running. You can bind that combination to a keyboard macro.

Manual typing or keyboard cursor changes pause insertion to protect your edits. Click where you want dictation to resume. Unsupported or unresponsive editors pause insertion; click a compatible field to recover without restarting. Password and read-only fields are refused. The toolbar never sends Enter on your behalf.

### Smart cleanup

Cleanup happens when stopping with the shortcut, not when submitting with Enter. It preserves meaning, technical names and mixed French/English, while removing hesitations and explicit false starts:

- `Euh je vais push la branche and then run the tests.` → `Je vais push la branche and then run the tests.`
- `On se retrouve mardi, ah non attends mercredi.` → `On se retrouve mercredi.`
- `Tu viens point d'interrogation.` → `Tu viens ?`

English `question mark` also becomes `?` during cleanup. Quoted punctuation names are preserved by the deterministic conversion; model cleanup remains heuristic. Short text uses full cleanup; longer text uses guarded local edits to avoid rewriting whole paragraphs. Invalid or overly destructive model responses keep the raw text. Cleanup is limited to 16,000 characters per session; beyond that, raw text is kept. For chapters or long documents, work in sections.

## Models and privacy

| Purpose | Local model | License |
| --- | --- | --- |
| Speech recognition | [dropbox-dash/faster-whisper-large-v3-turbo](https://huggingface.co/dropbox-dash/faster-whisper-large-v3-turbo), converted from [OpenAI Whisper large-v3-turbo](https://huggingface.co/openai/whisper-large-v3-turbo) | MIT |
| Text cleanup | [Qwen/Qwen3-4B-Instruct-2507](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507) | Apache 2.0 |

Exact model revisions are pinned in `backend/download_models.py`. They are downloaded on installation, **not committed to GitHub**. Both run locally in offline mode after setup. Recognition redecodes a rolling segment approximately every 750 ms; it is progressive transcription rather than a native word-by-word streaming model.

Audio and transcripts stay in memory. This app does not create dictation recordings or history, use the clipboard, or send inference to an external API. A private authenticated loopback server connects the Windows UI to Python. Hugging Face telemetry is disabled. Your destination app applies its own storage/network policy. Installer downloads naturally contact upstream services.

## Troubleshooting

- **No shortcut / no tray icon:** launch `Start.vbs`. Enable startup in the tray menu if desired. If another app owns the shortcut, its tray menu entry shows the problem and can retry.
- **Settings gear:** opens a separate window; if dictating, the app first stops listening while keeping existing text.
- **Models loading:** the first load can take a while. Installation downloads several gigabytes. Let it finish; retry installation after network failures.
- **CUDA / out-of-memory errors:** check driver and available VRAM; the GPU sizes above are recommendations, not a guarantee. CPU mode is much slower.
- **Editor blocks insertion:** some browser, Electron, terminal or code-editor fields expose incomplete Windows UI Automation data. Try another text field. An elevated destination app can block input from a non-elevated dictation app.
- **Recognition or cleanup mistakes:** select the text and dictate a replacement, restore the last raw dictation from the tray if still safe, or disable cleanup. No human-level accuracy is claimed.

See [development and verification](CONTRIBUTING.md), [architecture](docs/architecture.md), [third-party notices](THIRD-PARTY.md), and [security reporting](SECURITY.md).

## License

Application code: [MIT](LICENSE). Dependencies and downloaded models retain their own licenses. No user recordings, settings, model weights or private caches are included in this repository.
