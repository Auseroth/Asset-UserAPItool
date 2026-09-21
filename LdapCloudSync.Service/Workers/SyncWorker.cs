using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Ipc;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Providers;
using LdapCloudSync.Core.Services;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LdapCloudSync.Service.Workers;

/// <summary>
/// Main background service that manages scheduled sync operations for all cloud targets.
/// Runs as a Windows Service. Also hosts the IPC server for app communication.
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private readonly ConfigService _configService;
    private readonly Serilog.ILogger _log;
    private IpcServer? _ipcServer;

    /// <summary>
    /// Tracks last run time per target+category so schedules are evaluated correctly.
    /// Key = "{targetId}:{category}"
    /// </summary>
    private readonly Dictionary<string, DateTime> _lastRunTimes = new(StringComparer.OrdinalIgnoreCase);

    public SyncWorker(ConfigService configService)
    {
        _configService = configService;
        _log = Log.Logger;

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.Information("=== LdapCloudSync Service Starting ===");

        // Load configuration
        _configService.Load();
        LoggingService.Initialize(_configService.Current.Logging);

        // Start IPC server for app communication
        _ipcServer = new IpcServer(HandleIpcCommandAsync, _log);
        _ipcServer.Start();

        _log.Information("Service initialized. {TargetCount} cloud target(s) configured.",
            _configService.Current.CloudTargets.Count);

        // Main scheduling loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = await EvaluateAndRunSchedulesAsync(stoppingToken);

                // Sleep until the next scheduled sync or 1 minute (whichever is shorter)
                // Short sleep ensures we pick up config changes and IPC commands promptly
                var sleepDuration = delay < TimeSpan.FromMinutes(1)
                    ? TimeSpan.FromMinutes(1)
                    : delay > TimeSpan.FromMinutes(5)
                        ? TimeSpan.FromMinutes(5)
                        : delay;

                _log.Debug("Next schedule check in {Delay}", sleepDuration);
                await Task.Delay(sleepDuration, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Unhandled error in sync worker loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _log.Information("=== LdapCloudSync Service Stopping ===");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_ipcServer is not null)
            await _ipcServer.StopAsync();

        LoggingService.Shutdown();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Evaluates all target schedules and runs any that are due.
    /// Returns the smallest delay until the next due sync.
    /// </summary>
    private async Task<TimeSpan> EvaluateAndRunSchedulesAsync(CancellationToken stoppingToken)
    {
        var config = _configService.Current;
        var nowUtc = DateTime.UtcNow;
        var smallestDelay = TimeSpan.FromHours(1);

        foreach (var target in config.CloudTargets)
        {
            if (!target.Enabled)
                continue;

            // Evaluate assets schedule
            if (target.Assets.Enabled)
            {
                var assetsSchedule = target.Schedule.PrimarySchedule;
                var assetsDelay = await EvaluateCategoryScheduleAsync(
                    target, "assets", assetsSchedule, nowUtc, stoppingToken);

                if (assetsDelay < smallestDelay)
                    smallestDelay = assetsDelay;
            }

            // Evaluate users schedule
            if (target.Users.Enabled)
            {
                var usersSchedule = target.Schedule.CoupledSchedule
                    ? target.Schedule.PrimarySchedule
                    : target.Schedule.UsersSchedule;

                var usersDelay = await EvaluateCategoryScheduleAsync(
                    target, "users", usersSchedule, nowUtc, stoppingToken);

                if (usersDelay < smallestDelay)
                    smallestDelay = usersDelay;
            }
        }

        return smallestDelay;
    }

    private async Task<TimeSpan> EvaluateCategoryScheduleAsync(
        CloudTargetConfig target,
        string category,
        ScheduleEntry schedule,
        DateTime nowUtc,
        CancellationToken stoppingToken)
    {
        if (!schedule.Enabled)
        {
            _log.Debug("Schedule disabled for {Target} / {Category}", target.Name, category);
            return TimeSpan.FromHours(1);
        }

        var key = $"{target.Id}:{category}";
        var hasPriorRun = _lastRunTimes.TryGetValue(key, out var lastRun);

        if (!hasPriorRun)
        {
            if (!target.Schedule.RunAtLaunch)
            {
                // Seed baseline at startup so first run waits for next window.
                _lastRunTimes[key] = nowUtc;
                return ScheduleEvaluator.GetDelayUntilNextRun(schedule, nowUtc);
            }

            lastRun = DateTime.MinValue;
        }

        var nextRun = ScheduleEvaluator.GetNextRunTime(schedule, lastRun);

        if (nextRun is null || nextRun > nowUtc)
        {
            // Not due yet — return delay until next run
            return nextRun.HasValue ? nextRun.Value - nowUtc : TimeSpan.FromHours(1);
        }

        // It's time to sync
        _log.Information("Scheduled sync triggered for {Target} / {Category}", target.Name, category);
        await RunSyncInternalAsync(target.Id, category, maxRecords: 0, stoppingToken);
        _lastRunTimes[key] = nowUtc;

        // Return delay until the next occurrence
        return ScheduleEvaluator.GetDelayUntilNextRun(schedule, nowUtc);
    }

    /// <summary>
    /// Runs the sync pipeline for a specific target and category.
    /// </summary>
    private async Task<SyncResult> RunSyncInternalAsync(
        string targetId,
        string category,
        int maxRecords,
        CancellationToken cancellationToken)
    {
        var config = _configService.Current;
        var loggingConfig = config.Logging;

        var activityLogger = LoggingService.CreateActivityLogger(
            $"sync-{category}", loggingConfig);

        var orchestrator = new SyncOrchestrator(
            _configService,
            adConfig => new ActiveDirectoryProvider(adConfig),
            targetConfig => CloudClientFactory.CreateClient(targetConfig, activityLogger),
            source => CloudSourceFactory.CreateClient(source),
            new SourceFileService(),
            activityLogger);

        if (maxRecords > 0)
            return await orchestrator.RunTestSyncAsync(targetId, category, maxRecords, cancellationToken);

        return await orchestrator.RunSyncAsync(targetId, category, cancellationToken);
    }

    #region IPC Command Handler

    private async Task<IpcResponse> HandleIpcCommandAsync(IpcCommand command)
    {
        _log.Information("Processing IPC command: {CommandType}", command.Type);

        try
        {
            return command.Type switch
            {
                IpcCommandType.RunSync => await HandleSyncCommandAsync(command, isTest: false),
                IpcCommandType.RunTestSync => await HandleSyncCommandAsync(command, isTest: true),
                IpcCommandType.ReloadConfig => HandleReloadConfig(),
                IpcCommandType.GetStatus => HandleGetStatus(),
                IpcCommandType.Stop => HandleStop(),
                _ => IpcResponse.Error($"Unknown command type: {command.Type}")
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error handling IPC command {CommandType}", command.Type);
            return IpcResponse.Error($"Command failed: {ex.Message}");
        }
    }

    private async Task<IpcResponse> HandleSyncCommandAsync(IpcCommand command, bool isTest)
    {
        var config = _configService.Current;
        var targets = string.IsNullOrEmpty(command.TargetId)
            ? config.CloudTargets.Where(t => t.Enabled).ToList()
            : config.CloudTargets.Where(t => t.Id == command.TargetId).ToList();

        if (targets.Count == 0)
            return IpcResponse.Error("No matching enabled targets found.");

        var allResults = new List<string>();
        var maxRecords = isTest ? command.MaxRecords : 0;

        foreach (var target in targets)
        {
            var categories = ResolveCategories(command.Category, target);

            foreach (var category in categories)
            {
                var result = await RunSyncInternalAsync(target.Id, category, maxRecords, CancellationToken.None);
                allResults.Add(
                    $"{target.Name}/{category}: Created={result.Created}, Updated={result.Updated}, " +
                    $"Failed={result.Failed}{(result.Errors.Count > 0 ? $", Errors: {string.Join("; ", result.Errors)}" : "")}");
            }
        }

        var label = isTest ? "Test sync" : "Sync";
        return IpcResponse.Ok(
            $"{label} complete for {targets.Count} target(s).",
            string.Join("\n", allResults));
    }

    private IpcResponse HandleReloadConfig()
    {
        _configService.Load();
        _lastRunTimes.Clear(); // Reset schedules on reload
        _log.Information("Configuration reloaded from disk");
        return IpcResponse.Ok("Configuration reloaded successfully.");
    }

    private IpcResponse HandleGetStatus()
    {
        var config = _configService.Current;
        var enabledTargets = config.CloudTargets.Count(t => t.Enabled);
        var scheduledItems = _lastRunTimes.Count;

        var status =
            $"Service: Running\n" +
            $"Targets: {enabledTargets} enabled / {config.CloudTargets.Count} total\n" +
            $"Tracked schedules: {scheduledItems}\n" +
            $"Uptime check: {DateTime.UtcNow:u}";

        return IpcResponse.Ok("Service is running.", status);
    }

    private IpcResponse HandleStop()
    {
        _log.Information("Stop command received via IPC");
        // Request graceful shutdown through the host
        Environment.Exit(0);
        return IpcResponse.Ok("Service stopping.");
    }

    private static List<string> ResolveCategories(string? category, CloudTargetConfig target)
    {
        var categories = new List<string>();

        switch (category?.ToLowerInvariant())
        {
            case "assets":
                if (target.Assets.Enabled) categories.Add("assets");
                break;
            case "users" or "loanees":
                if (target.Users.Enabled) categories.Add("users");
                break;
            case "both" or null or "":
                if (target.Assets.Enabled) categories.Add("assets");
                if (target.Users.Enabled) categories.Add("users");
                break;
        }

        return categories;
    }

    #endregion
}