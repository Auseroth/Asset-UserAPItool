namespace LdapCloudSync.Core.Models;

public sealed class LoggingConfig
{
    /// <summary>
    /// Number of days to retain log files before auto-deletion.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Maximum size of a single log file in megabytes before rolling.
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>
    /// Minimum log level: Verbose, Debug, Information, Warning, Error, Fatal.
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";
}