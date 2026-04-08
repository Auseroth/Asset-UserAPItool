namespace LdapCloudSync.Core.Models;

/// <summary>
/// A single field mapping from one or more AD attributes to a cloud field.
/// </summary>
public sealed class FieldMapping
{
    /// <summary>
    /// The target cloud field name (e.g., "asset_tag", "fullName").
    /// </summary>
    public string CloudField { get; set; } = string.Empty;

    /// <summary>
    /// One or more AD attributes used as source.
    /// For simple 1:1 mapping, this contains a single entry.
    /// For transforms/merges, this contains all referenced attributes.
    /// </summary>
    public List<string> AdAttributes { get; set; } = [];

    /// <summary>
    /// Optional transform expression. Uses {attributeName} placeholders.
    /// If null/empty, the first (and only) AD attribute value is used directly.
    /// Examples:
    ///   "{givenName} {sn}"           -> "John Smith"
    ///   "{department} - {title}"     -> "Engineering - Developer"
    ///   "{mail}"                     -> direct passthrough (same as no transform)
    /// </summary>
    public string? TransformExpression { get; set; }

    /// <summary>
    /// Optional default value if the AD attribute(s) are empty/null.
    /// </summary>
    public string? DefaultValue { get; set; }
}