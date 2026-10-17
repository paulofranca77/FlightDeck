<p align="center"><img src="assets/icon.png" width="96" alt="FlightDeck icon"></p>

<h1 align="center">FlightDeck</h1>
<p align="center"><b>One-click launcher & window layout for your flight sim add-ons.</b><br>
Open, arrange and close all your companion apps — every window back in its exact spot, every flight.</p>

---

## Made entirely with AI — please read

**FlightDeck was written 100% by an AI assistant** ([Claude](https://claude.ai), by Anthropic).
I'm a flight sim enthusiast, not a programmer: I described what I wanted, tested every version on my own
setup, reported what went wrong, and Claude wrote all of the code. I didn't write a single line myself.

I'm publishing the source so anyone can see exactly what it does. It has been tested on my PC and my
add-ons, so your mileage may vary — issues and suggestions are very welcome.

## What it does

Every flight starts the same way: open Navigraph, your ATC app, your flight tracker, your VA client, a couple
of tools… then drag every window back to where you like it. FlightDeck does that with one click, and closes
everything again when you land. It works with any Windows program, not just sim add-ons.

- **Start Flight** — opens every app that isn't running, waits for each window and puts it back in its exact saved position and size (apps already open are moved too).
- **End Flight** — closes your add-ons. Apps that hide in the system tray are asked to exit the way Windows does at shutdown, so they save and quit cleanly. Forcing an app closed is only a last resort, and never happens while the simulator is open (that can crash the sim).
- **Wait for SimConnect** — per app: open it only once the simulator is ready for add-ons (during its main loading), not right away.
- **Pixel-exact layouts** — works with Windows 11 snap layouts: snapped and resized windows come back edge to edge.
- **Stacking order** — drag the apps in the list to choose which window stays in front; applied after the slowest app has settled.
- **Step-by-step guide** on first run and for new profiles, right where your apps will appear.
- **Capture windows**, **drag & drop** an .exe or shortcut, **Grab position**, per-app **Wait**, and a **Position** button for apps reopened mid-flight.
- **Tray-aware** — apps that start minimized to the tray are brought out automatically.
- **Administrator apps** — supported; FlightDeck asks Windows once, then starts elevated without prompting again.
- **Profiles**, layout map of your monitors, Dark/Light themes with your own accent colour, system-tray mode (minimize to tray), Start/End Flight desktop shortcuts, built-in log & diagnostics.

## Download

Get the latest `FlightDeck-vX.Y.zip` from **[Releases](../../releases)** — it's built automatically by GitHub
Actions from the code in this repository. Extract it to a permanent folder and run `FlightDeck.exe`.
No installer, nothing else needed on Windows 10 (1803+) or Windows 11.

### First steps

1. Open your add-ons and arrange their windows the way you like (snap them, resize them).
2. In FlightDeck: **Apps → Capture windows**, tick your add-ons, **Add selected**.
3. Drag the rows to set the stacking order (top = in front); raise **Wait** for slow starters.
4. Apps that run as administrator: turn on **Admin** in their row.
5. Click **Start Flight**. When you land: tray icon → **End Flight**.

## Transparency & privacy

- **No internet access, no telemetry, no account, no updater.** FlightDeck never connects to anything.
- Settings and logs are plain files in `%AppData%\FlightDeck` (`config.json`, `activity.log`, `crash.log`).
- No installer; it doesn't touch the registry or your simulator's folders.
- It only acts on the apps you add: starts them, moves/resizes their windows, closes them on End Flight.
- With **Run FlightDeck as administrator** on, it creates one Windows scheduled task, *FlightDeck (administrator)*, so it can start elevated without a prompt. Turning the option off removes it.
- For apps that start hidden in the tray it sends them the same "click" message Windows sends when you click their tray icon.

Because it isn't code-signed and it starts, moves and closes other programs, SmartScreen or an antivirus may warn
about it. The source is all here — and every release is built from it by GitHub Actions.

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer) on Windows:

```
dotnet build FlightDeck.csproj -c Release -o out
```

or open `FlightDeck.csproj` in Visual Studio 2022. The app targets .NET Framework 4.7.2 (built into Windows),
uses only Windows Forms and the Win32 API, and has no third-party runtime dependencies.

| Folder / file | What's inside |
| --- | --- |
| `src/Engine.cs` | Config, profiles, Start/End Flight logic |
| `src/Native.cs` | Win32 calls: finding, placing, stacking and revealing windows |
| `src/Tray.cs` | Finding and clicking tray icons (UI Automation) |
| `src/Elevation.cs` | Run-as-administrator via a scheduled task |
| `src/MainForm.cs`, `Pages.cs`, `Views.cs`, `Dialogs.cs`, `Controls.cs` | The user interface |
| `src/Diagnostics.cs` | Activity log and the diagnostics report |

## Troubleshooting

Open **Settings → View log & diagnostics → Copy all** and paste it in a [new issue](../../issues/new).
It lists your monitors, each app's windows and what FlightDeck did with them.

## License & credits

- Code: [MIT](LICENSE).
- The FlightDeck wordmark uses [Cal Sans](https://github.com/calcom/font) © The Cal Sans Project Authors,
  under the [SIL Open Font License 1.1](assets/CalSans-OFL.txt).
- Written by Claude (Anthropic), directed and tested by Paulo França.
