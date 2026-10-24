# Changelog

FlightDeck was built over a few days in October 2026 through a conversation between a flight-sim pilot
(who tested every build on his own setup) and Claude, the AI that wrote the code. These are the steps it went
through. Only v2.0 is published as a download; the earlier builds were private test versions.

## v2.4.0
- **START column** (from a user's feedback): choose when each app opens.
  - **Right away** — as before.
  - **After N seconds** — e.g. open your pre-flight tools first and the simulator itself 2 minutes later.
  - **Sim ready** — once the simulator is ready for add-ons (SimConnect), during its main loading.
  - **Flight loaded** — once you're in the cockpit. FlightDeck reads only the camera state through a small
    built-in SimConnect connection (no SimConnect.dll needed) and disconnects when it's done. It never
    changes anything in the simulator.
- The old **WAIT** column is now called **PLACE AFTER**: seconds to wait after an app's window appears before
  moving it into place. It never delayed opening the app, which the old name suggested.
- Tiles show what's still coming ("Starts in 40 s", "Waiting for flight"), and a notification tells you when
  they open.
- **Code clean-up** after many quick iterations: unused code removed, duplicated helpers merged (one place
  to find an app's processes, which also accepts names written with ".exe"), the engine split into smaller
  files, and the Dashboard's Position button no longer freezes the window while it opens an app from the tray.
  If the brand font can't load, the FlightDeck title now falls back to a system font instead of disappearing.

## v2.3.0
- **Safer End Flight.** Apps that go to the tray instead of closing are now asked to exit the way Windows
  does at shutdown, so they save and quit cleanly (DynamicLOD gets to restore your LODs). Forcing an app
  closed is only a last resort, and FlightDeck never does it while the simulator is open, because killing
  an add-on connected to the sim could crash it (a real MSFS 2024 crash report led to this). Close timeout
  is now 20 s by default, and FlightDeck never closes the simulator itself, even if it's in your app list.
- **Wait for SimConnect** (suggested by a user): an option per app to open it only once the simulator is
  ready for add-ons, during its main loading. It's a SIMCONNECT toggle in the Apps list. FlightDeck goes to
  the tray, waits, then opens and places those apps and re-applies the window order.
- **Minimize to tray** (Settings, on by default): minimizing puts FlightDeck next to the clock instead of
  the taskbar. One click on the tray icon brings it back.
- **First-run guide**: when a profile has no apps yet, the Dashboard and Apps page show what to do, step
  by step, with the buttons right there.

## v2.2.3
- Progress bar shows the moment you click Start Flight / End Flight / Re-apply layout, and climbs as each
  app opens (opening now counts for a bigger share of the bar).
- The window no longer stalls while apps launch: the "running" check for the tiles now happens in the background.

## v2.2.2
- Smoother progress bar: it climbs with every action (opening each app, window found, each placement pass,
  closing each app) instead of jumping per finished app, and keeps drifting gently while waiting.

## v2.2.1 / v2.2
- Choose your own accent colour (Settings → Accent colour): 8 presets or any colour with the picker.
  Buttons, highlights, logo, window and tray icons and the desktop shortcuts all follow it;
  text on and in the accent colour adjusts automatically to stay readable in Dark and Light.
- Settings page scrolls on small windows / high DPI.
- Shortcuts pinned to Start and to the taskbar also switch to the new colour (v2.2.1).

## v2.1
- Progress bar under "Activity" during Start Flight, End Flight and Apply layout
  ("3 of 7 apps ready", "Setting the window order…", turns orange if an app needs attention).

## v2.0 — first public release
- Landing animation ("Welcome back!") after End Flight when every app closed.
- "In tray" status while End Flight waits for apps that hide in the tray instead of quitting.
- Start/End Flight desktop shortcuts get their own takeoff and landing icons.

## v1.9
- Apps that start hidden in the tray are opened automatically: FlightDeck sends them a tray "click"
  (double-click, then clicking the taskbar icon as fallbacks).
- FlightDeck stays on top while Start Flight runs, then shows "All set! Have a great flight" before going to the tray.

## v1.8
- Detects apps parked in the system tray and reveals their hidden window.

## v1.7
- Apps that run as administrator: FlightDeck offers to restart as administrator, and after a one-time
  Windows approval starts elevated silently (scheduled task).
- Apps that set themselves "always on top" no longer break the stacking order.
- Rank badges in the Apps list.

## v1.6
- Position button on each Dashboard tile; apps already open are moved by Start Flight.
- Placement keeps watching each app for up to 30 s (splash/login screens, apps that move themselves).
- Activity log file and "View log & diagnostics" with one-click copy.

## v1.5
- "Grab position" link in the Apps list with a 3-step guide; own branded notifications.
- Layout map labels that never overlap; Dashboard tiles always fit (they shrink as you add apps).

## v1.4
- Drag & drop an .exe or shortcut onto the window to add an app; "No position yet" warning.
- Cal Sans wordmark with the tagline "Your add-ons in one click".

## v1.3
- Green icon everywhere, takeoff/landing icons on the Start/End Flight buttons, real app icons in the lists.

## v1.2
- Stacking order applied as the very last step, after the slowest app has settled.

## v1.1
- Drag to set the stacking order; WAIT column per app; gaps between snapped windows closed.
- Accent color #9FCC2E and a Light theme; tray menu with Open / Start or End Flight / Exit.

## v1.0
- Single app (Windows Forms) replacing the first PowerShell script: Dashboard, Apps, Layout and Settings pages.
- Start Flight / End Flight, Capture windows, profiles, system-tray mode.

## v0.1 — the idea
- A PowerShell script that opened a list of programs and moved their windows to saved positions.
