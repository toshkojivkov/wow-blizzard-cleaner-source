using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace WoWCleaner;

public sealed class BackupManager
{
    private const string ManifestEntryName = "backup_manifest.txt";
    private readonly WhitelistManager whitelist;
    private readonly Logger logger;

    public BackupManager(WhitelistManager whitelist, Logger logger)
    {
        this.whitelist = whitelist;
        this.logger = logger;
    }

    public Task<(string RegistryBackupPath, string ZipBackupPath)> CreateBackupAsync(IEnumerable<CleanupTarget> targets, IProgress<string> progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var regPath = Path.Combine(whitelist.DesktopPath, $"Backup_{timestamp}.reg");
            var zipPath = Path.Combine(whitelist.DesktopPath, $"Backup_{timestamp}.zip");
            var targetList = targets.ToList();

            progress.Report("Creating registry backup...");
            CreateRegistryBackup(targetList, regPath, cancellationToken);
            logger.Info($"Created backup: {regPath}");

            progress.Report("Creating file backup zip...");
            CreateZipBackup(targetList, zipPath, cancellationToken);
            logger.Info($"Created backup: {zipPath}");

            return (regPath, zipPath);
        }, cancellationToken);
    }

    public Task RestoreAsync(string backupPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (backupPath.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
            {
                progress.Report("Restoring registry backup...");
                RunProcess("regedit.exe", $"/s \"{backupPath}\"", useShellExecute: true);
                logger.Info($"Restored registry backup: {backupPath}");
                return;
            }

            if (backupPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                progress.Report("Restoring file backup zip...");
                RestoreZip(backupPath, cancellationToken);
                logger.Info($"Restored file backup zip: {backupPath}");
            }
        }, cancellationToken);
    }

    private void CreateRegistryBackup(IReadOnlyList<CleanupTarget> targets, string regPath, CancellationToken cancellationToken)
    {
        File.WriteAllText(regPath, "Windows Registry Editor Version 5.00\r\n\r\n");
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"WoWCleanerReg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var registryTargets = MinimalRegistryTargets(targets).ToList();
            for (var index = 0; index < registryTargets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = registryTargets[index];

                if (!whitelist.IsRegistryTargetAllowed(target.Target, out var reason))
                {
                    logger.Warn($"Registry backup skipped by safety rules: {target.Target}. {reason}");
                    continue;
                }

                var tempFile = Path.Combine(tempDirectory, $"export_{index}.reg");
                try
                {
                    RunProcess("regedit.exe", $"/e \"{tempFile}\" \"{target.Target}\"", useShellExecute: true);
                    if (File.Exists(tempFile))
                    {
                        AppendRegWithoutHeader(regPath, tempFile);
                        target.Status = CleanupTargetStatus.BackedUp;
                        logger.Info($"Registry key backed up: {target.Target}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn($"Registry backup failed for {target.Target}: {ex.Message}");
                }
            }
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private void CreateZipBackup(IReadOnlyList<CleanupTarget> targets, string zipPath, CancellationToken cancellationToken)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var manifest = new StringBuilder();
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var index = 0;

        foreach (var target in MinimalFileTargets(targets))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!whitelist.IsFileTargetAllowed(target.Target, out var reason))
            {
                logger.Warn($"File backup skipped by safety rules: {target.Target}. {reason}");
                continue;
            }

            try
            {
                if (target.Kind == CleanupTargetKind.File && File.Exists(target.Target))
                {
                    var entryName = $"files/{index}/{Path.GetFileName(target.Target)}";
                    archive.CreateEntryFromFile(target.Target, entryName, CompressionLevel.Optimal);
                    manifest.AppendLine($"FILE|{Escape(target.Target)}|{Escape(entryName)}");
                    target.Status = CleanupTargetStatus.BackedUp;
                    logger.Info($"File backed up: {target.Target}");
                    index++;
                }
                else if (target.Kind == CleanupTargetKind.Directory && Directory.Exists(target.Target))
                {
                    var folderRoot = $"folders/{index}";
                    manifest.AppendLine($"DIR|{Escape(target.Target)}|{Escape(folderRoot)}");

                    foreach (var file in Directory.EnumerateFiles(target.Target, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(target.Target, file).Replace('\\', '/');
                        archive.CreateEntryFromFile(file, $"{folderRoot}/{relative}", CompressionLevel.Optimal);
                    }

                    target.Status = CleanupTargetStatus.BackedUp;
                    logger.Info($"Folder backed up: {target.Target}");
                    index++;
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"File backup failed for {target.Target}: {ex.Message}");
            }
        }

        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8);
        writer.Write(manifest.ToString());
    }

    private void RestoreZip(string zipPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry(ManifestEntryName);
        if (manifestEntry == null)
        {
            throw new InvalidOperationException("Backup manifest is missing.");
        }

        using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length != 3)
            {
                continue;
            }

            var kind = parts[0];
            var targetPath = Unescape(parts[1]);
            var entryRoot = Unescape(parts[2]);

            if (kind == "FILE")
            {
                var entry = archive.GetEntry(entryRoot);
                if (entry == null)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
            else if (kind == "DIR")
            {
                foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(entryRoot + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    var relative = entry.FullName[(entryRoot.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
                    var destination = Path.Combine(targetPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }
        }
    }

    private IEnumerable<CleanupTarget> MinimalRegistryTargets(IEnumerable<CleanupTarget> targets)
    {
        var ordered = targets
            .Where(target => target.Kind == CleanupTargetKind.RegistryKey)
            .DistinctBy(target => WhitelistManager.NormalizeRegistryPath(target.Target), StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => WhitelistManager.NormalizeRegistryPath(target.Target).Count(ch => ch == '\\'))
            .ToList();

        var result = new List<CleanupTarget>();
        foreach (var target in ordered)
        {
            if (!result.Any(parent => WhitelistManager.RegistryPathStartsWith(target.Target, parent.Target)))
            {
                result.Add(target);
            }
        }

        return result;
    }

    private IEnumerable<CleanupTarget> MinimalFileTargets(IEnumerable<CleanupTarget> targets)
    {
        var ordered = targets
            .Where(target => target.Kind is CleanupTargetKind.File or CleanupTargetKind.Directory)
            .DistinctBy(target => Path.GetFullPath(target.Target), StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => Path.GetFullPath(target.Target).Count(ch => ch == Path.DirectorySeparatorChar))
            .ToList();

        var result = new List<CleanupTarget>();
        foreach (var target in ordered)
        {
            if (!result.Any(parent => parent.Kind == CleanupTargetKind.Directory && WhitelistManager.IsPathUnderRoot(target.Target, parent.Target)))
            {
                result.Add(target);
            }
        }

        return result;
    }

    private static void AppendRegWithoutHeader(string destination, string source)
    {
        var lines = File.ReadAllLines(source);
        var content = lines.SkipWhile(line => !line.StartsWith("[", StringComparison.Ordinal)).ToArray();
        if (content.Length > 0)
        {
            File.AppendAllText(destination, string.Join(Environment.NewLine, content) + Environment.NewLine + Environment.NewLine);
        }
    }

    private static void RunProcess(string fileName, string arguments, bool useShellExecute)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = useShellExecute,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        process.Start();
        process.WaitForExit();
    }

    private static string Escape(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string Unescape(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Временната папка не трябва да прекъсва основната операция.
        }
    }
}
