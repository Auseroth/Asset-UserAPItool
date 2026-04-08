namespace LdapCloudSync.Core.Services;

/// <summary>
/// Lightweight cron expression parser supporting standard 5-field format:
/// minute hour dayOfMonth month dayOfWeek
/// Supports: numbers, ranges (1-5), lists (1,3,5), step values (*/15), and wildcard (*).
/// </summary>
public static class CronParser
{
    /// <summary>
    /// Calculates the next occurrence after the given time for a cron expression.
    /// Returns null if the expression is invalid.
    /// </summary>
    public static DateTime? GetNextOccurrence(string cronExpression, DateTime after)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return null;

        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return null;

        var minutes = ParseField(parts[0], 0, 59);
        var hours = ParseField(parts[1], 0, 23);
        var daysOfMonth = ParseField(parts[2], 1, 31);
        var months = ParseField(parts[3], 1, 12);
        var daysOfWeek = ParseField(parts[4], 0, 6);

        if (minutes is null || hours is null || daysOfMonth is null || months is null || daysOfWeek is null)
            return null;

        // Start from the next minute after "after"
        var candidate = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0)
            .AddMinutes(1);

        // Search up to 2 years ahead to prevent infinite loops
        var limit = after.AddYears(2);

        while (candidate < limit)
        {
            if (months.Contains(candidate.Month)
                && daysOfMonth.Contains(candidate.Day)
                && daysOfWeek.Contains((int)candidate.DayOfWeek)
                && hours.Contains(candidate.Hour)
                && minutes.Contains(candidate.Minute))
            {
                return candidate;
            }

            candidate = candidate.AddMinutes(1);

            // Optimization: skip entire hours/days when possible
            if (!hours.Contains(candidate.Hour) && candidate.Minute != 0)
            {
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day,
                    candidate.Hour, 0, 0).AddHours(1);
            }
            else if (!months.Contains(candidate.Month))
            {
                // Skip to first day of next month
                candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0).AddMonths(1);
            }
            else if (!daysOfMonth.Contains(candidate.Day) || !daysOfWeek.Contains((int)candidate.DayOfWeek))
            {
                // Skip to next day
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, 0, 0, 0).AddDays(1);
            }
        }

        return null;
    }

    /// <summary>
    /// Validates a cron expression. Returns null if valid, or an error message.
    /// </summary>
    public static string? Validate(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return "Cron expression cannot be empty.";

        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return $"Expected 5 fields (minute hour dayOfMonth month dayOfWeek), got {parts.Length}.";

        if (ParseField(parts[0], 0, 59) is null) return $"Invalid minute field: {parts[0]}";
        if (ParseField(parts[1], 0, 23) is null) return $"Invalid hour field: {parts[1]}";
        if (ParseField(parts[2], 1, 31) is null) return $"Invalid day-of-month field: {parts[2]}";
        if (ParseField(parts[3], 1, 12) is null) return $"Invalid month field: {parts[3]}";
        if (ParseField(parts[4], 0, 6) is null) return $"Invalid day-of-week field: {parts[4]}";

        return null;
    }

    private static HashSet<int>? ParseField(string field, int min, int max)
    {
        var result = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            // Step value: */2 or 1-10/3
            var stepParts = part.Split('/');
            int step = 1;

            if (stepParts.Length == 2)
            {
                if (!int.TryParse(stepParts[1], out step) || step < 1)
                    return null;
            }
            else if (stepParts.Length > 2)
            {
                return null;
            }

            var rangePart = stepParts[0];

            if (rangePart == "*")
            {
                for (int i = min; i <= max; i += step)
                    result.Add(i);
            }
            else if (rangePart.Contains('-'))
            {
                var rangeParts = rangePart.Split('-');
                if (rangeParts.Length != 2) return null;
                if (!int.TryParse(rangeParts[0], out var start) || !int.TryParse(rangeParts[1], out var end))
                    return null;
                if (start < min || end > max || start > end)
                    return null;
                for (int i = start; i <= end; i += step)
                    result.Add(i);
            }
            else
            {
                if (!int.TryParse(rangePart, out var val) || val < min || val > max)
                    return null;
                result.Add(val);
            }
        }

        return result.Count > 0 ? result : null;
    }
}