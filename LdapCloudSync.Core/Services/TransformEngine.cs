using System.Text.RegularExpressions;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Applies field mappings and transforms to convert source records into cloud-ready dictionaries.
/// Handles direct 1:1 mappings, multi-field merges, and expression-based transforms.
/// Works uniformly for AD, cloud API, and file sources - the input is always a flat
/// string dictionary regardless of source type.
/// </summary>
public sealed partial class TransformEngine
{
    private readonly ILogger _log;

    public TransformEngine(ILogger? logger = null)
    {
        _log = logger ?? Log.Logger;
    }

    /// <summary>
    /// Transforms a batch of source records using the given field mappings.
    /// </summary>
    /// <param name="sourceRecords">Raw source records (field -> value dictionaries).</param>
    /// <param name="mappings">Field mappings to apply.</param>
    /// <returns>Cloud-ready records (cloud field -> transformed value dictionaries).</returns>
    public IReadOnlyList<Dictionary<string, object>> TransformBatch(
        IReadOnlyList<Dictionary<string, string>> sourceRecords,
        IReadOnlyList<FieldMapping> mappings)
    {
        var results = new List<Dictionary<string, object>>(sourceRecords.Count);

        foreach (var sourceRecord in sourceRecords)
        {
            var cloudRecord = TransformSingle(sourceRecord, mappings);
            if (cloudRecord.Count > 0)
                results.Add(cloudRecord);
        }

        _log.Information("Transformed {Count} records using {MappingCount} field mappings",
            results.Count, mappings.Count);

        return results;
    }

    /// <summary>
    /// Transforms a single source record into a cloud-ready dictionary.
    /// </summary>
    public Dictionary<string, object> TransformSingle(
        Dictionary<string, string> sourceRecord,
        IReadOnlyList<FieldMapping> mappings)
    {
        var cloudRecord = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            try
            {
                var value = ApplyMapping(sourceRecord, mapping);
                if (value is not null)
                    cloudRecord[mapping.CloudField] = value;
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
    private static string? ApplyMapping(Dictionary<string, string> sourceRecord, FieldMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
            return ApplyTransformExpression(sourceRecord, mapping);

        if (mapping.SourceFields.Count == 0)
            return mapping.DefaultValue;

        var fieldName = mapping.SourceFields[0];
        if (sourceRecord.TryGetValue(fieldName, out var value) && !string.IsNullOrEmpty(value))
            return value;

        return mapping.DefaultValue;
    }

    /// <summary>
    /// Evaluates a transform expression by replacing {fieldName} placeholders
    /// with actual source values.
    /// Examples:
    ///   "{givenName} {sn}"           -> "John Smith"
    ///   "{department} - {title}"     -> "Engineering - Developer"
    ///   "PC-{cn}"                    -> "PC-WORKSTATION01"
    /// </summary>
    private static string? ApplyTransformExpression(Dictionary<string, string> sourceRecord, FieldMapping mapping)
    {
        var result = PlaceholderRegex().Replace(mapping.TransformExpression!, match =>
        {
            var fieldName = match.Groups[1].Value;
            return sourceRecord.TryGetValue(fieldName, out var value) && !string.IsNullOrEmpty(value)
                ? value
                : string.Empty;
        });

        return string.IsNullOrWhiteSpace(result) ? mapping.DefaultValue : result.Trim();
    }

    /// <summary>
    /// Extracts all source field names referenced in a transform expression.
    /// Useful for the UI to auto-populate the SourceFields list.
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
            return null;

        var openBraces = expression.Count(c => c == '{');
        var closeBraces = expression.Count(c => c == '}');

        if (openBraces != closeBraces)
            return "Mismatched braces in transform expression.";

        var matches = PlaceholderRegex().Matches(expression);
        if (matches.Count == 0 && (openBraces > 0 || closeBraces > 0))
            return "Invalid placeholder syntax. Use {fieldName} format.";

        foreach (Match match in matches)
        {
            if (string.IsNullOrWhiteSpace(match.Groups[1].Value))
                return "Empty placeholder name found.";
        }

        return null;
    }

    /// <summary>
    /// Evaluates a mapping against sample source data for UI preview purposes.
    /// </summary>
    public static string? PreviewTransform(Dictionary<string, string> sampleRecord, FieldMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
        {
            var result = PlaceholderRegex().Replace(mapping.TransformExpression, match =>
            {
                var fieldName = match.Groups[1].Value;
                return sampleRecord.TryGetValue(fieldName, out var value) && !string.IsNullOrEmpty(value)
                    ? value
                    : string.Empty;
            });

            return string.IsNullOrWhiteSpace(result) ? mapping.DefaultValue : result.Trim();
        }

        if (mapping.SourceFields.Count > 0 &&
            sampleRecord.TryGetValue(mapping.SourceFields[0], out var val) &&
            !string.IsNullOrEmpty(val))
        {
            return val;
        }

        return mapping.DefaultValue;
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();
}