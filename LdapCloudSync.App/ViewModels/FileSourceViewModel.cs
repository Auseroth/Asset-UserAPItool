using System.Text.Json;
using System.Windows.Input;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// ViewModel for editing a saved file source (a JSON snapshot on disk).
/// </summary>
public sealed class FileSourceViewModel : ViewModelBase
{
    private readonly SourceFileService _sourceFileService;
    private readonly string _originalName;

    public FileSourceViewModel(string name, SourceFileService sourceFileService)
    {
        _originalName       = name;
        _sourceFileService  = sourceFileService;
        _displayName        = name;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    private string _displayName;
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    private string _jsonContent = string.Empty;
    public string JsonContent
    {
        get => _jsonContent;
        set => SetProperty(ref _jsonContent, value);
    }

    private string _saveStatus = string.Empty;
    public string SaveStatus
    {
        get => _saveStatus;
        set => SetProperty(ref _saveStatus, value);
    }

    public ICommand SaveCommand { get; }

    /// <summary>Raised after a successful rename so the parent list can refresh.</summary>
    public event Action<string>? Renamed; // new name

    public async Task LoadAsync()
    {
        try
        {
            SaveStatus = "Loading...";

            // Read and pretty-print on a background thread to avoid freezing the UI
            // for large files (e.g. full SolarWinds asset exports with nested owner objects).
            var formatted = await Task.Run(async () =>
            {
                var raw = await _sourceFileService.ReadRawJsonAsync(_originalName);
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    return raw; // Not valid JSON — show as-is
                }
            });

            JsonContent = formatted;
            SaveStatus  = string.Empty;
        }
        catch (Exception ex)
        {
            JsonContent = string.Empty;
            SaveStatus  = $"Could not load file: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        var newName = DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            SaveStatus = "Name cannot be empty.";
            return;
        }

        try
        {
            await _sourceFileService.SaveRawJsonAsync(newName, JsonContent);

            // If renamed, delete the old file
            if (!string.Equals(newName, _originalName, StringComparison.OrdinalIgnoreCase))
            {
                _sourceFileService.DeleteFile(_originalName);
                Renamed?.Invoke(newName);
            }

            SaveStatus = $"Saved: {newName}.json";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }
}