# Changes

## 0.8.1

- Make the Schedule tab's Active controller mode selector activate the stored program and load fresh settings, including Auto, into editable fields.
- Keep parameter editing available through Save settings and Save & activate; reading settings and documentation capture do not activate modes.
- Restart an active timer with its edited duration through Save & activate; disable save-only while that countdown is active.
- Remove the white native gutter from the fan options menu and mode dropdown.
- Match popup borders, selected rows, and hover states to the widget's styling.

## 0.8.0

- Embed the program editor in a Schedule tab beside Status and Chart.
- Use the fan’s ⋯ options and current-mode dropdown for Manual Off/On, Auto, Timer to On/Off, Repeating cycle, and Daily schedule.
- Edit all seven basic controller modes with native On/Off presets from 0 to 10.
- Add four independent Auto triggers with whole-degree Celsius/Fahrenheit thresholds; preserve unedited temperature pairs and reserved settings.
- Edit native timers and cycles to whole-second precision using HH:mm:ss, from one second through 24 hours.
- Add Save settings that reselects the prior mode if firmware switches it during a write, alongside Save & activate; verify saved values through readback.
- Extend offline controller-editing checks and window smoke coverage for Auto and both timer layouts.
- Keep the PC-managed daily-window/cycle scheduler and its restoration behavior.
- Block edits to the currently running timer until a manual mode is selected, avoiding countdown resets.
- Verify Auto threshold preservation, a changed temperature pair, trigger enable flags, exact 61/67-second timer settings, and native mode selection on one live Controller 67.
- Confirm PC-managed output 10/0, stop restoration, and the complete original stored settings after reconnect; full-duration native timing and sensor-triggered Auto response remain unverified.

## 0.7.0

- Add a Fan schedules panel with separate Controller and This PC sections.
- Save basic daily or repeating-cycle programs and On/Off speed presets directly to Controller 67, with setting readback after writes.
- Run local daily windows with repeating fan cycles, including overnight and all-day windows.
- Show PC schedule state in the widget and provide schedule controls in its tray menu.
- Preserve the previous controller mode and presets; restore them on stop, manual control, disconnect, or normal quit.
- Keep enabled PC schedules for the next launch and resume the current phase after reconnection without replaying missed changes.
- Retain pending restoration data when a controller is disconnected or a restore write fails.

- Verify native scheduling writes, PC-managed run/idle output, and original-setting restoration against a live Controller 67.
- Add scheduling panel screenshots and 301 scheduling, program, persistence, and recovery checks.

## 0.6.6

- Verify valid edge-clamped panel docking on smaller Windows desktops.
- Wait for WPF layout completion before checking and capturing window smoke tests.
- Preserve detailed check diagnostics when a hosted build fails.

## 0.6.5

- Simplify the widget to Status and Chart tabs; open on Status with readings, controller mode, and fan controls together.
- Independent widgets for multiple Bluetooth controllers, with separate controls, readings, history, appearance, and tray icons.
- Restore additional controllers quietly in the background; show their widgets only after connection and prevent duplicate connections.
- Closing one controller widget leaves the others running.
- Separate History panel with larger plots, range selection, sample counts, and timestamps; saved data resumes after reopening.
- Normalize malformed saved settings and validate speed presets returned after writes.
- Preserve valid history samples when a saved array contains null entries.
- Add version-tagged Windows single-file release workflow and repeatable offline/window verification.
- Refresh public documentation, screenshots, license notice, and credential/binary ignore rules.

## 0.6.4

- Panel and accent preset dropdowns with swatches, alongside unrestricted custom colors.
- Matching WPF color-picker form with saturation/brightness area, hue slider, preview, validated hex entry, and cancel/apply behavior.
- Editable fan-name dropdown with common CLOUDLINE sizes and room/use suggestions.
- Celsius/Fahrenheit toggle in settings, synchronized with the main temperature button and charts.
- Sensor hover explanations, including VPD meaning and how it is calculated.
- Current fan speed and mode above the controls in one compact card.

## 0.6.3

