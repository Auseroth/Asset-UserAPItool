# ConNexus

ConNexus is a .NET 8 Windows solution for syncing Active Directory data to cloud systems, with a desktop configuration app, a Windows background service, and a kiosk-style asset check-in mode.

---

## Solution Overview

Projects in this repository:

- `LdapCloudSync.App` - WPF desktop app (`ConNexus.exe`) for configuration, testing, mapping, and update checks
- `LdapCloudSync.Core` - shared models, providers, scheduling, config, logging, and sync logic
- `LdapCloudSync.Service` - Windows Service host (`ConNexus.Service.exe`) that runs scheduled syncs
- `LdapCloudSync.ConfigLauncher` - small elevated launcher that starts `ConNexus.exe --config`
- `Asset&UserAPItool` - separate WinForms utility project in the same solution

---

## Requirements

- Windows
- .NET SDK 8.0+
- Visual Studio 2022/2026 (recommended)
- Active Directory access and credentials (for AD-backed sync scenarios)

---

## Build and Run

Build the full solution:

- Visual Studio: **Build > Build Solution**
- CLI: `dotnet build "Asset&UserAPItool.sln"`

Run desktop app:

- Visual Studio: set `LdapCloudSync.App` as startup project
- CLI: `dotnet run --project "LdapCloudSync.App\LdapCloudSync.App.csproj"`

---

## Configuration Storage

ConNexus stores config in:

- `C:\ProgramData\Connexus\config.json`

This is managed by `ConfigService` in `LdapCloudSync.Core`.

---

## Launch Arguments

`ConNexus.exe` supports these arguments:

| Argument | Behavior |
|---|---|
| *(none)* | Opens the main window. |
| `--config` | Opens configuration mode. If not elevated, ConNexus relaunches itself as admin. |
| `--asset-checkin` | Opens the Asset Check-In window directly (kiosk mode). |

Notes:

- `--config` takes precedence over kiosk mode.
- Kiosk mode also supports a custom argument from config (`Kiosk.AssetCheckInLaunchArgument`), while `--asset-checkin` remains the built-in default.

### Launch Argument Examples

From Command Prompt / PowerShell:

- `ConNexus.exe`
- `ConNexus.exe --config`
- `ConNexus.exe --asset-checkin`

From published output folder:

- `.\ConNexus.exe --config`
- `.\ConNexus.exe --asset-checkin`

From a Windows shortcut:

- Target: `"C:\Program Files\ConNexus\ConNexus.exe" --config`
- Target: `"C:\Program Files\ConNexus\ConNexus.exe" --asset-checkin`

---

## Update Check Behavior (GitHub Releases)

The update UI in the app checks the latest release API endpoint and compares:

- current app assembly version
- latest GitHub release tag

Default endpoint:

- `https://api.github.com/repos/Auseroth/Asset-UserAPItool/releases/latest`

Update download is enabled only when:

1. release tag parses to a version
2. latest version is newer than current version
3. latest release includes an `.exe` asset

---

## Windows Service

Service project: `LdapCloudSync.Service`

- Service name: `ConNexus`
- Worker implementation: `SyncWorker : BackgroundService`
- Runs schedule evaluation and sync execution in the background

Install helper script (run elevated):

- `LdapCloudSync.Service\install-service.ps1`

Uninstall helper script:

- `LdapCloudSync.Service\uninstall-service.ps1`

---

## Adding a New Cloud Provider (Quick Guide)

If you want to integrate another provider/API:

1. Create provider client in `LdapCloudSync.Core\Providers`
2. Add a preset in `LdapCloudSync.Core\Presets\PresetRegistry.cs` (optional but recommended)
3. Register provider selection in `CloudClientFactory`
4. Add provider-specific UI only if needed

Suggested validation path:

1. Test connection
2. Discover fields
3. Run small sync sample
4. Run scheduled sync through service

---

## Troubleshooting

- **No update EXE found**
  - Verify latest GitHub release has an `.exe` asset.
  - Verify release repository is public (or add auth logic if private).

- **Updater says up to date but should not**
  - Confirm app `AssemblyVersion`/`Version` and release tag format align (e.g., `1.0.1` / `v1.0.1`).

- **Config mode does not open elevated**
  - Run from a normal user session and pass `--config`; UAC prompt should appear.

- **Service not starting**
  - Re-run install script as administrator and verify `ConNexus.Service.exe` exists in publish/install location.

---

## Naming

User-visible naming in this repo should use **ConNexus**.


