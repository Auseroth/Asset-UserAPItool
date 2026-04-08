using LdapCloudSync.Core.Models;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Evaluates schedule entries to determine when the next sync should run.
/// Used by the service worker to calculate sleep durations.
/// </summary>
public static class ScheduleEvaluator
{
    /// <summary>
    /// Calculates the next run time for a schedule entry after the given time.
    /// Returns null if the schedule cannot be evaluated (e.g., invalid cron).
    /// </summary>
    public static DateTime? GetNextRunTime(ScheduleEntry entry, DateTime afterUtc)
    {
        return entry.Type switch
        {
            ScheduleType.Interval => afterUtc.Add(entry.Interval),
            ScheduleType.DailyAt => GetNextDailyAt(entry, afterUtc),
            ScheduleType.Cron => CronParser.GetNextOccurrence(entry.CronExpression, afterUtc),
            _ => null
        };
    }

    /// <summary>
    /// Calculates the delay from now until the next run time.
    /// Returns a minimum of 1 minute to prevent tight loops.
    /// </summary>
    public static TimeSpan GetDelayUntilNextRun(ScheduleEntry entry, DateTime nowUtc)
    {
        var nextRun = GetNextRunTime(entry, nowUtc);
        if (nextRun is null)
            return TimeSpan.FromHours(1); // Fallback if schedule is invalid

        var delay = nextRun.Value - nowUtc;
        return delay < TimeSpan.FromMinutes(1)
            ? TimeSpan.FromMinutes(1)
            : delay;
    }

    private static DateTime? GetNextDailyAt(ScheduleEntry entry, DateTime afterUtc)
    {
        // Convert to local time for daily-at evaluation since users think in local time
        var afterLocal = afterUtc.ToLocalTime();
        var targetTime = entry.DailyAtTime;

        // Try today first
        var candidate = new DateTime(afterLocal.Year, afterLocal.Month, afterLocal.Day,
            targetTime.Hour, targetTime.Minute, 0, DateTimeKind.Local);

        // If today's time has passed, start from tomorrow
        if (candidate <= afterLocal)
            candidate = candidate.AddDays(1);

        // Find the next matching day of week (search up to 8 days)
        for (int i = 0; i < 8; i++)
        {
            if (entry.DaysOfWeek.Contains(candidate.DayOfWeek))
                return candidate.ToUniversalTime();

            candidate = candidate.AddDays(1);
        }

        return null;
    }
}