namespace LdapCloudSync.Core.Models;

/// <summary>
/// Maps a Reftab asset status name to its statid value.
/// </summary>
public sealed class ReftabStatusMapping
{
    /// <summary>The source status label to match from transformed source records.</summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary>The target Reftab status label selected in the UI.</summary>
    public string TargetStatusName { get; set; } = string.Empty;

    /// <summary>The Reftab statid to send for this status.</summary>
    public string StatId { get; set; } = string.Empty;
}
