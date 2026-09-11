# AC Infinity Desktop

Created by: [Coolshrimp Mods](https://coolshrimpmodz.com/)

A compact Windows Bluetooth widget for AC Infinity **Controller 67 (CTR67A)** and its shared fan output. Native C# / WPF, with editable XAML for the UI Maker extension in VS Code. Unofficial; not affiliated with AC Infinity.

## Run

Open `Releases/dotnet-0.6.3/AC-Infinity-Desktop.exe`. Distribute this one file. The .NET runtime is bundled; no Python, Node, Electron, or separate .NET installation is needed. Requires Windows 10 version 2004 or newer / Windows 11, x64, and a working Bluetooth adapter. Native runtime components extract automatically on first launch. The executable is unsigned.

Quit an older running copy using its tray menu before opening this version. Close other phone/Mac controller connections so Bluetooth is available.

## Widget views

The compact card has three saved views, inspired by the AC Infinity Mac widgets:

- **Readings**: temperature, humidity, and calculated VPD.
- **Device status**: the same readings plus the shared fan output, active mode, and a stored-programs button.
- **Chart**: separate temperature, humidity, and VPD plots with 1h / 6h / 24h ranges. Each series has its own labeled scale; gaps longer than 60 seconds are not connected.

The heading defaults to **AC Infinity**. In Bluetooth settings, **Name on widget** saves a friendly name (for example, CLOUDLINE T6 or Workshop fan) separately for each controller on this PC. Blank restores the default. Technical advertised names and controller identification stay in Bluetooth settings. Existing saved Bluetooth names are not reused as the widget heading.

Click the device name to choose a controller or manage Bluetooth. The header shows the last successful read and connection status. A compact On/Off toggle and speed slider sit directly below the readings in every view. The thumb follows reported output when idle; dragging selects a target and releasing applies it without confirmation. The colored fill and number always represent the latest reported output, independently of the target. Stored programs are available from the three-dot button in Device status. The widget view and chart range are remembered across launches.

History is collected by this PC while connected, retained for up to 24 hours / 6,000 samples per controller, and saved about once per minute and on exit under `%LOCALAPPDATA%\ACInfinityDesktop\history`. It does not download earlier readings from the device or cloud. Two readings are needed to draw a chart. Stored history remains visible while disconnected; live numbers clear so old readings are not presented as current. No AI score or independent port status is synthesized for Controller 67.

## Connect and use

- Discovery looks for ACI / AC Infinity names or manufacturer advertisements, then connects briefly to verify the expected Bluetooth service and characteristics. For supported/unknown model types it also validates read-only Controller 67 settings and live replies. Verified devices appear with their detected addresses; unsupported advertised model types are labeled and cannot receive control commands. Candidates that do not respond are omitted and can be retried by rescanning. No fan modes or speeds are changed during verification.
- At startup, the app looks for the last successfully connected controller. On first use it scans nearby devices and automatically connects if exactly one compatible controller is found.
- Click the **connection status badge** to open the Bluetooth panel, scan, select among multiple controllers, disconnect, or open Windows Bluetooth settings. The optional address field starts blank: enter your own Bluetooth address if known, or leave it blank to scan and choose a detected controller. After connecting, the field shows the connected user’s detected MAC. Disconnect clears the entry field. The remembered connection is stored privately on each PC and is never bundled into the release.
- Lost connections retry after increasing delays of 30, 60, then at most 120 seconds. Bluetooth operations also have their own timeouts. A remembered address keeps automatic reconnection directed at that controller.
- **Disconnect** pauses automatic retries for the current session. Scan, manually connect, re-enable automatic connection in settings, or restart to resume. Disabling automatic connection still allows manual discovery and connection.
- The fan icon rotates according to the reported output: 0 stops, 5 turns once per second, and 10 turns twice per second. Rotation stops when disconnected or hidden in the tray. This indicates relative speed, not measured motor RPM.
- Automatic and manual status refreshes keep controls enabled and visually steady. A fan command clicked during a status read waits for that read to finish; Bluetooth requests remain serialized. Controls still disable while applying changes or when disconnected.
- Live readings refresh every 10 seconds when stable. Manual commands and detected output changes switch to two-second reads. Two fresh replies at the target, or at least 10 seconds of unchanged output, return to the slower interval. An unresolved target expires after 90 seconds. Duplicate UI redraws do not count as new readings, and requests never overlap; slow Bluetooth replies can lengthen the effective interval. These are controller-reported levels, not measured RPM. Reconnection only reads settings and status; it does not set the fan speed or change its mode.
- Releasing the slider at **1–10** selects manual On mode at that speed. Releasing at **0** selects the saved Off mode; it does not overwrite the saved minimum. The On/Off toggle also uses stored presets and shows Auto when an automatic mode is active. Off mode can still run the fan if the saved Off speed is nonzero. Arrow/Home/End/Page keys apply on key release. Programmatic updates never send speed commands, and unchanged releases do nothing.
- Stored programs show Auto thresholds, timers, cycle durations, and daily schedule. Editing these programs is not implemented.

Only one normal instance runs per Windows session, even when launched from different folders. Opening the EXE again restores the existing window from the tray instead of creating another connection. Older versions still block duplicates but cannot respond to the new restore signal; quit them from the tray before upgrading.

Bluetooth, appearance, stored programs, and connection help each have their own WPF Window, XAML, and code-behind. They size vertically to their content and dock to the main widget, following it as it moves or changes view. Docking chooses a side, above, or below within the current monitor’s work area; if neither window can fit together, the panel is clamped on-screen. Close with × or Escape; minimizing to the tray hides them. Opening a different panel hides the previous one. Opening settings does not stretch or scroll the main widget.

## Widget controls

- The **pushpin** toggles always on top for the widget and its panels; its angle indicates the pin state.
- **⚙** opens saved preferences: automatic connection, full RGB color pickers for the panel and accent, and panel opacity. Color buttons show a swatch and hex value. **Restore appearance defaults** resets colors and opacity without changing connection or pin settings.
- **−** hides to the system tray. Double-click the tray icon or choose Show controller to restore.
- **×** quits and releases Bluetooth. The tray menu also has Quit.
- Drag any noninteractive panel area to move the widget, including when pinned. Controls retain their normal mouse behavior.

Connection, temperature unit, pin state, appearance, and position are saved locally in `%LOCALAPPDATA%\ACInfinityDesktop\preferences.json`. Delete that file while the app is closed to reset preferences. No accounts, cloud service, or telemetry are used.

## UI Maker / development

Open `ACInfinityDesktop.code-workspace` in VS Code (or open this project folder directly) to load the included UI Maker single-file/self-contained release settings. Select `ACInfinityDesktop.csproj` in UI Maker. Edit each form separately in its WPF designer: `MainWindow.xaml`, `BluetoothWindow.xaml`, `AppearanceWindow.xaml`, `ProgramsWindow.xaml`, and `HelpWindow.xaml`. `SensorChartView.xaml` is the reusable chart view. Shared colors and control styles live in `Theme.xaml`, including UI Maker’s `UimToggleSwitch` and `UimModernSlider` templates. These style standard WPF CheckBox and Slider controls and remain editable in UI Maker. All windows are explicitly included in the project and have their own `.xaml.cs` code-behind. Development requires the .NET 10 SDK.

```powershell
dotnet build ACInfinityDesktop.csproj -c Release
./publish.ps1
```

The `SingleExe` publish profile produces a self-contained Windows x64 executable in `Releases/dotnet-0.6.3`. WPF trimming is disabled. Windows Forms provides the tray icon, color picker, and monitor work-area lookup.

If UI Maker reports NETSDK1176, its command has forced `--self-contained false`. Compression now turns off automatically for that mode, but the resulting executable requires the .NET Desktop Runtime. For the standalone release, use the included workspace settings or `./publish.ps1`. A parent workspace selecting this folder through UI Maker does not load this folder's `.vscode/settings.json`; open the supplied `.code-workspace` file to use the project settings.

## Verification

The release passes 65 offline checks covering protocol frames, CRC, response matching, decoding, acknowledgments, input bounds, preferences, reconnect behavior, single-instance activation, device eligibility, history, docking placement, and adaptive polling transitions. Offline window checks exercise release-only slider submission, telemetry during editing, duplicate/unchanged releases, pin/unpin, a connection panel following the widget, appearance changes, and tray hide/restore before rendering a screenshot. The main widget is 450 logical pixels wide and sizes vertically to its selected view.

```powershell
& ./Releases/dotnet-0.6.3/AC-Infinity-Desktop.exe --self-test --output "$PWD/checks.json"
& ./Releases/dotnet-0.6.3/AC-Infinity-Desktop.exe --smoke --output "$PWD/window.png"
& ./Releases/dotnet-0.6.3/AC-Infinity-Desktop.exe --smoke --settings --output "$PWD/settings.png"
```

Add `--bluetooth` to smoke mode to render the Bluetooth window; `--settings` renders the appearance window.

For layout previews, `--smoke --readings`, `--smoke --chart`, and `--smoke --controls` render those views. Add `--demo` only to generate explicitly labeled simulated readings for visual checks; normal app launches never use this data.

Smoke mode uses default in-memory preferences and does not connect to Bluetooth or save settings. Optional `--probe --address <Bluetooth MAC> --output <JSON path>` performs read-only live discovery and three status samples. Close other controller apps before using it.

Native Bluetooth reads were verified on real Controller 67 hardware during development. This release's reconnection policy has offline coverage; a real radio dropout/recovery and physical drag interaction still need user verification. The release-to-set interaction and adaptive polling have offline coverage; physical slider input and sustained two-second reads still need testing with the controller. Motor response to manual writes has not been deliberately hardware-tested. Writes validate controller acknowledgments.

## Scope and limitations

Fan controls support Controller 67-compatible verified responses. Other advertised model types may be identified through their AC Infinity GATT service but are listed as unsupported for control. New discovery verification has offline coverage; additional hardware/model verification remains necessary. The two fan connections share programming; the app does not discover individual attached fans. Controller 67 (CTR67A) identifies the controller, not the fan attached to it. The current Bluetooth replies do not supply the attached fan model; a friendly name is a user label, not detected hardware identity. An advertised type of 1 is mapped to Controller 67; a validated compatible protocol without that type is labeled as an unconfirmed exact model. VPD is calculated from air temperature and humidity without a leaf-temperature offset. This is not a Controller AI/Wi-Fi cloud client. Weak Bluetooth signal and another app holding the connection can prevent connection.

## Source layout and repository

- `MainWindow.xaml` and code-behind: main widget and controller coordination
- `BluetoothWindow.xaml`, `AppearanceWindow.xaml`, `ProgramsWindow.xaml`, `HelpWindow.xaml`: separate editable forms
- `Theme.xaml`: shared designer-visible styles
- `Native/PanelDocking.cs`: monitor-aware placement for attached forms
- `Native/SpeedPolling.cs`: fresh-sample settling and adaptive refresh timing
- `SensorChartView.xaml` / code-behind: local sensor plots
- `Native/SensorHistory.cs`: bounded per-controller history
- `Native/BluetoothController.cs`: Windows BLE/GATT connection and requests
- `Native/Protocol.cs`: frame validation and Controller 67 data
- `Native/Preferences.cs`: local settings and reconnect policy
- `Native/ProtocolChecks.cs`: offline checks
- `Properties/PublishProfiles/SingleExe.pubxml`: release profile
- `vendor/`: retained upstream protocol license notices

The local Git repository tracks the native project, artwork, and documentation. Generated binaries, caches, diagnostic captures, and device addresses are excluded. Obsolete Electron/Python prototypes and superseded releases have been removed from the working folder. Local protocol findings and current verification captures remain under diagnostics; the current executable is under Releases/dotnet-0.6.3. No remote is configured and nothing is published. Before publishing externally, choose a license for the new application code and retain the included third-party notices. Report issues with Windows version, controller model, and reproduction steps; redact Bluetooth addresses and personal diagnostic data.
