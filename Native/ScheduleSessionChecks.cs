using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACInfinityDesktop;

internal static class ScheduleSessionChecks
{
    public static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException($"Schedule session test {passed + 1} failed: {description}");
            passed++;
        }
        async Task Reject(Func<Task> action, string description)
        {
            bool rejected = false;
            try { await action(); } catch (IOException) { rejected = true; } catch (InvalidDataException) { rejected = true; } catch (ArgumentException) { rejected = true; }
            Check(rejected, description);
        }
        static bool Same(ControllerPresetSnapshot? value, int mode, int on, int off) => value != null && value.Mode == mode && value.On == on && value.Off == off;
        static bool Current(FakeController controller, int mode, int on, int off) => controller.State.Mode == mode && controller.State.On == on && controller.State.Off == off;
        static DateTimeOffset LocalAt(int hour, int minute)
        {
            var wall = new DateTime(2026, 10, 18, hour, minute, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(wall, TimeZoneInfo.Local.GetUtcOffset(wall));
        }

        var normal = new Fixture();
        normal.Controller.OnRead = () => { normal.Controller.State.Mode = 7; normal.Controller.State.On = 0; normal.Controller.State.Off = 10; };
        var requested = AlwaysOn(); requested.Enabled = false;
        await normal.Session.StartAsync(requested);
        Check(normal.Events.SequenceEqual(new[] { "read", "save", "save", "write" }), "fresh read and durable recovery precede first override");
        Check(Same(normal.Prefs.ScheduleRestore, 7, 0, 10) && Same(normal.Durable.ScheduleRestore, 7, 0, 10), "fresh original settings are captured, including zero on-preset");
        Check(normal.Prefs.AppSchedule.Enabled && !requested.Enabled && !ReferenceEquals(requested, normal.Prefs.AppSchedule), "start arms an independent copy of the requested schedule");
        requested.RunSpeed = 3;
        Check(normal.Prefs.AppSchedule.RunSpeed == 8, "later editor changes cannot mutate the running schedule");
        Check(normal.Prefs.ScheduleAddress == normal.Prefs.address && normal.Session.HasSchedule, "schedule ownership remains tied to this controller");
        Check(Current(normal.Controller, 2, 8, 1), "on phase applies presets and manual-on mode");
        normal.Controller.State.Output = 0;
        int writes = normal.Controller.Writes.Count, saves = normal.SaveCalls;
        await normal.Session.TickAsync(LocalAt(12, 0));
        Check(normal.Controller.Writes.Count == writes && normal.SaveCalls == saves, "ramping motor output does not trigger redundant preset writes");
        await normal.Session.StopAsync();
        Check(Current(normal.Controller, 7, 0, 10), "stop restores exact original mode and presets");
        Check(!normal.Session.HasSchedule && !normal.Durable.AppSchedule.Enabled && normal.Durable.ScheduleRestore == null, "successful stop clears durable recovery only after restore");
        Check(normal.Events.TakeLast(3).SequenceEqual(new[] { "save", "write", "save" }), "stop persists intent before restore and clears afterward");

        var idle = new Fixture(); idle.Prefs.address = "";
        await idle.Session.StopAsync(); await idle.Session.StopAsync(false); await idle.Session.TickAsync(LocalAt(12, 0));
        Check(idle.SaveCalls == 0 && idle.Controller.Reads == 0 && idle.Controller.Writes.Count == 0, "no-op stop and tick leave manual controller state and settings alone");
        await Reject(() => idle.Session.StartAsync(AlwaysOn()), "start rejects a missing controller address");
        Check(!idle.Session.HasSchedule && idle.SaveCalls == 0 && idle.Controller.Reads == 0, "invalid address cannot stage a session");
        idle.Prefs.address = "bad-address";
        await Reject(() => idle.Session.StartAsync(AlwaysOn()), "start rejects malformed controller ownership");
        var invalid = new Fixture();
        var invalidPlan = AlwaysOn(); invalidPlan.OnMinutes = 0;
        await Reject(() => invalid.Session.StartAsync(invalidPlan), "start rejects invalid schedule before a live read");
        Check(invalid.Controller.Reads == 0 && invalid.SaveCalls == 0 && !invalid.Session.HasSchedule, "invalid schedule performs no controller or persistence operations");
        invalid.Controller.Connected = false;
        await Reject(() => invalid.Session.StartAsync(AlwaysOn()), "start requires a connected controller");
        Check(invalid.Controller.Reads == 0 && invalid.Controller.Writes.Count == 0, "offline start cannot capture stale settings");
        var readFailure = new Fixture(); readFailure.Controller.FailRead = true;
        await Reject(() => readFailure.Session.StartAsync(AlwaysOn()), "failed fresh read aborts start");
        Check(!readFailure.Session.HasSchedule && readFailure.SaveCalls == 0 && readFailure.Controller.Writes.Count == 0, "read failure leaves no armed schedule or fabricated baseline");

        var firstSave = new Fixture(); firstSave.FailOnSave = 1;
        var originalSchedule = firstSave.Prefs.AppSchedule;
        await Reject(() => firstSave.Session.StartAsync(AlwaysOn()), "first recovery save failure aborts start");
        Check(ReferenceEquals(originalSchedule, firstSave.Prefs.AppSchedule) && !firstSave.Session.HasSchedule && firstSave.Prefs.ScheduleAddress == "", "unsaved initial staging rolls back all schedule ownership");
        Check(firstSave.Prefs.ScheduleRestore == null && firstSave.Durable.ScheduleRestore == null && firstSave.Controller.Writes.Count == 0, "first save failure cannot authorize a hardware write");
        firstSave.FailOnSave = 0;
        await firstSave.Session.StartAsync(AlwaysOn());
        Check(firstSave.Controller.Reads == 2 && Same(firstSave.Prefs.ScheduleRestore, 3, 4, 2), "retry captures fresh original state after rolled-back start");

        var secondSave = new Fixture(); secondSave.FailOnSave = 2;
        await Reject(() => secondSave.Session.StartAsync(AlwaysOn()), "pre-write persistence failure blocks first override");
        Check(secondSave.Prefs.AppSchedule.Enabled && Same(secondSave.Prefs.ScheduleRestore, 3, 4, 2) && Same(secondSave.Durable.ScheduleRestore, 3, 4, 2), "already durable baseline remains owned after later save failure");
        Check(secondSave.Controller.Writes.Count == 0 && Current(secondSave.Controller, 3, 4, 2), "second save failure leaves original controller programming intact");
        secondSave.FailOnSave = 0;
        await secondSave.Session.TickAsync(LocalAt(12, 0));
        Check(secondSave.Events.TakeLast(2).SequenceEqual(new[] { "save", "write" }) && secondSave.Controller.Reads == 1, "retry must persist baseline again before override without recapturing it");

        var resumedCapture = new Fixture();
        resumedCapture.Prefs.AppSchedule = AlwaysOn(); resumedCapture.Prefs.ScheduleAddress = resumedCapture.Prefs.address;
        resumedCapture.CommitInitial(); resumedCapture.FailOnSave = 1;
        await Reject(() => resumedCapture.Session.TickAsync(LocalAt(12, 0)), "resume capture save failure blocks override");
        Check(Same(resumedCapture.Prefs.ScheduleRestore, 3, 4, 2) && resumedCapture.Durable.ScheduleRestore == null && resumedCapture.Controller.Writes.Count == 0, "unsaved resume capture remains pending but cannot authorize a write");
        resumedCapture.FailOnSave = 0;
        await resumedCapture.Session.TickAsync(LocalAt(12, 0));
        Check(resumedCapture.Controller.Reads == 1 && Same(resumedCapture.Durable.ScheduleRestore, 3, 4, 2) && Current(resumedCapture.Controller, 2, 8, 1), "later tick persists the original captured baseline before retrying override");
        Check(resumedCapture.Events.TakeLast(2).SequenceEqual(new[] { "save", "write" }), "resume retry never writes from an in-memory-only restore point");

        var overrideFailure = new Fixture(); overrideFailure.Controller.FailWrites = 1;
        await Reject(() => overrideFailure.Session.StartAsync(AlwaysOn()), "failed override retains recovery ownership");
        Check(overrideFailure.Prefs.AppSchedule.Enabled && Same(overrideFailure.Prefs.ScheduleRestore, 3, 4, 2) && Same(overrideFailure.Durable.ScheduleRestore, 3, 4, 2), "failed override keeps original durable presets");
        await overrideFailure.Session.TickAsync(LocalAt(12, 0));
        Check(overrideFailure.Controller.Writes.Count == 2 && Current(overrideFailure.Controller, 2, 8, 1), "next tick retries current phase after a failed write");
        await overrideFailure.Session.StopAsync();
        Check(Current(overrideFailure.Controller, 3, 4, 2), "recovery still restores original settings after failed override");
        var partialWrite = new Fixture(); partialWrite.Controller.FailWrites = 1; partialWrite.Controller.ApplyBeforeFailure = true;
        await Reject(() => partialWrite.Session.StartAsync(AlwaysOn()), "partially applied override reports failure");
        Check(Current(partialWrite.Controller, 2, 8, 1) && Same(partialWrite.Durable.ScheduleRestore, 3, 4, 2), "partial hardware change does not replace durable original baseline");
        await partialWrite.Session.StopAsync();
        Check(Current(partialWrite.Controller, 3, 4, 2), "stop repairs even a write that failed after changing hardware");

        var offlineStop = new Fixture(); await offlineStop.Session.StartAsync(AlwaysOn());
        writes = offlineStop.Controller.Writes.Count; offlineStop.Controller.Connected = false;
        await offlineStop.Session.StopAsync();
        Check(!offlineStop.Prefs.AppSchedule.Enabled && offlineStop.Session.HasSchedule && Same(offlineStop.Durable.ScheduleRestore, 3, 4, 2), "offline stop durably disables schedule and keeps pending restore");
        saves = offlineStop.SaveCalls;
        await offlineStop.Session.TickAsync(LocalAt(12, 0));
        Check(offlineStop.Controller.Writes.Count == writes && offlineStop.SaveCalls == saves, "disconnected pending tick performs no write or save");
        offlineStop.Controller.Connected = true;
        await offlineStop.Session.TickAsync(LocalAt(12, 0));
        Check(Current(offlineStop.Controller, 3, 4, 2) && !offlineStop.Session.HasSchedule && offlineStop.Durable.ScheduleRestore == null, "reconnect restores disabled pending session instead of resuming overrides");

        var restoreFailure = new Fixture(); await restoreFailure.Session.StartAsync(AlwaysOn()); restoreFailure.Controller.FailWrites = 1;
        await Reject(() => restoreFailure.Session.StopAsync(), "failed restore retains pending recovery");
        Check(!restoreFailure.Prefs.AppSchedule.Enabled && Same(restoreFailure.Prefs.ScheduleRestore, 3, 4, 2) && Same(restoreFailure.Durable.ScheduleRestore, 3, 4, 2), "restore failure cannot lose original settings");
        await restoreFailure.Session.TickAsync(LocalAt(12, 0));
        Check(Current(restoreFailure.Controller, 3, 4, 2) && !restoreFailure.Session.HasSchedule, "disabled tick retries exact failed restore");
        var disableFailure = new Fixture(); await disableFailure.Session.StartAsync(AlwaysOn());
        writes = disableFailure.Controller.Writes.Count; disableFailure.FailOnSave = disableFailure.SaveCalls + 1;
        await Reject(() => disableFailure.Session.StopAsync(), "stop persistence failure blocks hardware restore");
        Check(disableFailure.Controller.Writes.Count == writes && Same(disableFailure.Prefs.ScheduleRestore, 3, 4, 2) && Same(disableFailure.Durable.ScheduleRestore, 3, 4, 2), "failed stop save leaves durable recovery intact");
        disableFailure.FailOnSave = 0;
        await disableFailure.Session.TickAsync(LocalAt(12, 0));
        Check(Current(disableFailure.Controller, 3, 4, 2) && !disableFailure.Session.HasSchedule, "next tick durably completes previously unsaved stop");
        var clearFailure = new Fixture(); await clearFailure.Session.StartAsync(AlwaysOn());
        clearFailure.FailOnSave = clearFailure.SaveCalls + 2;
        await Reject(() => clearFailure.Session.StopAsync(), "failed clear-marker save reports pending recovery");
        Check(Current(clearFailure.Controller, 3, 4, 2) && Same(clearFailure.Prefs.ScheduleRestore, 3, 4, 2) && Same(clearFailure.Durable.ScheduleRestore, 3, 4, 2), "clear-save failure retains exact retry baseline even after successful hardware restore");
        clearFailure.FailOnSave = 0;
        await clearFailure.Session.TickAsync(LocalAt(12, 0));
        Check(!clearFailure.Session.HasSchedule && clearFailure.Durable.ScheduleRestore == null, "retry clears durable recovery only after successful restore and save");

        var quit = new Fixture(); await quit.Session.StartAsync(AlwaysOn());
        quit.Prefs.AppSchedule.OnMinutes = 5; quit.Prefs.AppSchedule.OffMinutes = 10;
        await quit.Session.StopAsync(false);
        Check(quit.Prefs.AppSchedule.Enabled && quit.Prefs.ScheduleRestore == null && quit.Durable.AppSchedule.Enabled && quit.Durable.ScheduleRestore == null, "quit restores controller but preserves enabled preference for relaunch");
        Check(Current(quit.Controller, 3, 4, 2), "quit restores original hardware programming");
        var relaunched = new Fixture(quit.Durable);
        await relaunched.Session.TickAsync(LocalAt(0, 6));
        Check(relaunched.Controller.Reads == 1 && Same(relaunched.Prefs.ScheduleRestore, 3, 4, 2), "relaunch captures a fresh baseline after graceful quit");
        Check(relaunched.Controller.Writes.Count == 1 && Current(relaunched.Controller, 1, 8, 1), "relaunch resumes current off phase without replaying a missed on phase");
        Check(Same(relaunched.Durable.ScheduleRestore, 3, 4, 2), "relaunch override has a durable recovery point");

        var wrongAddress = new Fixture(); await wrongAddress.Session.StartAsync(AlwaysOn());
        writes = wrongAddress.Controller.Writes.Count; saves = wrongAddress.SaveCalls; int reads = wrongAddress.Controller.Reads;
        wrongAddress.Prefs.address = "11:22:33:44:55:66";
        await Reject(() => wrongAddress.Session.TickAsync(LocalAt(12, 0)), "tick rejects controller ownership mismatch");
        await Reject(() => wrongAddress.Session.StopAsync(), "stop cannot restore presets onto another controller");
        await Reject(() => wrongAddress.Session.StartAsync(AlwaysOn()), "start cannot overwrite another controller's pending restore");
        Check(wrongAddress.Controller.Writes.Count == writes && wrongAddress.SaveCalls == saves && wrongAddress.Controller.Reads == reads && wrongAddress.Prefs.AppSchedule.Enabled, "ownership mismatch causes no writes, persistence, or ownership changes");
        wrongAddress.Prefs.address = "aa:bb:cc:dd:ee:ff";
        await wrongAddress.Session.TickAsync(LocalAt(12, 0));
        Check(wrongAddress.Controller.Writes.Count == writes, "controller identity comparison is case insensitive");
        var missingBinding = new Fixture(); missingBinding.Prefs.AppSchedule = AlwaysOn();
        await Reject(() => missingBinding.Session.TickAsync(LocalAt(12, 0)), "enabled schedule without saved ownership cannot capture a new controller");
        Check(missingBinding.SaveCalls == 0 && missingBinding.Controller.Reads == 0 && missingBinding.Controller.Writes.Count == 0, "missing ownership is never silently rebound");

        var first = new Fixture(); var second = new Fixture(); second.Prefs.address = "11:22:33:44:55:66";
        second.Controller.State.Mode = 6; second.Controller.State.On = 10; second.Controller.State.Off = 0;
        var secondPlan = AlwaysOn(); secondPlan.RunSpeed = 4; secondPlan.IdleSpeed = 0;
        await first.Session.StartAsync(AlwaysOn()); await second.Session.StartAsync(secondPlan);
        Check(Current(first.Controller, 2, 8, 1) && Current(second.Controller, 2, 4, 0), "independent controllers retain different running presets");
        Check(Same(first.Prefs.ScheduleRestore, 3, 4, 2) && Same(second.Prefs.ScheduleRestore, 6, 10, 0), "independent controllers preserve their own original modes");
        first.Controller.Connected = false; await first.Session.StopAsync();
        await second.Session.TickAsync(LocalAt(12, 0));
        Check(second.Prefs.AppSchedule.Enabled && Current(second.Controller, 2, 4, 0) && first.Prefs.ScheduleRestore != null, "disconnected stop on one session cannot stop the other");
        await second.Session.StopAsync();
        Check(Current(second.Controller, 6, 10, 0) && !second.Session.HasSchedule && first.Session.HasSchedule, "restoring one controller leaves the other's pending recovery intact");
        first.Controller.Connected = true; await first.Session.TickAsync(LocalAt(12, 0));
        Check(Current(first.Controller, 3, 4, 2) && !first.Session.HasSchedule, "independent pending restore completes on its own reconnect");

        var equalSpeeds = new Fixture(); var equalPlan = AlwaysOn(); equalPlan.OnMinutes = 5; equalPlan.OffMinutes = 10; equalPlan.RunSpeed = equalPlan.IdleSpeed = 2;
        await equalSpeeds.Session.StartAsync(equalPlan);
        await equalSpeeds.Session.TickAsync(LocalAt(0, 1));
        Check(Current(equalSpeeds.Controller, 2, 2, 2), "on phase selects on mode even when preset speeds are equal");
        await equalSpeeds.Session.TickAsync(LocalAt(0, 6));
        Check(Current(equalSpeeds.Controller, 1, 2, 2), "off phase selects off mode even when idle equals run speed");
        var disconnectedSave = new Fixture(); disconnectedSave.OnSave = call => { if (call == 2) disconnectedSave.Controller.Connected = false; };
        await disconnectedSave.Session.StartAsync(AlwaysOn());
        Check(disconnectedSave.Controller.Writes.Count == 0 && Same(disconnectedSave.Durable.ScheduleRestore, 3, 4, 2), "disconnect during persistence cannot authorize a stale connected write");
        disconnectedSave.Controller.Connected = true;
        await disconnectedSave.Session.TickAsync(LocalAt(12, 0));
        Check(Current(disconnectedSave.Controller, 2, 8, 1) && disconnectedSave.Controller.Reads == 1, "reconnect uses preserved baseline after pre-write disconnect");
        var invalidBaseline = new Fixture(); invalidBaseline.Prefs.AppSchedule = AlwaysOn(); invalidBaseline.Prefs.ScheduleAddress = invalidBaseline.Prefs.address;
        invalidBaseline.Prefs.ScheduleRestore = new ControllerPresetSnapshot { Mode = 0, On = 4, Off = 2 };
        await Reject(() => invalidBaseline.Session.TickAsync(LocalAt(12, 0)), "malformed retained baseline blocks schedule override");
        Check(invalidBaseline.Controller.Writes.Count == 0, "malformed recovery point cannot authorize hardware changes");
        return passed;
    }

    private static AppFanSchedule AlwaysOn() => new() { Enabled = true, StartMinute = 0, EndMinute = 0, OnMinutes = 1440, OffMinutes = 1, RunSpeed = 8, IdleSpeed = 1 };

    private sealed class Fixture
    {
        internal readonly Preferences Prefs;
        internal readonly FakeController Controller;
        internal readonly AppScheduleSession Session;
        internal readonly List<string> Events = new();
        internal int SaveCalls, FailOnSave;
        internal Action<int>? OnSave;
        private string durableJson;
        internal Preferences Durable => JsonSerializer.Deserialize<Preferences>(durableJson)!;
        internal Fixture(Preferences? preferences = null)
        {
            Prefs = preferences ?? new Preferences { address = "AA:BB:CC:DD:EE:FF" };
            durableJson = JsonSerializer.Serialize(Prefs);
            Controller = new FakeController(Events);
            Controller.BeforeWrite = () =>
            {
                var durable = Durable;
                if (durable.ScheduleRestore is not { IsValid: true } || !string.Equals(durable.ScheduleAddress, Prefs.address, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Hardware write attempted without durable controller recovery data.");
            };
            Session = new AppScheduleSession(Prefs, Controller, Save);
        }
        internal void CommitInitial() => durableJson = JsonSerializer.Serialize(Prefs);
        private void Save()
        {
            SaveCalls++; Events.Add("save");
            if (SaveCalls == FailOnSave) throw new IOException("Injected preference save failure.");
            durableJson = JsonSerializer.Serialize(Prefs);
            OnSave?.Invoke(SaveCalls);
        }
    }

    private sealed class FakeController : IScheduleController
    {
        private readonly List<string> events;
        internal FakeController(List<string> events) { this.events = events; }
        public bool Connected { get; set; } = true;
        public ControllerState State { get; } = new() { Mode = 3, On = 4, Off = 2, Output = 4 };
        internal readonly List<ControllerPresetSnapshot> Writes = new();
        internal int Reads, FailWrites;
        internal bool FailRead, ApplyBeforeFailure;
        internal Action? OnRead, BeforeWrite;
        public Task ReadProgramsAsync()
        {
            Reads++; events.Add("read");
            if (FailRead) throw new IOException("Injected fresh-read failure.");
            OnRead?.Invoke();
            return Task.CompletedTask;
        }
        public Task SetPresetsAsync(ControllerPresetSnapshot snapshot)
        {
            if (!Connected) throw new InvalidOperationException("Write attempted while disconnected.");
            BeforeWrite?.Invoke(); events.Add("write");
            Writes.Add(new ControllerPresetSnapshot { Mode = snapshot.Mode, On = snapshot.On, Off = snapshot.Off });
            bool failing = FailWrites > 0;
            if (!failing || ApplyBeforeFailure) { State.Mode = snapshot.Mode; State.On = snapshot.On; State.Off = snapshot.Off; }
            if (failing) { FailWrites--; throw new IOException("Injected controller write failure."); }
            return Task.CompletedTask;
        }
    }
}
