# AC Infinity Desktop v0.7.0

I built a small Windows desktop widget for AC Infinity Controller 67 so I can keep my fan controls and sensor readings nearby without opening the phone app.

It connects over Bluetooth and shows temperature, humidity, calculated VPD, fan speed, and local sensor history. You can review saved readings in a separate History panel, change the colors, pin it on top, or tuck it into the tray.

This version adds a Fan schedules panel with two options:

- **Controller:** save a basic daily schedule or repeating cycle directly to the controller, along with its run and idle speeds. It keeps running without the PC. Daily schedules use the controller's clock; the app does not synchronize it.
- **This PC:** combine a daily time window with a repeating run/idle cycle. Overnight and all-day windows are supported, and the fan uses the idle speed outside the window. Minimize to the tray to keep it running.

This PC schedules need the PC awake, the app running, and Bluetooth connected. **Stop & restore** returns the controller to its previous mode and speed presets. Quitting normally restores those settings too, while keeping the schedule saved for the next launch. Reconnecting resumes the current phase without replaying missed changes. If the connection drops or the app closes unexpectedly, the controller may keep its last applied mode until reconnection.

Using more than one Bluetooth controller? Each gets its own widget with separate controls, schedules, and readings. Remembered controllers reconnect quietly on relaunch and only open their widgets once connected. Fans plugged into the same Controller 67 still share that controller's programming.

The Windows x64 release is a single download with the runtime included. Find it on the [Releases page](https://github.com/coolshrimp/AC-Infinity-Fan-Controller/releases).

Tested with my Controller 67: basic cycle/daily program writes and readback, PC-managed output switching to 10 and 0, and restoration verified after reconnect. Full-duration native timing and simultaneous connections to several physical controllers still need testing. This is an unofficial project and is not affiliated with AC Infinity.

Feedback and reproducible bug reports are welcome. Please leave Bluetooth addresses and private diagnostics out of public posts.

If this tool helped you out or you enjoy having fan control on your desktop, donations are appreciated. [Support Coolshrimp Modz](https://links.coolshrimpmodz.com/).
