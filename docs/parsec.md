# Parsec compatibility mode

This mode keeps Whisper, Qwen and the microphone on the **client PC**. Do not start a second copy on the remote computer. Only the client's small toolbar is used.

## Enable

1. Open Local Dictation Settings and check **Enable Parsec mode (experimental)**.
2. Choose the **Remote computer keyboard** layout: same as local, French AZERTY, or English US. It must match the layout used by the remote Windows session.
3. Save, click an ordinary text field in the Parsec session, and use **Ctrl+Shift+Alt+D**.
4. Speak, then pause briefly. A finished phrase is transcribed, cleaned (if enabled) and typed into Parsec. Press the shortcut again to flush the last phrase and stop.

This installation setting is opt-in for fresh installs. The application recognizes the Windows `parsecd` streaming window, not every unsupported application. Normal local fields keep their existing progressive transcription and guarded corrections.

## Differences from local dictation

Parsec exposes a video surface rather than the remote editor through Windows UI Automation. This app cannot read or verify the remote caret, selection, text value, password/read-only state, or whether the remote editor accepted input.

- **Finished phrases only:** words are not provisionally inserted and revised. Pause/silence or the segment limit finishes a phrase.
- **Cleanup before typing:** each phrase is cleaned locally before insertion. Already-sent phrases are never rewritten or backspaced. Raw-text restoration is unavailable for remote phrases.
- **Select and replace:** selection is handled by the remote editor when the first typed key arrives; it cannot be checked or guaranteed by the client app.
- **Manual input:** typing, clicking or changing focus detaches the current span. Click where dictation should resume. Changing the remote destination does not replay old phrases.
- **Enter:** still stops immediately and discards audio not yet inserted. Wait until the phrase appears, or finish with the shortcut before submitting.
- **Character support:** physical scan codes are used instead of Unicode packets or clipboard paste. Your chosen host keyboard must support the characters. A whole phrase is checked before typing; unsupported characters (such as emoji) refuse that phrase instead of sending only a prefix.
- **Remote transport:** models and audio stay on the client, but the resulting keyboard input travels through your existing Parsec connection. Network latency, keyboard layout, permissions and the remote editor can still affect insertion.

The Parsec-mode shortcut is handled locally, including repeats, and its `D` key is suppressed rather than forwarded to the remote computer. No remote helper, microphone passthrough, clipboard overwrite, new listening port or second model instance is installed. For Parsec-specific input routing, see [Configure Hotkeys](https://support.parsec.app/hc/en-us/articles/32381778420372-Configure-Hotkeys), including its Ignore Hotkey option, and [Immersive Mode](https://support.parsec.app/hc/en-us/articles/32361385571860-Immersive-Mode-Setting).

## Verification limits

Automated interactive tests use a dedicated local editor: scan-code insertion, punctuation/case, selection replacement, append-only behavior, unsupported-character preflight, interruption and single local shortcut handling. `--parsec-probe` inspects an actual Parsec window's metadata **without typing into it**. The complete client-to-host path still needs a manual test in a disposable remote text field; no test writes into a user's chat or submits a message.
