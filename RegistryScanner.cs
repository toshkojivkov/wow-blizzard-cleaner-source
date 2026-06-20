using Microsoft.Win32;

namespace WoWCleaner;

public sealed class RegistryScanner
{
    private readonly WhitelistManager whitelist;
    private readonly Logger logger;

    public RegistryScanner(WhitelistManager whitelist, Logger logger)
    {
        this.whitelist = whitelist;
        this.logger = logger;
    }

    public Task<List<CleanupTarget>> ScanAsync(IProgress<string> progress, CancellationToken cancellationToken, bool deepRegistryScan)
    {
        return Task.Run(() =>
        {
            var found = new List<CleanupTarget>();

            foreach (var root in BuildRegistryScanRoots(deepRegistryScan))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report($"Scanning registry: {root}");
                ScanRegistryTree(root, found, cancellationToken);
            }

            return found
                .GroupBy(target => WhitelistManager.NormalizeRegistryPath(target.Target), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(target => target.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public Task<int> DeleteAsync(IEnumerable<CleanupTarget> targets, IProgress<CleanupTarget> progress, CancellationToken cancellationToken, bool smartDeletion = true)
    {
        return Task.Run(() =>
        {
            var deleted = 0;
            var deletionTargets = BuildDeletionOrder(targets, smartDeletion);
            var batchSize = smartDeletion ? 25 : Math.Max(1, deletionTargets.Count);

            if (smartDeletion)
            {
                logger.Info($"Smart deletion enabled for {deletionTargets.Count} registry targets.");
            }

            foreach (var batch in deletionTargets.Chunk(batchSize))
            {
                foreach (var target in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!whitelist.IsRegistryTargetAllowed(target.Target, out var reason))
                    {
                        target.Status = CleanupTargetStatus.Skipped;
                        target.Details = reason;
                        logger.Warn($"Registry deletion blocked: {target.Target}. {reason}");
                        progress.Report(target);
                        continue;
                    }

                    var attempts = smartDeletion ? 3 : 1;
                    for (var attempt = 1; attempt <= attempts; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            if (!TryParseRegistryPath(target.Target, out var hive, out var subPath))
                            {
                                target.Status = CleanupTargetStatus.Skipped;
                                target.Details = "Unsupported hive.";
                                progress.Report(target);
                                break;
                            }

                            var parentPath = GetParentPath(subPath);
                            var keyName = GetKeyName(subPath);
                            using var parent = hive.OpenSubKey(parentPath, writable: true);
                            if (parent == null)
                            {
                                target.Status = CleanupTargetStatus.Skipped;
                                target.Details = "Parent key not found.";
                                progress.Report(target);
                                break;
                            }

                            parent.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
                            target.Status = CleanupTargetStatus.Deleted;
                            target.Details = "Deleted.";
                            deleted++;
                            logger.Info($"Deleted registry key: {target.Target}");
                            progress.Report(target);
                            break;
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            target.Status = CleanupTargetStatus.AccessDenied;
                            target.Details = ex.Message;
                            logger.Warn($"Access denied deleting registry key {target.Target}: {ex.Message}");
                            progress.Report(target);
                            break;
                        }
                        catch (Exception ex) when (smartDeletion && attempt < attempts)
                        {
                            logger.Warn($"Retry {attempt}/{attempts} deleting registry key {target.Target}: {ex.Message}");
                            SmartDelay(cancellationToken, 150, 450);
                        }
                        catch (Exception ex)
                        {
                            target.Status = CleanupTargetStatus.Failed;
                            target.Details = ex.Message;
                            logger.Error($"Failed deleting registry key {target.Target}: {ex.Message}");
                            progress.Report(target);
                            break;
                        }
                    }
                }

                if (smartDeletion)
                {
                    SmartDelay(cancellationToken, 80, 180);
                }
            }

            return deleted;
        }, cancellationToken);
    }

    private static List<CleanupTarget> BuildDeletionOrder(IEnumerable<CleanupTarget> targets, bool smartDeletion)
    {
        var ordered = MinimalRegistryTargets(targets);
        if (!smartDeletion)
        {
            return ordered;
        }

        return ordered
            .OrderByDescending(target => WhitelistManager.NormalizeRegistryPath(target.Target).Count(ch => ch == '\\'))
            .ThenBy(target => target.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SmartDelay(CancellationToken cancellationToken, int minMilliseconds, int maxMilliseconds)
    {
        var delay = Random.Shared.Next(minMilliseconds, maxMilliseconds + 1);
        if (cancellationToken.WaitHandle.WaitOne(delay))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private IEnumerable<string> BuildRegistryScanRoots(bool deepRegistryScan)
    {
        foreach (var root in whitelist.RegistryRoots)
        {
            yield return root;
        }

        foreach (var sid in GetUsersSubKeys())
        {
            yield return $@"HKEY_USERS\{sid}\Software\Blizzard Entertainment";
            yield return $@"HKEY_USERS\{sid}\Software\Battle.net";
            yield return $@"HKEY_USERS\{sid}\Software\World of Warcraft";
        }

        // Динамичното търсене е ограничено до Software зоните и пак минава през allowlist преди триене.
        if (!deepRegistryScan)
        {
            yield break;
        }

        yield return @"HKEY_CURRENT_USER\Software";
        yield return @"HKEY_LOCAL_MACHINE\SOFTWARE";
        foreach (var sid in GetUsersSubKeys())
        {
            yield return $@"HKEY_USERS\{sid}\Software";
        }
    }

    private void ScanRegistryTree(string rootPath, List<CleanupTarget> found, CancellationToken cancellationToken)
    {
        if (!whitelist.IsRegistryScanRootAllowed(rootPath))
        {
            logger.Warn($"Registry scan root skipped by safety rules: {rootPath}");
            return;
        }

        if (!TryParseRegistryPath(rootPath, out var hive, out var subPath))
        {
            return;
        }

        using var rootKey = SafeOpen(hive, subPath, rootPath);
        if (rootKey == null)
        {
            return;
        }

        Walk(rootPath, rootKey, found, cancellationToken);
    }

    private void Walk(string fullPath, RegistryKey key, List<CleanupTarget> found, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keyMatches = whitelist.IsWarcraftRegistryMatch(fullPath) || ValuesMatch(key);
        if (keyMatches && whitelist.IsRegistryTargetAllowed(fullPath, out _))
        {
            found.Add(new CleanupTarget(CleanupTargetKind.RegistryKey, fullPath, "Registry match"));
            logger.Info($"Registry trace found: {fullPath}");
        }

        foreach (var childName in SafeGetSubKeyNames(key, fullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childPath = $@"{fullPath}\{childName}";

            if (!whitelist.IsRegistryScanRootAllowed(childPath))
            {
                continue;
            }

            using var child = SafeOpen(key, childName, childPath);
            if (child != null)
            {
                Walk(childPath, child, found, cancellationToken);
            }
        }
    }

    private bool ValuesMatch(RegistryKey key)
    {
        try
        {
            foreach (var valueName in key.GetValueNames())
            {
                if (whitelist.IsWarcraftRegistryMatch(valueName))
                {
                    return true;
                }

                var value = key.GetValue(valueName);
                if (value is string text && whitelist.IsWarcraftRegistryMatch(text))
                {
                    return true;
                }

                if (value is string[] lines && lines.Any(whitelist.IsWarcraftRegistryMatch))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not read registry values from {key.Name}: {ex.Message}");
        }

        return false;
    }

    private IEnumerable<string> GetUsersSubKeys()
    {
        try
        {
            return Registry.Users.GetSubKeyNames()
                .Where(name => !name.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not enumerate HKEY_USERS: {ex.Message}");
            return [];
        }
    }

    private IEnumerable<string> SafeGetSubKeyNames(RegistryKey key, string path)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Warn($"Access denied reading subkeys of {path}: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not read subkeys of {path}: {ex.Message}");
            return [];
        }
    }

    private RegistryKey? SafeOpen(RegistryKey parent, string subPath, string displayPath)
    {
        try
        {
            return parent.OpenSubKey(subPath, writable: false);
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not open registry key {displayPath}: {ex.Message}");
            return null;
        }
    }

    private static List<CleanupTarget> MinimalRegistryTargets(IEnumerable<CleanupTarget> targets)
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

    private static bool TryParseRegistryPath(string path, out RegistryKey hive, out string subPath)
    {
        hive = Registry.CurrentUser;
        subPath = "";
        var normalized = WhitelistManager.NormalizeRegistryPath(path);
        var separator = normalized.IndexOf('\\');
        if (separator <= 0 || separator >= normalized.Length - 1)
        {
            return false;
        }

        var hiveName = normalized[..separator];
        subPath = normalized[(separator + 1)..];
        hive = hiveName.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_USERS" => Registry.Users,
            _ => Registry.CurrentUser
        };

        return hiveName is "HKEY_CURRENT_USER" or "HKEY_LOCAL_MACHINE" or "HKEY_USERS";
    }

    private static string GetParentPath(string subPath)
    {
        var index = subPath.LastIndexOf('\\');
        return index < 0 ? "" : subPath[..index];
    }

    private static string GetKeyName(string subPath)
    {
        var index = subPath.LastIndexOf('\\');
        return index < 0 ? subPath : subPath[(index + 1)..];
    }
}
