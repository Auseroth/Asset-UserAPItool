using System.Collections.ObjectModel;
using LdapCloudSync.Core.Models;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// ViewModel for the TestSyncPreviewWindow.
/// Displays the exact JSON payloads captured from a dry-run sync.
/// </summary>
public sealed class TestSyncPreviewViewModel
{
    public string Summary { get; set; } = string.Empty;
    public ObservableCollection<DryRunCapture> Captures { get; } = [];
}