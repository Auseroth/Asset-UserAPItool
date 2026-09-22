using LdapCloudSync.Core.Models;
using Serilog;
using Serilog.Events;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Configures Serilog with separate log files per activity, rolling daily, with auto-cleanup.
/// </summary>
public static class LoggingService
{
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Connexus", "Logs");

    /// <summary>
    /// Initializes the global Serilog logger with all configured sinks.
    /// Call once at application/service startup.
    /// </summary>
    public static void Initialize(LoggingConfig config)
    {
        Directory.CreateDirectory(LogDirectory);

        var level = Enum.TryParse<LogEventLevel>(config.MinimumLevel, true, out var parsed)
            ? parsed
            : LogEventLevel.Information;

        var fileSizeBytes = config.MaxFileSizeMb * 1024L * 1024L;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                path: Path.Combine(LogDirectory, "service-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: config.RetentionDays,
                fileSizeLimitBytes: fileSizeBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(LogDirectory, "errors-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: config.RetentionDays,
                fileSizeLimitBytes: fileSizeBytes,
                rollOnFileSizeLimit: true,
                restrictedToMinimumLevel: LogEventLevel.Error,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// Creates a dedicated logger for a specific sync activity (e.g., "sync-assets", "sync-users").
    /// Each gets its own rolling log file.
    /// </summary>
    public static ILogger CreateActivityLogger(string activityName, LoggingConfig config)
    {
        var fileSizeBytes = config.MaxFileSizeMb * 1024L * 1024L;

        return new LoggerConfiguration()
            .MinimumLevel.Is(
                Enum.TryParse<LogEventLevel>(config.MinimumLevel, true, out var level)
                    ? level
                    : LogEventLevel.Information)
            .WriteTo.File(
                path: Path.Combine(LogDirectory, $"{activityName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: config.RetentionDays,
                fileSizeLimitBytes: fileSizeBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static string GetLogDirectory() => LogDirectory;

    /// <summary>
    /// Flushes and closes all loggers. Call at shutdown.
    /// </summary>
    public static void Shutdown() => Log.CloseAndFlush();
}