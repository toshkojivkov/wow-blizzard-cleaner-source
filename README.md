# WoW Blizzard Cleaner

WoW Blizzard Cleaner is a Windows Forms desktop utility for reviewing and safely cleaning local World of Warcraft, Blizzard, Battle.net, cache, log, and configuration leftovers from a Windows PC.

The project is aimed at users who want a clear, guided way to remove old local traces after uninstalling, testing, or cleaning up WoW-related software and third-party tools. It is built for people who do not want to manually search through registry paths, AppData folders, ProgramData folders, logs, caches, and leftover configuration files.

The application follows a review-first workflow: scanning does not delete anything. Detected items are shown in a table, the user chooses what to clean, backups are created, and deletion requires confirmation.

## What It Does     

- Finds local WoW, Blizzard, and Battle.net registry entries from allowlisted locations.
- Finds local WoW, Blizzard, and Battle.net files/folders from allowlisted paths.
- Lists detected registry keys, files, folders, processes, and services before cleanup.
- Lets the user select or unselect each item before deletion.
- Creates registry and file backups before deleting anything.
- Provides restore support for `.reg` and `.zip` backups.
- Logs actions to a desktop text log.
- Shows live progress, percentage, and ETA during long operations.
- Includes tray support and single-instance protection.

## Main Features

- Dark loader-style Windows Forms interface.
- `SCAN` lists detected traces without deleting anything.
- `DELETE SELECTED` removes only checked rows after confirmation.
- `DELETE ALL` checks all available rows and still asks for confirmation.
- `PRE-CLEAN` runs scan, selects allowed targets, and asks for the same backup/deletion confirmation.
- `AUTO-CLEAN ON EXIT` watches for real WoW game processes to close, waits 30 seconds, cancels if WoW starts again, and then starts the same safe pre-clean workflow.
- Smart deletion with batching and retry logging for temporarily locked files/keys.
- Backup and restore support.
- Manual allowlisted target entry.
- Informational Ban Wave Check using supported public sources:
  - Reddit: `r/wow`, `r/classicwow`, `r/woweconomy`, `r/wowclassic`, `r/worldofwarcraft`
  - Blizzard Official WoW Forums

## Safety Model

This project is intentionally conservative and user-confirmed.

- Scan does not delete anything.
- Cleanup requires explicit user confirmation.
- Backup is created before deletion.
- Only checked rows are cleaned.
- Access-denied errors are logged and skipped.
- Windows, System32, Microsoft, Driver, Service, Kernel, Riot, and Vanguard paths are protected.
- The app is not designed to modify Blizzard services, hardware identifiers, machine identity, network identity, Windows security logs, or anti-cheat components.
- Ban Wave Check is informational only and does not modify the game or the system.

## Responsible Use

This utility is for local maintenance, uninstall cleanup, troubleshooting, and user-controlled removal of old WoW/Blizzard/Battle.net leftovers.

It is not an anti-ban guarantee, anti-cheat bypass, spoofing tool, account evasion tool, or botting support tool. Users are responsible for following the rules of any software, game, or service they use.

## Installation And Build Requirements

To build the application from source, install:

- Windows 10 or Windows 11.
- Visual Studio 2022 Community, Professional, or Enterprise.
- The `.NET desktop development` workload from Visual Studio Installer.
- .NET 8 SDK. Visual Studio 2022 usually installs this with the workload, but it can also be installed from Microsoft.

To run a self-contained published release, users do not need to install .NET. The portable release includes the runtime inside the published app.

## Build From Source With Visual Studio

1. Download or clone this repository.
2. Open `WoWCleanerClassic.sln` in Visual Studio 2022.
3. Wait for Visual Studio to restore the project.
4. Select `Debug` or `Release` configuration.
5. Click `Build` -> `Build Solution`.
6. Run the app with `Start Debugging` or open the generated executable from:

```text
bin\Debug\net8.0-windows\WoWCleaner.exe
```

or:

```text
bin\Release\net8.0-windows\WoWCleaner.exe
```

## Build From Source With The .NET SDK

Install the .NET 8 SDK, then run:

```powershell
dotnet restore WoWCleanerClassic.sln
dotnet build WoWCleanerClassic.sln --configuration Release
```

The project must be built on Windows because it uses Windows Forms and Windows-specific APIs.

## Publish A Portable Build

Use this command to create a portable Windows x64 build:

```powershell
dotnet publish WoWCleaner.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ReleasePackage
```

The published folder contains:

- `WoWCleaner.exe`
- `AppIcon.ico`
- optional debug symbols if generated

The resulting `WoWCleaner.exe` can be shared as a portable app. No installer is required.

## Project Structure

- `Program.cs` - application entry point and single-instance guard.
- `Form1.cs` - main UI, tabs, workflow, tray integration, progress, and events.
- `RegistryScanner.cs` - registry scan and cleanup logic.
- `FileScanner.cs` - file/folder scan and cleanup logic.
- `ProcessKiller.cs` - process and service scan/stop logic.
- `BackupManager.cs` - `.reg` and `.zip` backup/restore logic.
- `WhitelistManager.cs` - allowlist and safety rules.
- `BanWaveMonitor.cs` - informational public-source ban-wave signal checker.
- `Logger.cs` - desktop text log writer.
- `AppIcon.ico` / `AppIcon.png` - application icons.

## Important Notes

Some items may require Administrator rights, especially HKLM registry keys, Program Files paths, ProgramData paths, and Windows services. If Administrator rights are not available, the app logs and skips protected operations.

Always review the Results tab before deleting anything.

## License

This project is released under the MIT License. See `LICENSE` for details.
