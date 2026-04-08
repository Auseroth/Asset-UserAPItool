namespace LdapCloudSync.Core.Models;

/// <summary>
/// Scheduling configuration for a cloud target.
/// </summary>
public sealed class ScheduleConfig
{
    /// <summary>
    /// When true, assets and users share the same schedule.
    /// When false, each has its own schedule.
    /// </summary>
    public bool CoupledSchedule { get; set; } = true;

    /// <summary>
    /// Shared schedule (used when CoupledSchedule = true).
    /// Also used as the Assets schedule when decoupled.
    /// </summary>
    public ScheduleEntry PrimarySchedule { get; set; } = new();

    /// <summary>
    /// Separate Users schedule (only used when CoupledSchedule = false).
    /// </summary>
    public ScheduleEntry UsersSchedule { get; set; } = new();
}

public sealed class ScheduleEntry
{
    public ScheduleType Type { get; set; } = ScheduleType.Interval;

    /// <summary>
    /// For Interval type: how often to sync.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// For Cron type: cron expression (e.g., "0 2 * * *" for daily at 2 AM).
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// For DailyAt type: time of day to run.
    /// </summary>
    public TimeOnly DailyAtTime { get; set; } = new(2, 0);

    /// <summary>
    /// For DailyAt type: which days to run on.
    /// </summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];
}

public enum ScheduleType
{
    /// <summary>Run every X hours/minutes.</summary>
    Interval,

    /// <summary>Run at a specific time on specific days.</summary>
    DailyAt,

    /// <summary>Full cron expression for advanced users.</summary>
    Cron
}