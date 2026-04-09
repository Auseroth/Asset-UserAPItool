using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

public sealed class LogViewerViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _autoRefreshTimer;

    public LogViewerViewModel()
    {
        RefreshLogFilesCommand = new RelayCommand(RefreshLogFiles);
        LoadSelectedLogCommand = new RelayCommand(LoadSelectedLog);
        RefreshLogFiles();

        // Auto-refresh every 3 seconds when viewing logs
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _autoRefreshTimer.Tick += (_, _) => LoadSelectedLog();
        _autoRefreshTimer.Start();
    }

    public ObservableCollection<string> LogFiles { get; } = [];

    private string? _selectedLogFile;
    public string? SelectedLogFile
    {
        get => _selectedLogFile;
        set
        {
            if (SetProperty(ref _selectedLogFile, value))
                LoadSelectedLog();
        }
    }

    private string _logContent = string.Empty;
    public string LogContent
    {
        get => _logContent;
        set => SetProperty(ref _logContent, value);
    }

    public ICommand RefreshLogFilesCommand { get; }
    public ICommand LoadSelectedLogCommand { get; }

    private void RefreshLogFiles()
    {
        var previousSelection = SelectedLogFile;
        LogFiles.Clear();
        var logDir = LoggingService.GetLogDirectory();

        if (!Directory.Exists(logDir))
        {
            LogContent = "Log directory does not exist yet. Logs will appear after the service runs.";
            return;
        }

        var files = Directory.GetFiles(logDir, "*.log")
            .OrderByDescending(File.GetLastWriteTime)
            .Select(Path.GetFileName)
            .Where(f => f is not null);

        foreach (var file in files)
            LogFiles.Add(file!);

        // Restore previous selection or pick the newest
        if (previousSelection is not null && LogFiles.Contains(previousSelection))
            SelectedLogFile = previousSelection;
        else if (LogFiles.Count > 0)
            SelectedLogFile = LogFiles[0];
    }

    private void LoadSelectedLog()
    {
        if (string.IsNullOrEmpty(SelectedLogFile))
        {
            LogContent = string.Empty;
            return;
        }

        var logDir = LoggingService.GetLogDirectory();
        var fullPath = Path.Combine(logDir, SelectedLogFile);

        if (!File.Exists(fullPath))
        {
            LogContent = "File not found.";
            return;
        }

        try
        {
            // Use FileShare.ReadWrite so we can read logs the service is actively writing
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var newContent = reader.ReadToEnd();

            // Only update if content changed (avoids TextBox scroll jump)
            if (newContent != _logContent)
                LogContent = newContent;
        }
        catch (Exception ex)
        {
            LogContent = $"Error reading log: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _autoRefreshTimer.Stop();
    }
}