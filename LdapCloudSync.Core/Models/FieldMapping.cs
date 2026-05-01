namespace LdapCloudSync.Core.Models;

/// <summary>
/// A single field mapping from one or more source fields to a cloud target field.
/// Source fields may be AD attributes (when source is AD) or API field names
/// (when source is a cloud API or file).
/// </summary>
public sealed class FieldMapping
{
    /// <summary>
    /// The target cloud field name (e.g., "asset_tag", "fullName").
    /// </summary>
    public string CloudField { get; set; } = string.Empty;

    /// <summary>
    /// One or more source fields used as input.
    /// For AD sources these are LDAP attribute names (e.g., "cn", "sAMAccountName").
    /// For cloud/file sources these are API response field names.
    /// For simple 1:1 mapping this contains a single entry.
    /// For transforms/merges this contains all referenced fields.
    /// </summary>
    public List<string> SourceFields { get; set; } = [];

    /// <summary>
    /// Optional transform expression. Uses {fieldName} placeholders.
    /// If null/empty, the first (and only) source field value is used directly.
    /// Examples:
    ///   "{givenName} {sn}"           -> "John Smith"
    ///   "{department} - {title}"     -> "Engineering - Developer"
    ///   "{mail}"                     -> direct passthrough (same as no transform)
    /// </summary>
    public string? TransformExpression { get; set; }

    /// <summary>
    /// Optional default value if the source field(s) are empty/null.
    /// </summary>
    public string? DefaultValue { get; set; }
}