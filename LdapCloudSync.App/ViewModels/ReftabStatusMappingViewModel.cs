using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LdapCloudSync.Core.Models;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// ViewModel for a single Reftab asset status lookup row.
/// Maps a source status value to a selected target status name,
/// then resolves that target name into statid for push payloads.
/// </summary>
public sealed class ReftabStatusMappingViewModel : ViewModelBase
{
    private Dictionary<string, string>? _sampleRecord;
    private IReadOnlyDictionary<string, string> _targetStatusLookup =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ReftabStatusMappingViewModel()
    {
    }

    public ReftabStatusMappingViewModel(ReftabStatusMapping model)
    {
        SourceStatus = model.StatusName;
        TargetStatus = model.TargetStatusName;
        StatId = model.StatId;
    }

    public void SetSampleRecord(Dictionary<string, string>? sampleRecord)
    {
        _sampleRecord = sampleRecord;
    }

    public void SetSourceStatusOptions(IReadOnlyList<string> sourceStatuses)
    {
        AvailableSourceStatuses.Clear();
        foreach (var option in sourceStatuses.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
            AvailableSourceStatuses.Add(option);
    }

    public void SetTargetStatusOptions(IReadOnlyDictionary<string, string> targetStatusLookup)
    {
        _targetStatusLookup = targetStatusLookup;

        AvailableTargetStatuses.Clear();
        foreach (var option in targetStatusLookup.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            AvailableTargetStatuses.Add(option);

        if (!string.IsNullOrWhiteSpace(TargetStatus)
            && targetStatusLookup.TryGetValue(TargetStatus.Trim(), out var selectedStatId)
            && !string.IsNullOrWhiteSpace(selectedStatId))
        {
            StatId = selectedStatId;
            return;
        }

        if (!string.IsNullOrWhiteSpace(StatId))
        {
            var existing = targetStatusLookup.FirstOrDefault(kv =>
                string.Equals(kv.Value, StatId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(existing.Key))
            {
                TargetStatus = existing.Key;
                return;
            }
        }

        ResolveStatIdFromTargetStatus();
    }

    private string _sourceStatus = string.Empty;
    public string SourceStatus
    {
        get => _sourceStatus;
        set
        {
            if (SetProperty(ref _sourceStatus, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    public ObservableCollection<string> AvailableSourceStatuses { get; } = [];
    public ObservableCollection<string> AvailableTargetStatuses { get; } = [];

    private string _targetStatus = string.Empty;
    public string TargetStatus
    {
        get => _targetStatus;
        set
        {
            if (SetProperty(ref _targetStatus, value))
            {
                ResolveStatIdFromTargetStatus();
                OnPropertyChanged(nameof(Preview));
            }
        }
    }

    private string _statId = string.Empty;
    public string StatId
    {
        get => _statId;
        set
        {
            if (SetProperty(ref _statId, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    private void ResolveStatIdFromTargetStatus()
    {
        if (string.IsNullOrWhiteSpace(TargetStatus))
        {
            StatId = string.Empty;
            return;
        }

        if (_targetStatusLookup.TryGetValue(TargetStatus.Trim(), out var statId) && !string.IsNullOrWhiteSpace(statId))
            StatId = statId;
        else
            StatId = string.Empty;
    }

    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceStatus) && string.IsNullOrWhiteSpace(TargetStatus))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(StatId))
                return "→ (no statid resolved)";

            return $"→ {StatId}";
        }
    }

    public ReftabStatusMapping ToModel()
    {
        return new ReftabStatusMapping
        {
            StatusName = SourceStatus,
            TargetStatusName = TargetStatus,
            StatId = StatId
        };
    }
}
