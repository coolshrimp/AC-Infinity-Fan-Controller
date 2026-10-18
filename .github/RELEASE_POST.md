# AC Infinity Desktop v0.8.0

I built a small Windows desktop widget for AC Infinity Controller 67 so I can keep my fan controls and sensor readings nearby without opening the phone app.

It connects over Bluetooth and shows temperature, humidity, calculated VPD, fan speed, and local sensor history. You can review saved readings in a separate History panel, change the colors, pin it on top, or tuck it into the tray.

The widget now has **Status**, **Chart**, and **Schedule** tabs, with the program editor built into Schedule. The **⋯** fan options menu and current-mode dropdown give you Manual Off, Manual On, Auto, Timer to On, Timer to Off, Repeating cycle, and Daily schedule.

- **Controller:** edit all seven basic modes, shared On/Off presets from 0 to 10, and four independent Auto triggers for temperature and humidity. Native timers and cycles use hours, minutes, and seconds. **Save settings** returns to the previous mode if firmware changes it during the write; **Save & activate** selects the displayed mode. Programs saved on the controller keep running without the PC. Daily schedules use the controller's clock; the app does not synchronize it.
- **This PC:** combine a daily time window with a repeating run/idle cycle. Overnight and all-day windows are supported, and the fan uses the idle speed outside the window. Minimize to the tray to keep it running.

Saving settings can affect fan output immediately, and some firmware briefly activates the edited mode while saving. Select Manual On or Manual Off before editing a timer that is currently counting down.

This PC schedules need the PC awake, the app running, and Bluetooth connected. **Stop & restore** returns the controller to its previous mode and speed presets. Quitting normally restores those settings too, while keeping the schedule saved for the next launch. Reconnecting resumes the current phase without replaying missed changes. If the connection drops or the app closes unexpectedly, the controller may keep its last applied mode until reconnection.

Using more than one Bluetooth controller? Each gets its own widget with separate controls, schedules, and readings. Remembered controllers reconnect quietly on relaunch and only open their widgets once connected. Fans plugged into the same Controller 67 still share that controller's programming.

The Windows x64 release is a single download with the runtime included. Find it on the [Releases page](https://github.com/coolshrimp/AC-Infinity-Fan-Controller/releases).

Tested with my Controller 67: a changed high-temperature threshold and its enable flag, preservation of unedited Auto values, exact 61/67-second timer settings, and native mode selection. PC-managed output reached 10 and 0, stopping restored its previous settings, and the complete original stored settings were verified after reconnect. Full-duration native timing, Auto response to changing sensor thresholds, real radio-dropout recovery, and simultaneous physical controllers still need testing. Reported fan output is not measured motor RPM. This is an unofficial project and is not affiliated with AC Infinity.

Feedback and reproducible bug reports are welcome. Please leave Bluetooth addresses and private diagnostics out of public posts.

If this tool helped you out or you enjoy having fan control on your desktop, donations are appreciated. [Support Coolshrimp Modz](https://links.coolshrimpmodz.com/).
