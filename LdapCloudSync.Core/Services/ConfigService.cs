using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LdapCloudSync.Core.Models;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Handles loading, saving, and managing the JSON configuration file.
/// Config is stored in C:\ProgramData\LdapCloudSync\.
/// </summary>
public sealed class ConfigService
{
    private static readonly string ConfigDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LDAPult");

    private static readonly string ConfigFilePath =
        Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private SyncConfig _config = new();
    private readonly object _lock = new();

    public SyncConfig Current
    {
        get { lock (_lock) return _config; }
    }

    /// <summary>
    /// Loads config from disk. If the file doesn't exist, creates a default config.
    /// </summary>
    public SyncConfig Load()
    {
        lock (_lock)
        {
            if (!File.Exists(ConfigFilePath))
            {
                _config = new SyncConfig();
                Save();
                return _config;
            }

            var json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
            _config = JsonSerializer.Deserialize<SyncConfig>(json, JsonOptions) ?? new SyncConfig();
            return _config;
        }
    }

    /// <summary>
    /// Persists the current config to disk.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(_config, JsonOptions);
            File.WriteAllText(ConfigFilePath, json, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Encrypts a plaintext password using DPAPI (machine-scoped) and returns a base64 string.
    /// </summary>
    public static string EncryptPassword(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encryptedBytes = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// Decrypts a DPAPI-encrypted base64 string back to plaintext.
    /// </summary>
    public static string DecryptPassword(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return string.Empty;

        var encryptedBytes = Convert.FromBase64String(encryptedBase64);
        var plaintextBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public static string GetConfigDirectory() => ConfigDirectory;
    public static string GetConfigFilePath() => ConfigFilePath;
}