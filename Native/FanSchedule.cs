using System;
namespace ACInfinityDesktop;

/// <summary>Daily, local-clock fan cycling; restarting evaluates the current phase.</summary>
public sealed class AppFanSchedule
{
    public bool Enabled { get; set; }
    public int StartMinute { get; set; } = 480;
    public int EndMinute { get; set; } = 1200;
    public int OnMinutes { get; set; } = 5;
    public int OffMinutes { get; set; } = 10;
    public int RunSpeed { get; set; } = 5;
    public int IdleSpeed { get; set; }

    public string Validate()
    {
        if (StartMinute is < 0 or > 1439 || EndMinute is < 0 or > 1439)
            return "Start and end times must be valid times of day.";
        if (OnMinutes is < 1 or > 1440 || OffMinutes is < 1 or > 1440)
            return "On and off durations must each be between 1 and 1,440 minutes.";
        if (RunSpeed is < 1 or > 10)
            return "Run speed must be between 1 and 10.";
        if (IdleSpeed is < 0 or > 10)
            return "Idle speed must be between 0 and 10.";
        return "";
    }

    public ScheduleDecision Evaluate(DateTimeOffset now) => EvaluateInZone(now, TimeZoneInfo.Local);

    // The explicit zone keeps clock-change checks deterministic without changing the PC clock.
    internal ScheduleDecision EvaluateInZone(DateTimeOffset now, TimeZoneInfo zone)
    {
        string error = Validate();
        if (error.Length != 0) throw new InvalidOperationException(error);
        if (!Enabled) return new(IdleSpeed, false, DateTimeOffset.MaxValue, "Schedule disabled");

        DateTime local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(now, zone).DateTime, DateTimeKind.Unspecified);
        DateTime start = local.Date.AddMinutes(StartMinute);
        if (local < start) start = start.AddDays(-1);
        // Equal times explicitly select all day, restarting the cycle at the daily start time.
        DateTime end = start.Date.AddMinutes(EndMinute);
        if (EndMinute <= StartMinute) end = end.AddDays(1);
        bool within = local >= start && local < end;
        if (!within)
            return new(IdleSpeed, false, NextBoundary(start.AddDays(1), now, zone), "Outside daily window");

        long onTicks = TimeSpan.FromMinutes(OnMinutes).Ticks;
        long cycleTicks = TimeSpan.FromMinutes((long)OnMinutes + OffMinutes).Ticks;
        long phase = (local - start).Ticks % cycleTicks;
        bool running = phase < onTicks;
        long remaining = running ? onTicks - phase : cycleTicks - phase;
        DateTime next = local.AddTicks(remaining);
        if (next > end) next = end;
        return new(running ? RunSpeed : IdleSpeed, true, NextBoundary(next, now, zone), running ? "On phase" : "Off phase");
    }

    private static DateTimeOffset NextBoundary(DateTime wallTime, DateTimeOffset now, TimeZoneInfo zone)
    {
        // A spring-forward gap has no matching instant. Re-evaluate when the clock reaches
        // the first valid time instead of replaying any phases skipped by the clock jump.
        while (zone.IsInvalidTime(wallTime)) wallTime = wallTime.AddMinutes(1);
        DateTimeOffset next;
        if (zone.IsAmbiguousTime(wallTime))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(wallTime);
            var first = new DateTimeOffset(wallTime, offsets[0]);
            var second = new DateTimeOffset(wallTime, offsets[1]);
            next = first > now && (second <= now || first < second) ? first : second;
        }
        else next = new DateTimeOffset(wallTime, zone.GetUtcOffset(wallTime));
        if (next <= now) next = now.AddSeconds(1);

        // During fall-back the wall clock can cross a phase boundary before its originally
        // projected instant. Wake at the offset change and derive the new current phase.
        var cursor = now;
        var offset = zone.GetUtcOffset(now);
        while (cursor < next)
        {
            var probe = next - cursor > TimeSpan.FromHours(1) ? cursor.AddHours(1) : next;
            if (zone.GetUtcOffset(probe) != offset)
            {
                long low = cursor.UtcTicks, high = probe.UtcTicks;
                while (high - low > 1)
                {
                    long middle = low + (high - low) / 2;
                    if (zone.GetUtcOffset(new DateTimeOffset(middle, TimeSpan.Zero)) == offset) low = middle;
                    else high = middle;
                }
                return TimeZoneInfo.ConvertTime(new DateTimeOffset(high, TimeSpan.Zero), zone);
            }
            cursor = probe;
        }
        return next;
    }
}

public sealed record ScheduleDecision(int Speed, bool WithinWindow, DateTimeOffset NextChange, string Description);
