using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ACInfinityDesktop;

internal interface IScheduleController
{
    bool Connected { get; }
    ControllerState State { get; }
    Task ReadProgramsAsync();
    Task SetPresetsAsync(ControllerPresetSnapshot snapshot);
}

// The owner serializes calls with its controller-operation gate. This class owns only
// schedule recovery state; it never changes windows, timers, or the motor-output reading.
internal sealed class AppScheduleSession
{
    private readonly Preferences prefs;
    private readonly IScheduleController controller;
    private readonly Action save;
    public bool HasSchedule => prefs.AppSchedule.Enabled || prefs.ScheduleRestore != null;

    public AppScheduleSession(Preferences prefs, IScheduleController controller, Action save)
    {
        this.prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.save = save ?? throw new ArgumentNullException(nameof(save));
    }

    public async Task StartAsync(AppFanSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        string error = schedule.Validate();
        if (error.Length != 0) throw new ArgumentException(error, nameof(schedule));
        RequireAddress();
        if (!controller.Connected) throw new IOException("Connect to this controller before starting its PC schedule.");
        var oldSchedule = prefs.AppSchedule;
        var oldRestore = prefs.ScheduleRestore;
        string oldAddress = prefs.ScheduleAddress;
        try
        {
            if (prefs.ScheduleRestore == null)
            {
                await controller.ReadProgramsAsync();
                RequireAddress();
                if (!controller.Connected) throw new IOException("The controller disconnected before its settings could be preserved.");
                prefs.ScheduleRestore = ControllerPresetSnapshot.Capture(controller.State);
            }
            if (!prefs.ScheduleRestore.IsValid) throw new InvalidDataException("The saved controller restore settings are invalid.");
            prefs.ScheduleAddress = prefs.address;
            prefs.AppSchedule = CopyEnabled(schedule);
            // A durable baseline must exist before even the first override is attempted.
            save();
        }
        catch
        {
            prefs.AppSchedule = oldSchedule;
            prefs.ScheduleRestore = oldRestore;
            prefs.ScheduleAddress = oldAddress;
            throw;
        }
        await TickAsync(DateTimeOffset.Now);
    }

    public async Task TickAsync(DateTimeOffset now)
    {
        if (!HasSchedule || !controller.Connected) return;
        RequireAddress();
        if (!prefs.AppSchedule.Enabled)
        {
            await StopAsync();
            return;
        }
        var decision = prefs.AppSchedule.Evaluate(now);
        if (prefs.ScheduleRestore == null)
        {
            await controller.ReadProgramsAsync();
            RequireAddress();
            if (!controller.Connected) return;
            prefs.ScheduleRestore = ControllerPresetSnapshot.Capture(controller.State);
            save();
        }
        if (!prefs.ScheduleRestore.IsValid) throw new InvalidDataException("The saved controller restore settings are invalid.");
        bool running = decision.WithinWindow && decision.Description == "On phase";
        var desired = new ControllerPresetSnapshot
        {
            Mode = running ? 2 : 1,
            On = prefs.AppSchedule.RunSpeed,
            Off = prefs.AppSchedule.IdleSpeed
        };
        var current = controller.State;
        if (current.Mode == desired.Mode && current.On == desired.On && current.Off == desired.Off) return;
        // Retry persistence even when a prior tick captured a baseline but its save failed.
        // An in-memory restore point alone never authorizes a controller write.
        save();
        RequireAddress();
        if (!controller.Connected) return;
        await controller.SetPresetsAsync(desired);
    }

    public async Task StopAsync(bool disable = true)
    {
        // Manual controls call Stop too. With no owned schedule they must remain untouched.
        if (!HasSchedule) return;
        RequireAddress();
        if (disable) prefs.AppSchedule.Enabled = false;
        // Preserve pending recovery across disconnects, write errors, and process exit.
        save();
        var snapshot = prefs.ScheduleRestore;
        if (snapshot == null || !controller.Connected) return;
        if (!snapshot.IsValid) throw new InvalidDataException("The saved controller restore settings are invalid.");
        RequireAddress();
        await controller.SetPresetsAsync(snapshot);
        prefs.ScheduleRestore = null;
        try { save(); }
        catch
        {
            // The controller was restored, but the durable pending marker may remain.
            // Retrying that exact restore is safe; discarding its baseline is not.
            prefs.ScheduleRestore = snapshot;
            throw;
        }
    }

    private void RequireAddress()
    {
        if (!Regex.IsMatch(prefs.address ?? "", "^(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$"))
            throw new IOException("Connect to a controller first.");
        if (HasSchedule && !string.Equals(prefs.ScheduleAddress, prefs.address, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Reconnect the previous scheduled controller to restore it first.");
    }

    private static AppFanSchedule CopyEnabled(AppFanSchedule schedule) => new()
    {
        Enabled = true,
        StartMinute = schedule.StartMinute,
        EndMinute = schedule.EndMinute,
        OnMinutes = schedule.OnMinutes,
        OffMinutes = schedule.OffMinutes,
        RunSpeed = schedule.RunSpeed,
        IdleSpeed = schedule.IdleSpeed
    };
}
