using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LdapCloudSync.Core.Models;
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

        var jsonNames = Directory
            .GetFiles(SourceFilesDirectory, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f));

        var linkedNames = Directory
            .GetFiles(SourceFilesDirectory, "*.link")
            .Select(f => Path.GetFileNameWithoutExtension(f));

        return jsonNames
            .Concat(linkedNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
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

        if (!File.Exists(path))
        {
            var linkPath = GetLinkPath(name);
            if (File.Exists(linkPath))
            {
                var linkedPath = (await File.ReadAllTextAsync(linkPath)).Trim();
                if (string.IsNullOrWhiteSpace(linkedPath))
                    throw new InvalidOperationException($"Source file link '{name}' is empty or invalid.");

                await File.WriteAllTextAsync(linkedPath, prettyJson);
                _log.Information("Linked source file saved: {Path}", linkedPath);
                return;
            }
        }

        await File.WriteAllTextAsync(path, prettyJson);
        _log.Information("Source file saved: {Path}", path);
    }

    /// <summary>
    /// Registers an external JSON file as a source by creating a local link entry.
    /// </summary>
    public string RegisterExternalFile(string externalPath, string? alias = null)
    {
        EnsureDirectoryExists();

        if (string.IsNullOrWhiteSpace(externalPath))
            throw new ArgumentException("External file path cannot be empty.", nameof(externalPath));

        var fullPath = Path.GetFullPath(externalPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Selected file does not exist.", fullPath);

        var baseName = string.IsNullOrWhiteSpace(alias)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : alias.Trim();

        var finalName = GetAvailableName(baseName);
        var linkPath = GetLinkPath(finalName);

        File.WriteAllText(linkPath, fullPath);
        _log.Information("External source file linked: {Name} -> {Path}", finalName, fullPath);

        return finalName;
    }

    /// <summary>
    /// Applies a configured post-success action to the linked source file payload.
    /// This does not remove or rename the .link registration file itself.
    /// </summary>
    public async Task<(bool Applied, string Message)> ApplyLinkedFileSuccessActionAsync(
        string sourceName,
        FileSourceSuccessAction action,
        string renameSuffix)
    {
        if (action == FileSourceSuccessAction.None)
            return (false, "No post-success action configured.");

        var linkPath = GetLinkPath(sourceName);
        if (!File.Exists(linkPath))
            return (false, $"Source '{sourceName}' is not a linked file source.");

        var linkedPath = (await File.ReadAllTextAsync(linkPath)).Trim();
        if (string.IsNullOrWhiteSpace(linkedPath))
            return (false, $"Linked path for source '{sourceName}' is empty.");

        if (!File.Exists(linkedPath))
            return (false, $"Linked source file not found: {linkedPath}");

        switch (action)
        {
            case FileSourceSuccessAction.DeleteFile:
                File.Delete(linkedPath);
                return (true, $"Deleted linked source file '{linkedPath}'.");

            case FileSourceSuccessAction.RenameFile:
                var dir = Path.GetDirectoryName(linkedPath) ?? string.Empty;
                var baseName = Path.GetFileNameWithoutExtension(linkedPath);
                var ext = Path.GetExtension(linkedPath);

                var safeSuffix = string.IsNullOrWhiteSpace(renameSuffix)
                    ? "-processed"
                    : renameSuffix.Trim();

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var candidate = Path.Combine(dir, $"{baseName}{safeSuffix}-{timestamp}{ext}");

                var i = 2;
                while (File.Exists(candidate))
                {
                    candidate = Path.Combine(dir, $"{baseName}{safeSuffix}-{timestamp}-{i}{ext}");
                    i++;
                }

                File.Move(linkedPath, candidate);
                return (true, $"Renamed linked source file to '{candidate}'.");

            default:
                return (false, $"Unsupported post-success action: {action}");
        }
    }

    /// <summary>
    /// Reads a saved source file and returns its raw JSON content for the editor.
    /// Supports local JSON files and linked external JSON files.
    /// </summary>
    public async Task<string> ReadRawJsonAsync(string name)
    {
        var path = GetFilePath(name);

        if (File.Exists(path))
            return await File.ReadAllTextAsync(path);

        var linkPath = GetLinkPath(name);
        if (File.Exists(linkPath))
        {
            var linkedPath = (await File.ReadAllTextAsync(linkPath)).Trim();
            if (string.IsNullOrWhiteSpace(linkedPath))
                throw new InvalidOperationException($"Source file link '{name}' is empty or invalid.");

            if (!File.Exists(linkedPath))
                throw new FileNotFoundException($"Linked source file for '{name}' was not found.", linkedPath);

            return await File.ReadAllTextAsync(linkedPath);
        }

        throw new FileNotFoundException($"Source file '{name}' not found.", path);
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

            List<Dictionary<string, string>> records;

            // Supported formats:
            // 1) top-level array: [ {...}, {...} ]
            // 2) object with category array: { "assets": [ ... ] } / { "users": [ ... ] }
            // 3) object with "fields" array: { "fields": [ ... ] }
            // 4) single record object: { "ComputerName": "...", ... }
            if (root.ValueKind == JsonValueKind.Array)
            {
                records = ParseRecordsFromArray(root);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetArrayPropertyCaseInsensitive(root, category, out var categoryArray))
                {
                    records = ParseRecordsFromArray(categoryArray);
                }
                else if (TryGetArrayPropertyCaseInsensitive(root, "fields", out var fieldsArray))
                {
                    records = ParseRecordsFromArray(fieldsArray);
                }
                else
                {
                    var singleRecord = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    FlattenJsonObject(root, singleRecord, prefix: null);
                    records = [singleRecord];
                }
            }
            else
            {
                _log.Warning("Source file '{Name}' is not an object or array and cannot be parsed.", name);
                return [];
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
        var jsonPath = GetFilePath(name);
        var linkPath = GetLinkPath(name);

        if (File.Exists(jsonPath))
        {
            File.Delete(jsonPath);
            _log.Information("Source file deleted: {Path}", jsonPath);
        }

        if (File.Exists(linkPath))
        {
            File.Delete(linkPath);
            _log.Information("Source file link deleted: {Path}", linkPath);
        }
    }

    /// <summary>
    /// Renames a saved source file.
    /// </summary>
    public void RenameFile(string currentName, string newName)
    {
        ValidateName(newName);

        var oldJsonPath = GetFilePath(currentName);
        var oldLinkPath = GetLinkPath(currentName);
        var newJsonPath = GetFilePath(newName);
        var newLinkPath = GetLinkPath(newName);

        if (File.Exists(newJsonPath) || File.Exists(newLinkPath))
            throw new InvalidOperationException($"A source file named '{newName}' already exists.");

        if (File.Exists(oldJsonPath))
        {
            File.Move(oldJsonPath, newJsonPath);
            _log.Information("Source file renamed: {OldPath} -> {NewPath}", oldJsonPath, newJsonPath);
            return;
        }

        if (File.Exists(oldLinkPath))
        {
            File.Move(oldLinkPath, newLinkPath);
            _log.Information("Source file link renamed: {OldPath} -> {NewPath}", oldLinkPath, newLinkPath);
            return;
        }

        throw new FileNotFoundException($"Source file '{currentName}' not found.", oldJsonPath);
    }

    //  Helpers 

    public bool IsLinkedFileSource(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return false;

        return File.Exists(GetLinkPath(sourceName.Trim()));
    }

    private string GetFilePath(string name) =>
        Path.Combine(SourceFilesDirectory, $"{name}.json");

    private string GetLinkPath(string name) =>
        Path.Combine(SourceFilesDirectory, $"{name}.link");

    private bool SourceNameExists(string name) =>
        File.Exists(GetFilePath(name)) || File.Exists(GetLinkPath(name));

    private string GetAvailableName(string baseName)
    {
        ValidateName(baseName);

        if (!SourceNameExists(baseName))
            return baseName;

        var i = 2;
        while (SourceNameExists($"{baseName}-{i}"))
            i++;

        return $"{baseName}-{i}";
    }

    private void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(SourceFilesDirectory);
        EnsureWritableForStandardUsers();
    }

    private void EnsureWritableForStandardUsers()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var directoryInfo = new DirectoryInfo(SourceFilesDirectory);
            var security = directoryInfo.GetAccessControl();
            var usersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var accessRule = new FileSystemAccessRule(
                usersSid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            security.AddAccessRule(accessRule);
            directoryInfo.SetAccessControl(security);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or PlatformNotSupportedException or SystemException)
        {
            _log.Debug(ex, "Unable to update source file permissions for {Directory}; continuing with existing ACLs.", SourceFilesDirectory);
        }
    }

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


    private static List<Dictionary<string, string>> ParseRecordsFromArray(JsonElement arrayElement)
    {
        var records = new List<Dictionary<string, string>>();

        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FlattenJsonObject(item, record, prefix: null);
            records.Add(record);
        }

        return records;
    }

    private static bool TryGetArrayPropertyCaseInsensitive(
        JsonElement root,
        string propertyName,
        out JsonElement arrayElement)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                arrayElement = prop.Value;
                return true;
            }

            break;
        }

        arrayElement = default;
        return false;
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