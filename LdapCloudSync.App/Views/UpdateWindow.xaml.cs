using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace LdapCloudSync.App.Views;

public partial class UpdateWindow : Window
{
    private const string DefaultReleaseUrl = "https://api.github.com/repos/Auseroth/Asset-UserAPItool/releases/latest";

    private readonly HttpClient _httpClient = new();
    private string? _downloadUrl;
    private string? _downloadFileName;
    private bool _isUpdateAvailable;

    public UpdateWindow()
    {
        InitializeComponent();

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Connexus-UpdateChecker");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        ReleaseUrlTextBox.Text = DefaultReleaseUrl;
        CurrentVersionTextBlock.Text = FormatVersion(GetCurrentVersion());
        LatestVersionTextBlock.Text = "Not checked";
        StatusTextBlock.Text = "Ready.";
        DownloadAndRunButton.IsEnabled = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _httpClient.Dispose();
        base.OnClosed(e);
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesCoreAsync();
    }

    private async Task<bool> CheckForUpdatesCoreAsync()
    {
        _downloadUrl = null;
        _downloadFileName = null;
        _isUpdateAvailable = false;

        var releaseUrl = ReleaseUrlTextBox.Text?.Trim() ?? string.Empty;
        if (!TryBuildLatestReleaseApiUrl(releaseUrl, out var apiUrl, out var repoName))
        {
            StatusTextBlock.Text = "Enter a valid GitHub API repository or releases URL.";
            LatestVersionTextBlock.Text = "Unknown";
            return false;
        }

        CheckUpdatesButton.IsEnabled = false;
        DownloadAndRunButton.IsEnabled = false;
        StatusTextBlock.Text = $"Checking latest release for {repoName}...";

        try
        {
            using var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var displayVersion = string.IsNullOrWhiteSpace(tag) ? "Unknown" : tag.Trim();
            LatestVersionTextBlock.Text = displayVersion;

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var assetUrl = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(assetUrl))
                        continue;

                    _downloadUrl = assetUrl;
                    _downloadFileName = name;
                    break;
                }
            }

            var currentVersion = GetCurrentVersion();
            if (TryParseVersion(displayVersion, out var latestVersion))
            {
                if (latestVersion > currentVersion)
                {
                    _isUpdateAvailable = !string.IsNullOrWhiteSpace(_downloadUrl);
                    StatusTextBlock.Text = _isUpdateAvailable
                        ? "A newer version is available. Click Update Now to install."
                        : "A newer version is available, but no EXE asset was found.";
                }
                else
                {
                    _downloadUrl = null;
                    _downloadFileName = null;
                    _isUpdateAvailable = false;
                    StatusTextBlock.Text = "You are up to date.";
                }
            }
            else
            {
                _downloadUrl = null;
                _downloadFileName = null;
                _isUpdateAvailable = false;
                StatusTextBlock.Text = "Latest release version could not be parsed.";
            }

            return _isUpdateAvailable;
        }
        catch (Exception ex)
        {
            LatestVersionTextBlock.Text = "Unknown";
            StatusTextBlock.Text = $"Update check failed: {ex.Message}";
            return false;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            DownloadAndRunButton.IsEnabled = _isUpdateAvailable;
        }
    }

    private async void DownloadAndRun_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUpdateAvailable || string.IsNullOrWhiteSpace(_downloadUrl))
        {
            var foundAsset = await CheckForUpdatesCoreAsync();
            if (!foundAsset || !_isUpdateAvailable || string.IsNullOrWhiteSpace(_downloadUrl))
            {
                StatusTextBlock.Text = "No newer update EXE is available.";
                return;
            }
        }

        DownloadAndRunButton.IsEnabled = false;
        CheckUpdatesButton.IsEnabled = false;

        try
        {
            var fileName = string.IsNullOrWhiteSpace(_downloadFileName)
                ? "Connexus-Update.exe"
                : _downloadFileName;

            var updateDirectory = Path.Combine(Path.GetTempPath(), "Connexus", "Updates");
            Directory.CreateDirectory(updateDirectory);

            var localFilePath = Path.Combine(updateDirectory, fileName);
            StatusTextBlock.Text = "Downloading update EXE...";

            await using (var sourceStream = await _httpClient.GetStreamAsync(_downloadUrl))
            await using (var destinationStream = File.Create(localFilePath))
            {
                await sourceStream.CopyToAsync(destinationStream);
            }

            StatusTextBlock.Text = "Starting update EXE...";
            Process.Start(new ProcessStartInfo
            {
                FileName = localFilePath,
                UseShellExecute = true
            });

            StatusTextBlock.Text = "Update EXE started.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Download failed: {ex.Message}";
        }
        finally
        {
            DownloadAndRunButton.IsEnabled = true;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static bool TryBuildLatestReleaseApiUrl(string releaseUrl, out string apiUrl, out string repository)
    {
        apiUrl = string.Empty;
        repository = string.Empty;

        if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out var uri))
            return false;

        var parts = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? owner = null;
        string? repo = null;

        if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            // Supports /repos/{owner}/{repo}/releases/latest
            if (parts.Length >= 3 && parts[0].Equals("repos", StringComparison.OrdinalIgnoreCase))
            {
                owner = parts[1];
                repo = parts[2];
            }
            else if (parts.Length >= 2)
            {
                owner = parts[0];
                repo = parts[1];
            }
        }
        else if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
        {
            // Supports /{owner}/{repo}/releases/latest
            if (parts.Length >= 2)
            {
                owner = parts[0];
                repo = parts[1];
            }
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return false;

        repository = $"{owner}/{repo}";
        apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        return true;
    }

    private static Version GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version ?? new Version(0, 0, 0, 0);
    }

    private static string FormatVersion(Version version) => $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    private static bool TryParseVersion(string? releaseTag, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(releaseTag))
            return false;

        var cleaned = releaseTag.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[1..];

        cleaned = cleaned.Split('-', '+')[0];
        return Version.TryParse(cleaned, out version);
    }
}
