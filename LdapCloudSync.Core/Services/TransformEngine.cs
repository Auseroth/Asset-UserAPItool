using System.Text.RegularExpressions;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Applies field mappings and transforms to convert AD records into cloud-ready dictionaries.
/// Handles direct 1:1 mappings, multi-field merges, and expression-based transforms.
/// </summary>
public sealed partial class TransformEngine
{
    private readonly ILogger _log;

    public TransformEngine(ILogger? logger = null)
    {
        _log = logger ?? Log.Logger;
    }

    /// <summary>
    /// Transforms a batch of AD records using the given field mappings.
    /// </summary>
    /// <param name="adRecords">Raw AD records (attribute -> value dictionaries).</param>
    /// <param name="mappings">Field mappings to apply.</param>
    /// <returns>Cloud-ready records (cloud field -> transformed value dictionaries).</returns>
    public IReadOnlyList<Dictionary<string, object>> TransformBatch(
        IReadOnlyList<Dictionary<string, string>> adRecords,
        IReadOnlyList<FieldMapping> mappings)
    {
        var results = new List<Dictionary<string, object>>(adRecords.Count);

        foreach (var adRecord in adRecords)
        {
            var cloudRecord = TransformSingle(adRecord, mappings);
            if (cloudRecord.Count > 0)
                results.Add(cloudRecord);
        }

        _log.Information("Transformed {Count} records using {MappingCount} field mappings",
            results.Count, mappings.Count);

        return results;
    }

    /// <summary>
    /// Transforms a single AD record into a cloud-ready dictionary.
    /// </summary>
    public Dictionary<string, object> TransformSingle(
        Dictionary<string, string> adRecord,
        IReadOnlyList<FieldMapping> mappings)
    {
        var cloudRecord = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            try
            {
                var value = ApplyMapping(adRecord, mapping);
                if (value is not null)
                {
                    cloudRecord[mapping.CloudField] = value;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Transform failed for cloud field {CloudField}", mapping.CloudField);
            }
        }

        return cloudRecord;
    }

    /// <summary>
    /// Applies a single mapping to produce a value for one cloud field.
    /// </summary>
    private static string? ApplyMapping(Dictionary<string, string> adRecord, FieldMapping mapping)
    {
        // If a transform expression is provided, use it
        if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
        {
            return ApplyTransformExpression(adRecord, mapping);
        }

        // Simple 1:1 mapping: take the value of the first (and only) AD attribute
        if (mapping.AdAttributes.Count == 0)
            return mapping.DefaultValue;

        var attrName = mapping.AdAttributes[0];
        if (adRecord.TryGetValue(attrName, out var value) && !string.IsNullOrEmpty(value))
            return value;

        return mapping.DefaultValue;
    }

    /// <summary>
    /// Evaluates a transform expression by replacing {attributeName} placeholders
    /// with actual AD values.
    /// Examples:
    ///   "{givenName} {sn}"           -> "John Smith"
    ///   "{department} - {title}"     -> "Engineering - Developer"
    ///   "PC-{cn}"                    -> "PC-WORKSTATION01"
    /// </summary>
    private static string? ApplyTransformExpression(Dictionary<string, string> adRecord, FieldMapping mapping)
    {
        var expression = mapping.TransformExpression!;
        var result = PlaceholderRegex().Replace(expression, match =>
        {
            var attrName = match.Groups[1].Value;
            if (adRecord.TryGetValue(attrName, out var value) && !string.IsNullOrEmpty(value))
                return value;

            return string.Empty;
        });

        // If the entire result is empty/whitespace after substitution, use the default
        if (string.IsNullOrWhiteSpace(result))
            return mapping.DefaultValue;

        return result.Trim();
    }

    /// <summary>
    /// Extracts all AD attribute names referenced in a transform expression.
    /// Useful for the UI to auto-populate the AdAttributes list.
    /// </summary>
    public static IReadOnlyList<string> ExtractAttributeNames(string transformExpression)
    {
        if (string.IsNullOrWhiteSpace(transformExpression))
            return [];

        return PlaceholderRegex()
            .Matches(transformExpression)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Validates a transform expression for correct syntax.
    /// Returns null if valid, or an error message if invalid.
    /// </summary>
    public static string? ValidateExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null; // Empty is fine (means direct mapping)

        var openBraces = expression.Count(c => c == '{');
        var closeBraces = expression.Count(c => c == '}');

        if (openBraces != closeBraces)
            return "Mismatched braces in transform expression.";

        var matches = PlaceholderRegex().Matches(expression);
        if (matches.Count == 0 && (openBraces > 0 || closeBraces > 0))
            return "Invalid placeholder syntax. Use {attributeName} format.";

        foreach (Match match in matches)
        {
            if (string.IsNullOrWhiteSpace(match.Groups[1].Value))
                return "Empty placeholder name found.";
        }

        return null;
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();
}