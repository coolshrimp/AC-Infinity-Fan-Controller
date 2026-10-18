# AC Infinity Desktop

A compact Windows Bluetooth widget for **AC Infinity Controller 67 (CTR67A)**, made by [Coolshrimp Modz](https://coolshrimpmodz.com/).

Keep fan controls, temperature, humidity, and calculated VPD on your desktop. Connect several Bluetooth controllers at once, each in its own widget with independent controls, schedules, and sensor history.

Unofficial software; not affiliated with or endorsed by AC Infinity.

## Supported controller

**AC Infinity Controller 67 (CTR67A)** — Bluetooth fan control and sensor readings.

<img src="screenshots/ac-infinity-fan-controller%28controller%2067%29.webp" alt="AC Infinity Controller 67 with its sensor probe" width="360">

Controller 67 is currently the only identified controller model supported for control.

## Screenshots

The Status, Chart, Sensor history, and Appearance views were captured from a live Controller 67 connection. Device addresses and private diagnostics are excluded.

| Status | Chart |
| --- | --- |
| ![Live readings, controller mode and fan controls](screenshots/status.png) | ![Compact sensor chart in the main widget](screenshots/chart.png) |

| Sensor history | Appearance |
| --- | --- |
| ![Separate local sensor history panel](screenshots/history.png) | ![Widget appearance settings](screenshots/appearance.png) |

Scheduling panels below show the default values before connecting.

| Controller schedule | This PC schedule |
| --- | --- |
| ![Save a daily schedule directly to the controller](screenshots/controller-schedule.png) | ![Run a daily window with repeating cycles from the tray](screenshots/pc-schedule.png) |

## Get started

1. Open the [Releases page](https://github.com/coolshrimp/AC-Infinity-Fan-Controller/releases) and download the Windows x64 release asset.
2. Run the downloaded application. The .NET runtime is bundled; no separate runtime installation is required.
3. Keep the powered controller nearby and close other apps holding its Bluetooth connection.
4. The widget scans and connects automatically when exactly one compatible controller is available. Click the device name or connection badge to choose a controller manually.

Requires Windows 10 version 2004 or newer, or Windows 11, x64, and a working Bluetooth adapter. The application is unsigned. Native runtime components extract automatically on first launch.

## Multiple controllers

In the Bluetooth panel, select **Add another controller...** to open another widget. Each widget owns its connection, fan slider, On/Off toggle, readings, chart, appearance, and tray icon. Controllers already assigned to a widget are excluded from other widgets' discovery. Opening the application again restores existing widgets instead of making duplicate connections.

Additional controllers with automatic connection enabled are remembered. On relaunch, the main widget appears first; additional widgets stay hidden while retrying and appear only after a verified connection. Offline controllers do not create empty duplicate windows. Closing one widget releases only that controller; the other widgets keep running. Give each controller a friendly name in Bluetooth settings to tell them apart.

The two fan sockets on a Controller 67 **share its programming**. Separate widgets represent separate Bluetooth controllers, not individual fans attached to one Controller 67. Temperature and humidity come from each controller's reported sensor data; extra sensor channels are not invented.

## Controls and views

- **Status:** the default view on launch, with temperature, humidity, calculated VPD, the active controller mode, fan controls, and access to **Fan schedules** through the **⋯** button.
- **Chart:** a compact history overview with 1h, 6h, and 24h ranges. **History ↗** opens a separate, larger panel with temperature, humidity, VPD, sample counts, and first/latest timestamps. Saved readings can be reviewed while disconnected.
- Release the speed slider at **1-10** to select manual On mode at that level. **0** selects the saved Off mode. The colored fill shows reported output while you choose a target.
- The **On/Off** toggle selects stored presets. Off can still spin the fan when its saved minimum is above zero.
- **Pin** keeps a widget and its panels on top. **Settings** changes colors, opacity, and temperature units. **Minus** hides that widget to its tray icon; **Close** quits that widget.
- **Fan schedules:** read and edit basic controller daily/cycle programs, or run a daily window with cycling from this PC. Stored Auto thresholds and timers remain available to view.

Live readings normally refresh every 10 seconds, switching to two-second reads while output changes. Requests are serialized per controller. Reconnecting reads settings and status. An enabled PC schedule then applies its current phase; a stopped schedule with pending restoration restores its previous controller mode and presets. Disconnect stops the PC schedule and restores its previous settings before releasing Bluetooth, then pauses automatic retries until a manual reconnect, scan, or restart.

## Fan schedules

Open **⋯** beside the fan speed, or **Fan schedules…** from that widget's tray menu. Each controller has its own schedule and recovery settings. Enter times as **HH:mm** using a 24-hour clock. Run speed is **1–10**, idle speed is **0–10**, and each cycle duration is **1–1,440 whole minutes**. An idle speed above zero keeps the fan running.

### Controller

Choose a **Daily schedule** or **Repeating cycle**. **Read settings** loads the controller's saved values. **Save & activate** immediately writes the selected program and its On/Off speed presets, activates its mode, and reads the settings back to check that they were retained. The saved mode continues running without this app or PC.

A daily schedule uses the **controller's own clock**; check that clock before saving because this app does not synchronize it. Start and end must differ, and overnight windows are allowed. A repeating cycle runs continuously between its run and idle speeds. Combining a daily window with a cycle is available through **This PC** below; this version does not write the controller's advanced automation settings.

### This PC

Set a daily window and a repeating run/idle cycle, then choose **Save & start**. Starting immediately applies the current phase and its run/idle presets, including idle outside the window. The app uses the run/idle cycle inside the window and the idle speed outside it. Overnight windows are allowed. Equal start and end times select all day; the cycle starts again at the daily start time. This schedule follows this PC's local clock.

Keep the PC awake, this app running, and Bluetooth connected. Minimizing to the tray keeps it running. The widget shows the current schedule state, and the tray menu offers **Stop PC schedule**.

**Stop & restore** returns the controller to the mode and On/Off presets it had before this PC schedule started. Using the app's manual slider or On/Off toggle first stops the PC schedule and restores those settings, then applies your manual change. Selecting a controller program also stops the PC schedule before saving that program.

Quitting normally restores the previous controller mode and presets while keeping the PC schedule enabled in local settings for the next launch. After reopening and reconnecting to the same controller, the app applies the current phase without replaying missed changes. If Bluetooth drops or the app closes unexpectedly, the controller may keep the last applied mode and speed until reconnection. A stopped schedule that could not restore its settings keeps that restoration pending for the same controller.

## Privacy and local data

No account, cloud service, or telemetry is used. Preferences and Bluetooth addresses remain under `%LOCALAPPDATA%\ACInfinityDesktop`. Each widget saves its PC schedule, controller association, and previous controller mode/presets locally with its preferences. Additional widget settings live in its `controllers` subfolder. Sensor history is stored in the shared `history` subfolder, separately for each controller address.

History is collected only while this PC is connected. It retains up to 24 hours or 6,000 samples per controller, and is saved about once per minute and on exit. It does not download earlier readings from the controller or cloud. Gaps longer than 60 seconds are not joined on charts. Disconnected widgets retain history while clearing live values.

## Build and verify

Install the .NET 10 SDK, then run:

```powershell
dotnet build ACInfinityDesktop.csproj -c Release
./publish.ps1
./verify.ps1
```

The publish profile creates one self-contained Windows x64 application under `Releases/dotnet-<version>`. WPF trimming is disabled. Generated binaries, private settings, and diagnostics are ignored by Git.

`verify.ps1` runs 383 offline protocol/settings/schedule checks and 10 window smoke checks, including multiple-widget isolation, hidden offline restoration, independent window shutdown, and the separate history panel. Smoke checks use in-memory settings and never connect to Bluetooth. Diagnostic results remain under the ignored `diagnostics/verification` directory.

The GitHub workflow builds and verifies pull requests and pushes to `main` or `master`. Pushing a version tag matching the project version, such as `v0.7.0`, runs the same checks and publishes a release containing one Windows executable. The workflow has read-only permissions during builds; release publishing receives repository-content write permission in a separate job. No personal access token is stored in the repository.

For maintainers, `--capture-live --output screenshots` exports the four documentation views into the screenshots folder from a real connection, waits for a fresh sample, and exits. Capture mode blocks fan writes and leaves saved preferences and history unchanged. Close other copies before capturing.

For UI development, open `ACInfinityDesktop.code-workspace` in VS Code or the solution in Visual Studio. Each window has separate XAML and code-behind; shared styles live in `Theme.xaml`.

## Compatibility and testing limits

Control is restricted to verified Controller 67-compatible Bluetooth replies. Unsupported advertised models cannot receive commands. A compatible protocol without a known model advertisement does not confirm the exact controller model. Friendly fan names are user labels, not detected fan hardware.

VPD is calculated from air temperature and humidity without a leaf-temperature offset. Wi-Fi cloud controllers and independent fan-port control are outside the supported scope.

Live checks on one Controller 67 verified basic cycle/daily program writes and fresh readback, PC-managed run/idle phases reaching reported outputs 10 and 0, restoration on stop, and the original settings after reconnect. Reported output is not a motor RPM measurement. Multiple-widget lifecycle and isolation have offline coverage; simultaneous physical controllers, full-duration native timing, and real radio-dropout recovery still require hardware testing.

When reporting an issue, include your Windows version, controller model, and reproduction steps. Redact Bluetooth addresses and other private diagnostic details.

## Support the project

If this tool helped you out or you enjoy having fan control on your desktop, donations are appreciated. [Support Coolshrimp Modz](https://links.coolshrimpmodz.com/).

## Source and license

The application is native C# / WPF. `Native/BluetoothController.cs` handles BLE/GATT, and `Native/Protocol.cs` validates controller packets. `Native/SensorHistory.cs` maintains bounded local history; `Native/FanSchedule.cs` evaluates PC schedule phases.

Copyright 2026 Coolshrimp Modz. All rights reserved for the application code; see [LICENSE](LICENSE). Upstream protocol notices and their separate license terms are retained in [vendor/THIRD_PARTY_NOTICES.md](vendor/THIRD_PARTY_NOTICES.md) and [vendor/Viss-LICENSE.txt](vendor/Viss-LICENSE.txt).
