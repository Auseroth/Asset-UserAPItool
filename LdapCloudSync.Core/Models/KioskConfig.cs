namespace LdapCloudSync.Core.Models;

/// <summary>
/// Settings for the Asset Check-In kiosk experience.
/// </summary>
public sealed class KioskConfig
{
    public const string DefaultAssetCheckInArgument = "--asset-checkin";
    public const string AssetCheckInSourceFileName = "Check-in Kiosk";

    /// <summary>
    /// Command-line argument that launches the asset check-in window directly.
    /// </summary>
    public string AssetCheckInLaunchArgument { get; set; } = DefaultAssetCheckInArgument;

    /// <summary>
    /// Window title shown in kiosk mode.
    /// </summary>
    public string AssetCheckInWindowTitle { get; set; } = "Asset Check-In";

    /// <summary>
    /// Blocks repeated check-ins for the same tag/serial pair within this window.
    /// Set 0 to disable.
    /// </summary>
    public int DuplicateGuardSeconds { get; set; } = 5;

    /// <summary>
    /// Auto-close delay after successful check-in. Set 0 to keep window open.
    /// </summary>
    public int AutoCloseSeconds { get; set; } = 0;
}
