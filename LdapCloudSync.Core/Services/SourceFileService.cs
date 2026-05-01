using System.Text.Json;
using Serilog;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Manages saved JSON source files stored in ProgramData/LDAPult/sourceFiles/.
/// Files saved here appear as "File" source options on cloud targets,
/// allowing a one-time GET retrieve to be manually edited and pushed.
/// </summary>
public sealed class SourceFileService
{
    private static readonly string SourceFilesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LDAPult", "sourceFiles");

    private readonly ILogger _log;

    public SourceFileService(ILogger? logger = null)
    {
        _log = logger ?? Log.Logger;
        EnsureDirectoryExists();
    }

    /// <summary>
    /// The prefix used in CloudTargetConfig.SourceId to identify file sources.
    /// e.g., "file:MyExport" references MyExport.json in the sourceFiles directory.
    /// </summary>
    public const string FileSourcePrefix = "file:";

    /// <summary>
    /// Returns the names (without extension) of all saved source files.
    /// </summary>
    public IReadOnlyList<string> GetSavedFileNames()
    {
        EnsureDirectoryExists();

        return Directory
            .GetFiles(SourceFilesDirectory, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Saves raw JSON content to a named file. If a file with the same name
    /// already exists it is overwritten (supporting re-edit workflows).
    /// </summary>
    public async Task SaveRawJsonAsync(string name, string rawJson)
    {
        EnsureDirectoryExists();
        ValidateName(name);

        var path = GetFilePath(name);

        // Pretty-print for human editability
        string prettyJson;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            prettyJson = JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            // If parse fails, save as-is so the user can fix it manually
            prettyJson = rawJson;
        }

        await File.WriteAllTextAsync(path, prettyJson);
        _log.Information("Source file saved: {Path}", path);
    }

    /// <summary>
    /// Reads a saved source file and returns its raw JSON content for the editor.
    /// </summary>
    public async Task<string> ReadRawJsonAsync(string name)
    {
        var path = GetFilePath(name);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Source file '{name}' not found.", path);

        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// Reads a saved source file and deserializes records for the given category.
    /// The file is expected to contain an object with "assets" and/or "users" array keys.
    /// </summary>
    public async Task<IReadOnlyList<Dictionary<string, string>>> ReadRecordsAsync(
        string name, string category)
    {
        var json = await ReadRawJsonAsync(name);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement arrayElement;

            // Support both: top-level array, or object with category keys
            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty(category, out var nested)
                     && nested.ValueKind == JsonValueKind.Array)
            {
                arrayElement = nested;
            }
            else
            {
                _log.Warning("Source file '{Name}' does not contain a '{Category}' array", name, category);
                return [];
            }

            var records = new List<Dictionary<string, string>>();

            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                FlattenJsonObject(item, record, prefix: null);
                records.Add(record);
            }

            _log.Information("Read {Count} records from source file '{Name}' category '{Category}'",
                records.Count, name, category);

            return records;
        }
        catch (JsonException ex)
        {
            _log.Error(ex, "Failed to parse source file '{Name}'", name);
            throw new InvalidOperationException($"Source file '{name}' contains invalid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deletes a saved source file by name.
    /// </summary>
    public void DeleteFile(string name)
    {
        var path = GetFilePath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
            _log.Information("Source file deleted: {Path}", path);
        }
    }

    /// <summary>
    /// Renames a saved source file.
    /// </summary>
    public void RenameFile(string currentName, string newName)
    {
        ValidateName(newName);
        var oldPath = GetFilePath(currentName);
        var newPath = GetFilePath(newName);

        if (!File.Exists(oldPath))
            throw new FileNotFoundException($"Source file '{currentName}' not found.", oldPath);

        if (File.Exists(newPath))
            throw new InvalidOperationException($"A source file named '{newName}' already exists.");

        File.Move(oldPath, newPath);
        _log.Information("Source file renamed: {OldPath} -> {NewPath}", oldPath, newPath);
    }

    //  Helpers 

    private string GetFilePath(string name) =>
        Path.Combine(SourceFilesDirectory, $"{name}.json");

    private void EnsureDirectoryExists() =>
        Directory.CreateDirectory(SourceFilesDirectory);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Source file name cannot be empty.", nameof(name));

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (name.Contains(c))
                throw new ArgumentException(
                    $"Source file name contains invalid character '{c}'.", nameof(name));
        }
    }


    /// <summary>
    /// Flattens a JSON object into string key-value pairs.
    /// Nested objects are flattened with dot notation (e.g., "details.model").
    /// </summary>
    private static void FlattenJsonObject(
        JsonElement element,
        Dictionary<string, string> result,
        string? prefix)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var key = prefix is null ? prop.Name : $"{prefix}.{prop.Name}";

            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    FlattenJsonObject(prop.Value, result, key);
                    break;
                case JsonValueKind.Array:
                    result[key] = prop.Value.GetRawText();
                    break;
                case JsonValueKind.Null:
                    result[key] = string.Empty;
                    break;
                default:
                    result[key] = prop.Value.ToString();
                    break;
            }
        }
    }
}