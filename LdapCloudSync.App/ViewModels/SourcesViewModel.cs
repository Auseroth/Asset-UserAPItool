using System.Collections.ObjectModel;
using System.Windows.Input;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Services;
using LdapCloudSync.Core.Presets;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// Manages the list of configured data sources (AD and Cloud).
/// Parallel to CloudTargetsViewModel  one entry per configured source.
/// The Sources tab binds to this VM.
/// </summary>
public sealed class SourcesViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly SourceFileService _sourceFileService;

    public SourcesViewModel(ConfigService configService)
    {
        _configService = configService;
        _sourceFileService = new SourceFileService();

        AddAdSourceCommand = new RelayCommand(AddAdSource);
        AddCloudSourceCommand = new RelayCommand(AddCloudSource);
        RemoveSourceCommand = new RelayCommand(RemoveSource, () => SelectedSource is not null);
        RefreshFileSourcesCommand = new RelayCommand(RefreshFileSources);
        DeleteFileSourceCommand = new RelayCommand<string>(DeleteFileSource);
        SelectFileSourceCommand = new AsyncRelayCommand(p => SelectFileSourceAsync(p as string));

        LoadFromConfig();
        RefreshFileSources();
    }

    //  Preset names (shared with CloudSourceDetailView preset combobox) 
    public IReadOnlyList<string> PresetNames { get; } =
        ["Blank (REST)", .. PresetRegistry.GetAllNames()];

    // Configured sources (AD + Cloud) 
    public ObservableCollection<CloudSourceViewModel> Sources { get; } = [];

    private CloudSourceViewModel? _selectedSource;
    public CloudSourceViewModel? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value) && value != null)
                SelectedFileSource = null;
        }
    }

    private FileSourceViewModel? _selectedFileSource;
    public FileSourceViewModel? SelectedFileSource
    {
        get => _selectedFileSource;
        private set => SetProperty(ref _selectedFileSource, value);
    }

    public ICommand AddAdSourceCommand { get; }
    public ICommand AddCloudSourceCommand { get; }
    public ICommand RemoveSourceCommand { get; }
    public ICommand SelectFileSourceCommand { get; }

    /// <summary>
    /// Raised when any AD source discovers OUs, so MainViewModel can rebuild
    /// the OU selection lists on all cloud targets paired with that source.
    /// Passes (sourceId, computerOUs, userOUs).
    /// </summary>
    public event Action<string, IEnumerable<string>, IEnumerable<string>>? OUsDiscovered;

    //  File sources 

    /// <summary>
    /// Names of JSON files saved in ProgramData/LDAPult/sourceFiles/.
    /// Each can be referenced as a source by cloud targets using "file:{name}".
    /// </summary>
    public ObservableCollection<string> FileSourceNames { get; } = [];

    public ICommand RefreshFileSourcesCommand { get; }
    public ICommand DeleteFileSourceCommand { get; }

    private void RefreshFileSources()
    {
        FileSourceNames.Clear();
        foreach (var name in _sourceFileService.GetSavedFileNames())
            FileSourceNames.Add(name);
    }

    private void DeleteFileSource(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        _sourceFileService.DeleteFile(name);
        RefreshFileSources();
    }

    //  Add / Remove configured sources

    private void AddAdSource()
    {
        var config = new CloudSourceConfig
        {
            Name = $"AD Source {Sources.Count(s => s.IsAdSource) + 1}",
            SourceType = SourceType.AD,
            Enabled = true
        };
        var vm = CreateSourceViewModel(config);
        Sources.Add(vm);
        SelectedSource = vm;
    }

    private void AddCloudSource()
    {
        var config = new CloudSourceConfig
        {
            Name = $"Cloud Source {Sources.Count(s => s.IsCloudSource) + 1}",
            SourceType = SourceType.Cloud,
            Enabled = true
        };
        var vm = CreateSourceViewModel(config);
        Sources.Add(vm);
        SelectedSource = vm;
    }

    private void RemoveSource()
    {
        if (SelectedSource is null) return;
        var index = Sources.IndexOf(SelectedSource);
        Sources.Remove(SelectedSource);
        SelectedSource = Sources.Count > 0
            ? Sources[Math.Min(index, Sources.Count - 1)]
            : null;
    }

    //  Source option helpers (used by CloudTargetViewModel dropdowns) 

    public IReadOnlyList<string> GetAllSourceOptions(SourceFileService? sfs = null)
    {
        var options = new List<string>();
        foreach (var source in Sources)
            options.Add($"{source.Name} [{(source.IsAdSource ? "AD" : "Cloud")}]");
        foreach (var fileName in _sourceFileService.GetSavedFileNames())
            options.Add($"{fileName} [File]");
        return options;
    }

    public string ResolveSourceId(string displayOption, SourceFileService? sfs = null)
    {
        if (string.IsNullOrEmpty(displayOption))
            return string.Empty;

        if (displayOption.EndsWith("[File]", StringComparison.Ordinal))
        {
            var fileName = displayOption[..displayOption.LastIndexOf(" [File]", StringComparison.Ordinal)];
            return $"{SourceFileService.FileSourcePrefix}{fileName}";
        }
        foreach (var source in Sources)
        {
            var label = $"{source.Name} [{(source.IsAdSource ? "AD" : "Cloud")}]";
            if (string.Equals(displayOption, label, StringComparison.Ordinal))
                return source.Config.Id;
        }
        return string.Empty;
    }

    public string GetDisplayOptionForSourceId(string sourceId, SourceFileService? sfs = null)
    {
        if (string.IsNullOrEmpty(sourceId)) return string.Empty;
        if (sourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
        {
            var fileName = sourceId[SourceFileService.FileSourcePrefix.Length..];
            return $"{fileName} [File]";
        }
        var source = Sources.FirstOrDefault(s => s.Config.Id == sourceId);
        return source is null ? string.Empty
            : $"{source.Name} [{(source.IsAdSource ? "AD" : "Cloud")}]";
    }

    // Config persistence

    public void LoadFromConfig()
    {
        Sources.Clear();
        foreach (var sourceConfig in _configService.Current.Sources)
            Sources.Add(CreateSourceViewModel(sourceConfig));
        SelectedSource = Sources.FirstOrDefault();
    }

    public void ApplyToConfig()
    {
        _configService.Current.Sources.Clear();
        foreach (var vm in Sources)
        {
            vm.ApplyToConfig();
            _configService.Current.Sources.Add(vm.Config);
        }
    }

    private CloudSourceViewModel CreateSourceViewModel(CloudSourceConfig config)
    {
        var vm = new CloudSourceViewModel(config, _configService, _sourceFileService);
        vm.OUsDiscovered += () =>
            OUsDiscovered?.Invoke(vm.Config.Id, vm.DiscoveredComputerOUs, vm.DiscoveredUserOUs);
        return vm;
    }

    private async Task SelectFileSourceAsync(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;

        // Clear cloud source selection without triggering its setter's null-guard
        _selectedSource = null;
        OnPropertyChanged(nameof(SelectedSource));

        var vm = new FileSourceViewModel(name, _sourceFileService);
        vm.Renamed += newName =>
        {
            RefreshFileSources();
            _ = SelectFileSourceAsync(newName);
        };
        await vm.LoadAsync();
        SelectedFileSource = vm;
    }
}