- Friendly AC Infinity heading replaces technical Bluetooth advertisement names on the widget.
- Per-controller Name on widget field in Bluetooth settings, stored locally and retained across reconnects.
- Bluetooth name and controller identification remain in settings; fan labels are not presented as detected model numbers.
- Generic discovery progress keeps technical device names off the main panel.

## 0.6.2

- Slider follows reported fan output; dragging previews a target and releasing applies it once, without an Apply button. Keyboard adjustments apply on key release.
- Independent accent-colored track and number show current output while the target is pending. Refreshes preserve an active drag.
- Two-second reads during manual ramps or detected speed changes, returning to ten seconds after fresh replies confirm settling. Stale redraws do not count toward settling.
- Slider zero selects the existing saved Off mode without changing its minimum speed.
- Added adaptive polling tests and offline slider interaction checks.

## 0.6.1

- Compact On/Off toggle, speed slider, and Apply button beneath the sensor readings in every main-widget view.
- UI Maker modern toggle and slider templates for fan controls and appearance preferences.
- Separate settings forms dock to and follow the main window, repositioning around screen edges and when the widget changes size.
- Stored programs remain available from Device status; the redundant Fan control form has been removed.
- Six docking placement checks and a window-follow check supplement existing offline verification.

## 0.6.0

- Compact Mac-inspired widget with saved Readings, Device Status, and Chart views.
- Device chooser and last-updated connection header; separate designer-editable Fan control window.
- Up to 24 hours of local per-controller sensor history with 1h/6h/24h plots and clear data gaps.
- Faster temporary reads after manual changes with reported-to-target speed feedback.
- No synthesized scores or unsupported port data.

## 0.5.1

- Automatically probe discovered ACI candidates before listing them, with detected Bluetooth addresses and verification labels.
- Show the connected user?s MAC in the address field; ship no personal controller address.
- Add Created by: Coolshrimp Mods credit linking to https://coolshrimpmodz.com/.

## 0.5.0

- Optional Bluetooth address field starts blank with discovery guidance; remembered reconnection remains local to each user.
- Full panel/accent color pickers replace fixed presets, with saved colors and appearance defaults.
- Text contrast adapts to light and dark custom panels.

## 0.4.3

- Bundle compression is conditional on self-contained publishing, fixing NETSDK1176 when UI Maker overrides the setting.
- Added a dedicated VS Code workspace with single-file, self-contained Windows x64 publishing enabled.

## 0.4.2

- Status refreshes no longer dim buttons or interrupt slider interaction.
- Commands clicked during a status read wait for it to finish; duplicate refreshes are skipped.

## 0.4.1

- Preserved the updated user layout, removed its extra outer frame, and aligned header and footer controls.
- Content-sized main window and shared background opacity prevent clipping and opaque layering.
- Vector pushpin with pinned/unpinned state and hover tooltips for controls.

## 0.4.0

- Doubled fan rotation at every nonzero speed.
- Split Bluetooth, Appearance, Stored Programs, and Help into independent designer-editable WPF windows and code-behind.
- Shared Theme.xaml styles all forms; content-sized windows keep controls inside their panels.
- Panels open beside the widget when space permits, with one settings window visible at a time.

## 0.3.3

- Fan blades rotate proportionally to live output, stopping at zero, offline, or hidden.
- Speed changes preserve blade position; the slider alone does not change the animation.

## 0.3.2

- Bluetooth and appearance settings open as styled floating popups, without expanding the widget.
- Stored programs and help also use popups; removed the main window scrollbar.
- Popups close with the close button, Escape, outside click, or minimize to tray.

## 0.3.1

- Repeated launches restore the existing window, including from the tray, and exit silently.
- Cross-process checks cover duplicate rejection, early activation delivery, and lock release.

## 0.3.0

- Frameless draggable widget with internal pin, settings, minimize-to-tray, and quit controls.
- Remembered controller, automatic single-device connection, and bounded reconnect delays.
- Bluetooth settings panel accessible through the status badge.
- Saved theme, accent color, opacity, position, pin state, and connection preference.
- Native single-EXE release and offline preference/reconnect/UI checks.

## 0.2.0

- Native .NET 10 WPF implementation, Windows GATT status reads, manual fan controls, and stored program display.
