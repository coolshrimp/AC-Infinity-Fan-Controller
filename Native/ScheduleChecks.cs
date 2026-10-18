using System;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace ACInfinityDesktop;

internal static class ScheduleChecks
{
    public static int Run()
    {
        int passed = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException($"Schedule test {passed + 1} failed: {description}");
            passed++;
        }
        static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, TimeSpan.Zero);
        static ScheduleDecision Decide(AppFanSchedule plan, DateTimeOffset instant) => plan.EvaluateInZone(instant, TimeZoneInfo.Utc);
        var schedule = new AppFanSchedule { Enabled = true, StartMinute = 480, EndMinute = 1200, OnMinutes = 5, OffMinutes = 10, RunSpeed = 7 };
        Check(schedule.Validate() == "", "default settings validate");
        var decision = Decide(schedule, At(18, 7, 59, 59));
        Check(!decision.WithinWindow && decision.Speed == 0 && decision.NextChange == At(18, 8, 0), "before daily start");
        decision = Decide(schedule, At(18, 8, 0));
        Check(decision.WithinWindow && decision.Speed == 7 && decision.NextChange == At(18, 8, 5), "start is inclusive and on first");
        decision = Decide(schedule, At(18, 8, 4, 59));
        Check(decision.Speed == 7 && decision.NextChange == At(18, 8, 5), "on phase includes its last second");
        decision = Decide(schedule, At(18, 8, 5));
        Check(decision.Speed == 0 && decision.WithinWindow && decision.NextChange == At(18, 8, 15), "off phase begins exactly at boundary");
        decision = Decide(schedule, At(18, 8, 15));
        Check(decision.Speed == 7 && decision.NextChange == At(18, 8, 20), "cycle repeats");
        decision = Decide(schedule, At(18, 19, 59));
        Check(decision.Speed == 0 && decision.NextChange == At(18, 20, 0), "window end limits off phase");
        decision = Decide(schedule, At(18, 20, 0));
        Check(!decision.WithinWindow && decision.Speed == 0 && decision.NextChange == At(19, 8, 0), "end is exclusive");
        Check(Decide(schedule, At(18, 10, 8)).Speed == 0, "restart during off does not replay on");
        Check(Decide(schedule, At(18, 10, 17)).Speed == 7, "reconnect derives current on phase");
        decision = Decide(schedule, At(18, 8, 4, 59).AddMilliseconds(999));
        Check(decision.NextChange == At(18, 8, 5), "sub-second precision does not delay boundary");

        schedule.OnMinutes = 7; schedule.OffMinutes = 2;
        Check(Decide(schedule, At(18, 8, 6)).Speed == 7, "unequal on duration");
        decision = Decide(schedule, At(18, 8, 7));
        Check(decision.Speed == 0 && decision.NextChange == At(18, 8, 9), "unequal off duration");
        Check(Decide(schedule, At(18, 8, 9)).Speed == 7, "unequal cycle restarts");
        schedule.IdleSpeed = 2;
        Check(Decide(schedule, At(18, 7, 30)).Speed == 2 && Decide(schedule, At(18, 8, 8)).Speed == 2, "idle speed applies inside and outside window");

        schedule.StartMinute = 1320; schedule.EndMinute = 120; schedule.OnMinutes = 10; schedule.OffMinutes = 20; schedule.IdleSpeed = 0;
        Check(!Decide(schedule, At(18, 21, 59)).WithinWindow, "overnight window before start");
        Check(Decide(schedule, At(18, 22, 0)).Speed == 7, "overnight starts on");
        decision = Decide(schedule, At(19, 0, 5));
        Check(decision.WithinWindow && decision.Speed == 7 && decision.NextChange == At(19, 0, 10), "overnight continues across midnight");
        decision = Decide(schedule, At(19, 0, 10));
        Check(decision.WithinWindow && decision.Speed == 0 && decision.NextChange == At(19, 0, 30), "overnight off phase");
        decision = Decide(schedule, At(19, 2, 0));
        Check(!decision.WithinWindow && decision.NextChange == At(19, 22, 0), "overnight end is exclusive");

        schedule.StartMinute = schedule.EndMinute = 480; schedule.OnMinutes = 7; schedule.OffMinutes = 10;
        Check(Decide(schedule, At(18, 7, 59)).WithinWindow, "equal start and end means all day");
        decision = Decide(schedule, At(18, 8, 0));
        Check(decision.Speed == 7 && decision.NextChange == At(18, 8, 7), "all-day cycle resets at daily anchor");
        Check(Decide(schedule, At(18, 0, 0)).WithinWindow, "all-day includes midnight");
        schedule.OnMinutes = 1440; schedule.OffMinutes = 1440;
        decision = Decide(schedule, At(18, 16, 0));
        Check(decision.Speed == 7 && decision.NextChange == At(19, 8, 0), "long on phase is limited by daily reset");
        schedule.StartMinute = 480; schedule.EndMinute = 481;
        decision = Decide(schedule, At(18, 8, 0));
        Check(decision.NextChange == At(18, 8, 1), "short window clips long phase");
        schedule.Enabled = false;
        decision = Decide(schedule, At(18, 8, 0));
        Check(decision.Speed == 0 && !decision.WithinWindow && decision.NextChange == DateTimeOffset.MaxValue, "disabled has no scheduled wakeup");
        schedule.Enabled = true;

        foreach (var invalid in new AppFanSchedule[]
        {
            new() { StartMinute = -1 }, new() { StartMinute = 1440 },
            new() { EndMinute = -1 }, new() { EndMinute = 1440 },
            new() { OnMinutes = 0 }, new() { OnMinutes = -1 }, new() { OnMinutes = int.MaxValue },
            new() { OffMinutes = 0 }, new() { OffMinutes = -1 }, new() { OffMinutes = 1441 },
            new() { RunSpeed = 0 }, new() { RunSpeed = 11 }, new() { IdleSpeed = -1 }, new() { IdleSpeed = 11 }
        })
        {
            Check(invalid.Validate().Length > 0, "invalid field rejected");
            bool rejected = false;
            try { Decide(invalid, At(18, 8, 0)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "invalid evaluation cannot produce a command");
        }
        var saved = new AppFanSchedule { Enabled = true, StartMinute = 1380, EndMinute = 180, OnMinutes = 13, OffMinutes = 29, RunSpeed = 10, IdleSpeed = 1 };
        var restored = JsonSerializer.Deserialize<AppFanSchedule>(JsonSerializer.Serialize(saved))!;
        Check(restored.Enabled && restored.StartMinute == 1380 && restored.EndMinute == 180 && restored.OnMinutes == 13 && restored.OffMinutes == 29 && restored.RunSpeed == 10 && restored.IdleSpeed == 1, "serialization preserves schedule");
        Check(Decide(saved, At(19, 1, 30)) == Decide(restored, At(19, 1, 30)), "serialization preserves decision");
        Check(new AppFanSchedule().Evaluate(At(18, 8, 0)).NextChange == DateTimeOffset.MaxValue, "public evaluator supports disabled settings");
        saved.Enabled = true;
        var instant = At(19, 1, 30);
        Check(saved.Evaluate(instant) == saved.EvaluateInZone(instant, TimeZoneInfo.Local), "public evaluator follows PC local zone");
        saved.StartMinute = 0; saved.EndMinute = 1439; saved.OnMinutes = 1; saved.OffMinutes = 1;
        foreach (int year in new[] { 2000, 2026, 2099 })
        {
            var time = new DateTimeOffset(year, 2, 28, 23, 58, 59, TimeSpan.Zero);
            var next = Decide(saved, time).NextChange;
            Check(next > time && next - time <= TimeSpan.FromDays(1), "long calendar range has a future, bounded wakeup");
        }

        // Test both sides of the repeated hour without depending on this machine's zone.
        var zone = TimeZoneInfo.CreateCustomTimeZone("Schedule checks Eastern", TimeSpan.FromHours(-5), "Test Eastern", "Test Standard", "Test Daylight", new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020, 1, 1), new DateTime(2035, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday))
        });
        schedule.StartMinute = schedule.EndMinute = 0; schedule.OnMinutes = 5; schedule.OffMinutes = 10;
        var spring = new DateTimeOffset(2026, 3, 8, 1, 59, 0, TimeSpan.FromHours(-5));
        decision = schedule.EvaluateInZone(spring, zone);
        Check(decision.Speed == 0 && decision.NextChange == spring.AddMinutes(1), "spring gap wakes at clock transition");
        Check(schedule.EvaluateInZone(spring.AddMinutes(1), zone).Speed == 7, "spring jump derives current phase");
        var fall = new DateTimeOffset(2026, 11, 1, 1, 59, 0, TimeSpan.FromHours(-4));
        decision = schedule.EvaluateInZone(fall, zone);
        Check(decision.NextChange == fall.AddMinutes(1), "fall-back wakes before projected wall boundary");
        Check(schedule.EvaluateInZone(fall.AddMinutes(1), zone).Speed == 7, "repeated hour derives current phase");
        foreach (int offset in new[] { -4, -5 })
        {
            var repeated = new DateTimeOffset(2026, 11, 1, 1, 1, 0, TimeSpan.FromHours(offset));
            decision = schedule.EvaluateInZone(repeated, zone);
            Check(decision.Speed == 7 && decision.NextChange == repeated.AddMinutes(4), "ambiguous phase selects its next future instant");
        }
        schedule.StartMinute = 135; schedule.EndMinute = 240;
        decision = schedule.EvaluateInZone(spring, zone);
        Check(!decision.WithinWindow && decision.NextChange == spring.AddMinutes(1), "skipped start resumes at first valid local instant");
        Check(schedule.EvaluateInZone(spring.AddMinutes(1), zone).WithinWindow, "skipped daily start opens window after clock jump");
        var cycleProgram = new ControllerProgram { Mode = 6, OnMinutes = 13, OffMinutes = 29, RunSpeed = 8, IdleSpeed = 1 };
        var cycleFields = ControllerPrograms.ProgramFields(cycleProgram);
        byte[] cyclePayload = ControllerPrograms.Encode(cycleFields);
        Check(Convert.ToHexString(cyclePayload) == "11010112010816080000030C000006CC100106", "cycle converts minutes to big-endian seconds, presets before mode");
        Check(cycleFields.Keys.SequenceEqual(new byte[] { 17, 18, 22, 16 }), "cycle field order selects mode last");
        var dailyProgram = new ControllerProgram { Mode = 7, StartMinute = 1425, EndMinute = 377, RunSpeed = 10, IdleSpeed = 0 };
        var dailyFields = ControllerPrograms.ProgramFields(dailyProgram);
        byte[] dailyPayload = ControllerPrograms.Encode(dailyFields);
        Check(Convert.ToHexString(dailyPayload) == "11010012010A1704172D0611100107", "daily schedule encodes hour and minute pairs including overnight");
        Check(dailyFields.Keys.SequenceEqual(new byte[] { 17, 18, 23, 16 }), "daily field order selects mode last");
        var longestCycle = new ControllerProgram { Mode = 6, OnMinutes = 1440, OffMinutes = 1, RunSpeed = 1, IdleSpeed = 10 };
        Check(Convert.ToHexString(ControllerPrograms.ProgramFields(longestCycle)[22]) == "000151800000003C", "maximum cycle duration preserves all seconds bytes");
        foreach (var invalidProgram in new ControllerProgram[]
        {
            new() { Mode = 0 }, new() { Mode = 8 },
            new() { StartMinute = -1 }, new() { StartMinute = 1440 },
            new() { EndMinute = -1 }, new() { EndMinute = 1440 },
            new() { StartMinute = 600, EndMinute = 600 },
            new() { Mode = 6, OnMinutes = 0 }, new() { Mode = 6, OnMinutes = 1441 },
            new() { Mode = 6, OffMinutes = -1 }, new() { Mode = 6, OffMinutes = int.MaxValue },
            new() { RunSpeed = 11 }, new() { IdleSpeed = -1 }, new() { IdleSpeed = 11 }
        })
        {
            Check(invalidProgram.Validate().Length > 0, "invalid controller program rejected");
            bool rejected = false;
            try { ControllerPrograms.ProgramFields(invalidProgram); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid controller program cannot be encoded");
        }
        foreach (var fields in new[] { cycleFields, dailyFields })
        {
            var readback = Protocol.Fields(ControllerPrograms.Encode(fields));
            ControllerPrograms.Verify(fields, readback);
            Check(true, "complete readback confirms the programmed values");
            foreach (byte key in fields.Keys)
            {
                var missing = new Dictionary<byte, byte[]>(readback); missing.Remove(key);
                bool rejectedMissing = false;
                try { ControllerPrograms.Verify(fields, missing); } catch (InvalidDataException) { rejectedMissing = true; }
                Check(rejectedMissing, "readback missing any changed field fails");
                var changed = readback.ToDictionary(p => p.Key, p => (byte[])p.Value.Clone()); changed[key][^1] ^= 1;
                bool rejectedChanged = false;
                try { ControllerPrograms.Verify(fields, changed); } catch (InvalidDataException) { rejectedChanged = true; }
                Check(rejectedChanged, "readback rejecting any changed value fails");
            }
            readback[99] = new byte[] { 1, 2 };
            ControllerPrograms.Verify(fields, readback);
            Check(true, "additional readback fields do not invalidate confirmed settings");
            byte[] accepted = fields.Keys.SelectMany(key => new byte[] { key, 0 }).ToArray();
            Protocol.ValidateAck(accepted, fields.Keys.ToArray());
            Check(true, "acknowledgment accepts every changed program field");
            accepted[1] = 1;
            bool rejectedAck = false;
            try { Protocol.ValidateAck(accepted, fields.Keys.ToArray()); } catch (InvalidDataException) { rejectedAck = true; }
            Check(rejectedAck, "rejected program acknowledgment fails");

            // Program frames exceed the minimum ATT payload. Verify that framing and TLVs
            // survive the transport split, including a value crossing the first chunk.
            byte[] frame = Protocol.Request(3, 17, ControllerPrograms.Encode(fields));
            Check(frame.Length > 20, "program request spans multiple minimum-MTU writes");
            byte[][] fragments = Enumerable.Range(0, (frame.Length + 19) / 20)
                .Select(index => frame.Skip(index * 20).Take(20).ToArray()).ToArray();
            Check(fragments.Length == 2 && fragments[0].Length == 20 && fragments.All(part => part.Length is > 0 and <= 20), "complete frame fits bounded transport fragments");
            byte[] joined = fragments.SelectMany(part => part).ToArray();
            Check(joined.SequenceEqual(frame), "transport fragments preserve trailing mode and checksum");
            // Turn the reconstructed request into a valid reply fixture by updating the header CRC.
            joined[1] = 0x10;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(joined.AsSpan(6), Protocol.Crc(joined.AsSpan(0, 6)));
            ControllerPrograms.Verify(fields, Protocol.Fields(Protocol.Response(joined, 17, 3)));
            Check(true, "reassembled program frame passes checksum and setting readback");
            bool rejectedTruncated = false;
            try { Protocol.Response(joined[..20], 17, 3); } catch (InvalidDataException) { rejectedTruncated = true; }
            Check(rejectedTruncated, "first fragment alone cannot masquerade as a full readback");
        }
        for (int mode = 1; mode <= 7; mode++)
        foreach (int on in new[] { 0, 10 })
        foreach (int off in new[] { 0, 10 })
        {
            var snapshot = ControllerPresetSnapshot.Capture(new ControllerState { Mode = mode, On = on, Off = off });
            Check(snapshot.IsValid && snapshot.Mode == mode && snapshot.On == on && snapshot.Off == off, "snapshot preserves all supported modes and preset endpoints");
            var restoreFields = Protocol.Fields(ControllerPrograms.Presets(snapshot));
            Check(restoreFields.Keys.SequenceEqual(new byte[] { 17, 18, 16 }) && restoreFields[17][0] == off && restoreFields[18][0] == on && restoreFields[16][0] == mode, "restore writes exact saved presets with mode last");
        }
        Check(Convert.ToHexString(ControllerPrograms.Presets(new ControllerPresetSnapshot { Mode = 3, On = 0, Off = 10 })) == "11010A120100100103", "zero on-preset is preserved instead of forced to a nonzero speed");
        foreach (var invalidSnapshot in new ControllerPresetSnapshot[]
        {
            new() { Mode = 0 }, new() { Mode = 8 }, new() { Mode = 2, On = -1 },
            new() { Mode = 2, On = 11 }, new() { Mode = 3, Off = -1 }, new() { Mode = 3, Off = 11 }
        })
        {
            Check(!invalidSnapshot.IsValid, "invalid restore snapshot identified");
            bool rejectedRestore = false;
            try { ControllerPrograms.Presets(invalidSnapshot); } catch (ArgumentException) { rejectedRestore = true; }
            Check(rejectedRestore, "invalid restore cannot produce a command");
            bool rejectedCapture = false;
            try { ControllerPresetSnapshot.Capture(new ControllerState { Mode = invalidSnapshot.Mode, On = invalidSnapshot.On, Off = invalidSnapshot.Off }); } catch (InvalidDataException) { rejectedCapture = true; }
            Check(rejectedCapture, "unsupported live state cannot be captured as a restore point");
        }

        string preferencesDirectory = Path.Combine(Path.GetTempPath(), "aci-schedule-settings-" + Guid.NewGuid().ToString("N"));
        string preferencesPath = Path.Combine(preferencesDirectory, "preferences.json");
        Directory.CreateDirectory(preferencesDirectory);
        try
        {
            var preferences = new Preferences
            {
                address = "AA:BB:CC:DD:EE:FF", ScheduleAddress = "AA:BB:CC:DD:EE:FF",
                AppSchedule = new AppFanSchedule { Enabled = true, StartMinute = 1320, EndMinute = 120, OnMinutes = 13, OffMinutes = 29, RunSpeed = 8, IdleSpeed = 1 },
                ScheduleRestore = new ControllerPresetSnapshot { Mode = 7, On = 0, Off = 10 }
            };
            preferences.Save(preferencesPath);
            var reopened = Preferences.Read(preferencesPath);
            Check(reopened.AppSchedule.Enabled && reopened.ScheduleAddress == preferences.ScheduleAddress, "saved app schedule remains tied to its controller");
            Check(reopened.AppSchedule.StartMinute == 1320 && reopened.AppSchedule.EndMinute == 120 && reopened.AppSchedule.OnMinutes == 13 && reopened.AppSchedule.OffMinutes == 29 && reopened.AppSchedule.RunSpeed == 8 && reopened.AppSchedule.IdleSpeed == 1, "preferences retain overnight schedule and cycle settings");
            Check(reopened.ScheduleRestore is { Mode: 7, On: 0, Off: 10 }, "preferences preserve original mode and zero on-preset");
            Check(!File.Exists(preferencesPath + ".tmp"), "successful preference replacement leaves no partial schedule file");
            File.WriteAllText(preferencesPath, "{\"AppSchedule\":null,\"ScheduleAddress\":null,\"ScheduleRestore\":null}");
            reopened = Preferences.Read(preferencesPath);
            Check(reopened.AppSchedule != null && !reopened.AppSchedule.Enabled && reopened.AppSchedule.Validate() == "" && reopened.ScheduleAddress == "" && reopened.ScheduleRestore == null, "null scheduling properties normalize to disabled defaults");
            File.WriteAllText(preferencesPath, "{\"AppSchedule\":{\"Enabled\":true,\"OnMinutes\":0},\"ScheduleAddress\":\"AA:BB:CC:DD:EE:FF\",\"ScheduleRestore\":{\"Mode\":2,\"On\":5,\"Off\":0}}");
            reopened = Preferences.Read(preferencesPath);
            Check(!reopened.AppSchedule.Enabled && reopened.AppSchedule.Validate() == "", "invalid saved cycle is replaced with disabled defaults");
            File.WriteAllText(preferencesPath, "{\"AppSchedule\":{\"Enabled\":true},\"ScheduleAddress\":\"\",\"ScheduleRestore\":{\"Mode\":2,\"On\":5,\"Off\":0}}");
            Check(!Preferences.Read(preferencesPath).AppSchedule.Enabled, "missing saved controller disables automatic schedule");
            foreach (string invalidRestore in new[] { "{\"Mode\":0,\"On\":5,\"Off\":0}", "{\"Mode\":2,\"On\":11,\"Off\":0}", "{\"Mode\":3,\"On\":5,\"Off\":-1}" })
            {
                File.WriteAllText(preferencesPath, "{\"AppSchedule\":{\"Enabled\":true},\"ScheduleAddress\":\"AA:BB:CC:DD:EE:FF\",\"ScheduleRestore\":" + invalidRestore + "}");
                reopened = Preferences.Read(preferencesPath);
                Check(!reopened.AppSchedule.Enabled && reopened.ScheduleRestore == null, "invalid saved restore disables schedule instead of commanding malformed presets");
            }
            File.WriteAllText(preferencesPath, "{\"AppSchedule\":{\"Enabled\":true},\"ScheduleRestore\":\"broken\"}");
            Check(!Preferences.Read(preferencesPath).AppSchedule.Enabled, "malformed restore JSON cannot enable a schedule");
            File.WriteAllText(preferencesPath, "{\"AppSchedule\":");
            Check(!Preferences.Read(preferencesPath).AppSchedule.Enabled, "partial preference JSON cannot enable a schedule");
        }
        finally
        {
            if (File.Exists(preferencesPath)) File.Delete(preferencesPath);
            if (File.Exists(preferencesPath + ".tmp")) File.Delete(preferencesPath + ".tmp");
            Directory.Delete(preferencesDirectory);
        }
        return passed;
    }
}
