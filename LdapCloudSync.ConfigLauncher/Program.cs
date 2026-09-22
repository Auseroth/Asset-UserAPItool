using System.Diagnostics;

namespace LdapCloudSync.ConfigLauncher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var appPath = ResolveAppPath();

        var startInfo = new ProcessStartInfo
        {
            FileName = appPath,
            Arguments = "--config",
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(startInfo);
    }

    private static string ResolveAppPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ConNexus.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "ConNexus.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "ConNexus.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ConNexus.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "LdapCloudSync.App", "bin", "Debug", "net8.0-windows", "ConNexus.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "LdapCloudSync.App", "bin", "Release", "net8.0-windows", "ConNexus.exe")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
                return fullPath;
        }

        throw new FileNotFoundException("Unable to locate the main application executable.", candidates[^1]);
    }
}
