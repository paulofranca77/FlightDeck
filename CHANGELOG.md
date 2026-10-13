# Changelog

FlightDeck was built over a few days in October 2026 through a conversation between a flight-sim pilot
(who tested every build on his own setup) and Claude, the AI that wrote the code. These are the steps it went
through. Only v2.0 is published as a download; the earlier builds were private test versions.

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
