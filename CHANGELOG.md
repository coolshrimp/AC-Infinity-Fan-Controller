# Changes

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
