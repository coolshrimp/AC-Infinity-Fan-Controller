# AC Infinity Desktop

Created by: [Coolshrimp Mods](https://coolshrimpmodz.com/)

A compact Windows Bluetooth widget for AC Infinity **Controller 67 (CTR67A)** and its shared fan output. Native C# / WPF, with editable XAML for the UI Maker extension in VS Code. Unofficial; not affiliated with AC Infinity.

## Run

Open `Releases/dotnet-0.6.0/AC-Infinity-Desktop.exe`. Distribute this one file. The .NET runtime is bundled; no Python, Node, Electron, or separate .NET installation is needed. Requires Windows 10 version 2004 or newer / Windows 11, x64, and a working Bluetooth adapter. Native runtime components extract automatically on first launch. The executable is unsigned.

Quit an older running copy using its tray menu before opening this version. Close other phone/Mac controller connections so Bluetooth is available.

## Widget views

The compact card has three saved views, inspired by the AC Infinity Mac widgets:

- **Readings**: temperature, humidity, and calculated VPD.
- **Device status**: the same readings plus the shared fan output, active mode, and a Control button.
- **Chart**: separate temperature, humidity, and VPD plots with 1h / 6h / 24h ranges. Each series has its own labeled scale; gaps longer than 60 seconds are not connected.

Click the device name to choose a controller or manage Bluetooth. The header shows the last successful read and connection status. Manual speed controls live in their own Fan control window; stored programs are available there. The widget view and chart range are remembered across launches.

History is collected by this PC while connected, retained for up to 24 hours / 6,000 samples per controller, and saved about once per minute and on exit under `%LOCALAPPDATA%\ACInfinityDesktop\history`. It does not download earlier readings from the device or cloud. Two readings are needed to draw a chart. Stored history remains visible while disconnected; live numbers clear so old readings are not presented as current. No AI score or independent port status is synthesized for Controller 67.

## Connect and use

- Discovery looks for ACI / AC Infinity names or manufacturer advertisements, then connects briefly to verify the expected Bluetooth service and characteristics. For supported/unknown model types it also validates read-only Controller 67 settings and live replies. Verified devices appear with their detected addresses; unsupported advertised model types are labeled and cannot receive control commands. Candidates that do not respond are omitted and can be retried by rescanning. No fan modes or speeds are changed during verification.
- At startup, the app looks for the last successfully connected controller. On first use it scans nearby devices and automatically connects if exactly one compatible controller is found.
- Click the **connection status badge** to open the Bluetooth panel, scan, select among multiple controllers, disconnect, or open Windows Bluetooth settings. The optional address field starts blank: enter your own Bluetooth address if known, or leave it blank to scan and choose a detected controller. After connecting, the field shows the connected user?s detected MAC. Disconnect clears the entry field. The remembered connection is stored privately on each PC and is never bundled into the release.
- Lost connections retry after increasing delays of 30, 60, then at most 120 seconds. Bluetooth operations also have their own timeouts. A remembered address keeps automatic reconnection directed at that controller.
- **Disconnect** pauses automatic retries for the current session. Scan, manually connect, re-enable automatic connection in settings, or restart to resume. Disabling automatic connection still allows manual discovery and connection.
- The fan icon rotates according to the reported output: 0 stops, 5 turns once per second, and 10 turns twice per second. Rotation stops when disconnected or hidden in the tray. This indicates relative speed, not measured motor RPM.
- Automatic and manual status refreshes keep controls enabled and visually steady. A fan command clicked during a status read waits for that read to finish; Bluetooth requests remain serialized. Controls still disable while applying changes or when disconnected.
- Live readings refresh every 15 seconds. After a successful manual change, reads temporarily run every two seconds for up to 30 seconds, showing reported output → requested output until the target is reached. These are controller-reported levels, not measured RPM. Reconnection only reads settings and status; it does not set the fan speed or change its mode.
- **Apply speed** selects manual On mode and sets level 1–10. **Resume On** and **Off mode** use stored speed presets. Off mode can still run the fan if the saved Off speed is nonzero.
- Stored programs show Auto thresholds, timers, cycle durations, and daily schedule. Editing these programs is not implemented.

Only one normal instance runs per Windows session, even when launched from different folders. Opening the EXE again restores the existing window from the tray instead of creating another connection. Older versions still block duplicates but cannot respond to the new restore signal; quit them from the tray before upgrading.

Bluetooth, appearance, stored programs, and connection help each have their own WPF Window, XAML, and code-behind. They size vertically to their content and open beside the main widget when screen space permits. Close with × or Escape; minimizing to the tray hides them. Opening a different panel hides the previous one. The main widget does not resize or scroll.

## Widget controls

- **◇ / ◆** toggles always on top. The filled diamond indicates pinned.
- **⚙** opens saved preferences: automatic connection, full RGB color pickers for the panel and accent, and panel opacity. Color buttons show a swatch and hex value. **Restore appearance defaults** resets colors and opacity without changing connection or pin settings.
- **−** hides to the system tray. Double-click the tray icon or choose Show controller to restore.
- **×** quits and releases Bluetooth. The tray menu also has Quit.
- Drag any noninteractive panel area to move the widget, including when pinned. Controls retain their normal mouse behavior.

Connection, temperature unit, pin state, appearance, and position are saved locally in `%LOCALAPPDATA%\ACInfinityDesktop\preferences.json`. Delete that file while the app is closed to reset preferences. No accounts, cloud service, or telemetry are used.

## UI Maker / development

Open `ACInfinityDesktop.code-workspace` in VS Code (or open this project folder directly) to load the included UI Maker single-file/self-contained release settings. Select `ACInfinityDesktop.csproj` in UI Maker. Edit each form separately in its WPF designer: `MainWindow.xaml`, `BluetoothWindow.xaml`, `AppearanceWindow.xaml`, `ProgramsWindow.xaml`, `HelpWindow.xaml`, and `FanControlWindow.xaml`. `SensorChartView.xaml` is the reusable chart view. Shared colors and control styles live in `Theme.xaml`. All windows are explicitly included in the project and have their own `.xaml.cs` code-behind. Development requires the .NET 10 SDK.

```powershell
dotnet build ACInfinityDesktop.csproj -c Release
./publish.ps1
```

The `SingleExe` publish profile produces a self-contained Windows x64 executable in `Releases/dotnet-0.6.0`. WPF trimming is disabled. Windows Forms is used only for the tray icon.

If UI Maker reports NETSDK1176, its command has forced `--self-contained false`. Compression now turns off automatically for that mode, but the resulting executable requires the .NET Desktop Runtime. For the standalone release, use the included workspace settings or `./publish.ps1`. A parent workspace selecting this folder through UI Maker does not load this folder's `.vscode/settings.json`; open the supplied `.code-workspace` file to use the project settings.

## Verification

The release passes 42 offline checks covering protocol frames, CRC, response matching, decoding, acknowledgments, input bounds, preference migration and round-trip storage, and reconnect delay/pause/reset behavior. Offline window checks exercise pin/unpin, connection panel visibility, appearance changes, and tray hide/restore before rendering a screenshot. Window checks also verify the main widget stays 540 × 740.

```powershell
& ./Releases/dotnet-0.6.0/AC-Infinity-Desktop.exe --self-test --output "$PWD/checks.json"
& ./Releases/dotnet-0.6.0/AC-Infinity-Desktop.exe --smoke --output "$PWD/window.png"
& ./Releases/dotnet-0.6.0/AC-Infinity-Desktop.exe --smoke --settings --output "$PWD/settings.png"
```

Add `--bluetooth` to smoke mode to render the Bluetooth window; `--settings` renders the appearance window.

For layout previews, `--smoke --readings`, `--smoke --chart`, and `--smoke --controls` render those views. Add `--demo` only to generate explicitly labeled simulated readings for visual checks; normal app launches never use this data.

Smoke mode uses default in-memory preferences and does not connect to Bluetooth or save settings. Optional `--probe --address <Bluetooth MAC> --output <JSON path>` performs read-only live discovery and three status samples. Close other controller apps before using it.

Native Bluetooth reads were verified on real Controller 67 hardware during development. This release's reconnection policy has offline coverage; a real radio dropout/recovery and physical drag interaction still need user verification. Motor response to manual writes has not been deliberately hardware-tested. Writes validate controller acknowledgments.

## Scope and limitations

Fan controls support Controller 67-compatible verified responses. Other advertised model types may be identified through their AC Infinity GATT service but are listed as unsupported for control. New discovery verification has offline coverage; additional hardware/model verification remains necessary. The two fan connections share programming; the app does not discover individual attached fans. VPD is calculated from air temperature and humidity without a leaf-temperature offset. This is not a Controller AI/Wi-Fi cloud client. Weak Bluetooth signal and another app holding the connection can prevent connection.

## Source layout and repository

- `MainWindow.xaml` and code-behind: main widget and controller coordination
- `BluetoothWindow.xaml`, `AppearanceWindow.xaml`, `ProgramsWindow.xaml`, `HelpWindow.xaml`: separate editable forms
- `Theme.xaml`: shared designer-visible styles
- `FanControlWindow.xaml`: standalone manual controls
- `SensorChartView.xaml` / code-behind: local sensor plots
- `Native/SensorHistory.cs`: bounded per-controller history
- `Native/BluetoothController.cs`: Windows BLE/GATT connection and requests
- `Native/Protocol.cs`: frame validation and Controller 67 data
- `Native/Preferences.cs`: local settings and reconnect policy
- `Native/ProtocolChecks.cs`: offline checks
- `Properties/PublishProfiles/SingleExe.pubxml`: release profile
- `vendor/`: retained upstream protocol license notices

The local Git repository tracks the native project, artwork, and documentation. Generated binaries, caches, diagnostic captures, and device addresses are excluded. Obsolete Electron/Python prototypes and superseded releases have been removed from the working folder. Local protocol findings and current verification captures remain under diagnostics; the current executable is under Releases/dotnet-0.6.0. No remote is configured and nothing is published. Before publishing externally, choose a license for the new application code and retain the included third-party notices. Report issues with Windows version, controller model, and reproduction steps; redact Bluetooth addresses and personal diagnostic data.
