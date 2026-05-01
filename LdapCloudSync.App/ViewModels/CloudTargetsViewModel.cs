using System.Collections.ObjectModel;
using System.Windows.Input;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Presets;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

public sealed class CloudTargetsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly SourcesViewModel _sourcesVm;
    private readonly SourceFileService _sourceFileService;

    public CloudTargetsViewModel(
        ConfigService configService,
        SourcesViewModel sourcesVm,
        SourceFileService sourceFileService)
    {
        _configService     = configService;
        _sourcesVm         = sourcesVm;
        _sourceFileService = sourceFileService;

        PresetNames = ["Blank (REST)", .. PresetRegistry.GetAllNames()];

        AddTargetCommand    = new RelayCommand(AddTarget);
        RemoveTargetCommand = new RelayCommand(RemoveTarget, () => SelectedTarget is not null);

        LoadFromConfig();
    }

    /// <summary>
    /// Called by MainViewModel when an AD source discovers OUs.
    /// Rebuilds OU selectors for targets that reference this source,
    /// or for targets with no SourceId (which fall back to any AD source).
    /// </summary>
    public void RebuildAllOUSelections(
        string sourceId,
        IEnumerable<string> computerOUs,
        IEnumerable<string> userOUs)
    {
        foreach (var target in Targets)
        {
            if (string.IsNullOrEmpty(target.SourceId) || target.SourceId == sourceId)
                target.RebuildOUSelections(computerOUs, userOUs);
        }
    }

    public ObservableCollection<CloudTargetViewModel> Targets { get; } = [];
    public IReadOnlyList<string> PresetNames { get; }

    private CloudTargetViewModel? _selectedTarget;
    public CloudTargetViewModel? SelectedTarget
    {
        get => _selectedTarget;
        set => SetProperty(ref _selectedTarget, value);
    }

    public ICommand AddTargetCommand    { get; }
    public ICommand RemoveTargetCommand { get; }

    private void AddTarget()
    {
        var newConfig = new CloudTargetConfig { Name = $"Target {Targets.Count + 1}" };
        var vm        = new CloudTargetViewModel(newConfig, _configService, _sourcesVm, _sourceFileService);
        Targets.Add(vm);
        SelectedTarget = vm;
    }

    private void RemoveTarget()
    {
        if (SelectedTarget is null) return;
        var index = Targets.IndexOf(SelectedTarget);
        Targets.Remove(SelectedTarget);
        SelectedTarget = Targets.Count > 0
            ? Targets[Math.Min(index, Targets.Count - 1)]
            : null;
    }

    public void LoadFromConfig()
    {
        Targets.Clear();
        foreach (var targetConfig in _configService.Current.CloudTargets)
            Targets.Add(new CloudTargetViewModel(targetConfig, _configService, _sourcesVm, _sourceFileService));

        SelectedTarget = Targets.FirstOrDefault();
    }

    public void ApplyToConfig()
    {
        _configService.Current.CloudTargets.Clear();
        foreach (var vm in Targets)
        {
            vm.ApplyToConfig();
            _configService.Current.CloudTargets.Add(vm.Config);
        }
    }
}