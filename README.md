# ScrollOS (SOS)

> A PowerShell-powered terminal operating environment where graphical applications live in scrollback history.

ScrollOS reimagines the terminal as a complete operating environment. Applications render directly inside the terminal, support mouse input, graphics, and audio, and leave behind persistent artifacts in a scrollable timeline.

Instead of switching between windows, users navigate through history. Applications can be revisited, resumed, and interacted with long after they have been closed.

Core ideas:

- Terminal-first computing
- Graphical applications inside the terminal
- PowerShell as the application platform
- History as the desktop
- Persistent and resumable application state
- Cross-platform (Windows, Web, Raspberry Pi)

## Getting started (Windows)

Requires the .NET 10 SDK. Run it in Windows Terminal (it needs truecolor and mouse support).

```powershell
dotnet build
dotnet run --project src/ScrollOS.Core
```

Try the proof of concept:

1. Type `notes`, add a few notes (type + Enter), then press **Esc** to close the app.
2. Scroll up (mouse wheel or PgUp). The closed app is still in the timeline.
3. Quit with **Ctrl+Q** (or `exit`), start ScrollOS again, and the history is still there.
4. Click **[ Resume ]** on the artifact (or double-click it, or run `Resume-App <id>`), and the notes come back.

Background apps: type `timer`, enter `0.5 tea` to start a 30-second countdown, then press **Ctrl+Z**. The timer
keeps running in the background (still visible, dimmed, in its timeline entry). When it ends, a notification
appears in the timeline; click it to bring the timer back at the bottom.

Other keys: **Tab** switches between the prompt and the live app, and **PgUp/PgDn** scroll.
Useful commands: `Get-Timeline`, `Get-ScrollApp`, `Start-ScrollApp <name>`.
Data lives in `%USERPROFILE%\.scrollos` (override with `--home <dir>`). The host log is in `logs\host.log`.

### Layout

| Path | What it is |
| --- | --- |
| `src/ScrollOS.Core` | `scrollos.exe`: terminal, rendering, input, timeline, app lifecycle (no PowerShell, starts fast) |
| `src/ScrollOS.Host` | `scrollos-host.exe`: hosts PowerShell; one runspace per app, plus the shell |
| `src/ScrollOS.Protocol` | JSON-lines messages and the widget tree shared by both |
| `src/ScrollOS.Sdk` | PowerShell module: widget builders (`New-Panel`, `New-List`, …) and shell commands |
| `apps/` | Apps, one folder per app (`apps/NotesPS/NotesPS.psm1`, `apps/TimerPS/TimerPS.psm1`) |
| `tests/` | Unit tests, plus an end-to-end test that runs the full launch → close → resume flow |

Writing an app: see the contract at the top of [ScrollOS.Sdk.psm1](src/ScrollOS.Sdk/ScrollOS.Sdk.psm1) and the
[NotesPS](apps/NotesPS/NotesPS.psm1) example.
