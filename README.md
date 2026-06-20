# WoW Blizzard Cleaner

WoW Blizzard Cleaner is a Windows Forms utility for reviewing and safely cleaning local World of Warcraft, Blizzard, and Battle.net traces from a Windows PC.

The app is designed around a review-first workflow: scanning never deletes anything. Items are listed in a grid, the user chooses what to clean, backups are created, and deletion requires confirmation.

## Features

- Dark Windows Forms loader-style interface.
- Scan-only workflow before any cleanup.
- Per-item checkbox selection in the Results tab.
- `DELETE SELECTED` for checked rows only.
- `DELETE ALL` selects available rows and still requires confirmation.
- `PRE-CLEAN` workflow that scans, selects allowed targets, and asks for confirmation.
- Optional `AUTO-CLEAN ON EXIT` that watches for real WoW game processes to close, waits 30 seconds, cancels if WoW starts again, and then starts the same safe pre-clean workflow.
- Registry scanning for WoW, Blizzard, and Battle.net allowlisted locations.
- Optional deep registry scan with safety filters.
- File scanning for WoW, Blizzard, and Battle.net allowlisted folders.
- Process and service detection for WoW, Blizzard, and Battle.net names.
- Smart deletion with batching, retry logging, and access-denied handling.
- `.reg` registry backup before registry deletion.
- `.zip` backup before file/folder deletion.
- Restore from `.reg` and `.zip` backups.
- Live log view.
- Progress percentage and ETA shown in the progress panel and footer.
- Tray icon with Show, Auto-Clean ON/OFF, and Exit.
- Single-instance protection so only one copy can run at a time.
- Informational Ban Wave Check using supported public sources:
  - Reddit: `r/wow`, `r/classicwow`, `r/woweconomy`, `r/wowclassic`, `r/worldofwarcraft`
  - Blizzard Official WoW Forums

## Safety Model

The project is intentionally conservative.

- Scan does not delete anything.
- Cleanup requires explicit user confirmation.
- Backup is created before deletion.
- Only checked rows are cleaned.
- Windows, System32, Microsoft, Driver, Service, Kernel, Riot, and Vanguard paths are protected.
- Access-denied errors are logged and skipped.
- The app is not an anti-ban, bypass, spoofing, evasion, or anti-detection tool.
- Ban Wave Check is informational only and does not modify the game, Blizzard services, hardware identifiers, or network identity.

## Requirements

- Windows 10 or Windows 11.
- Visual Studio 2022 or newer.
- .NET 8 Windows Desktop workload.

## Build

Open `WoWCleanerClassic.sln` in Visual Studio 2022 and build the solution.

Or build from a terminal:

```powershell
dotnet build WoWCleanerClassic.sln
```

## Publish A Portable Build

```powershell
dotnet publish WoWCleaner.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ReleasePackage
```

The published folder contains the portable executable and `AppIcon.ico`.

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
