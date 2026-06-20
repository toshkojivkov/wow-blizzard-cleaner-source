namespace WoWCleaner;

public sealed class FileScanner
{
    private readonly WhitelistManager whitelist;
    private readonly Logger logger;

    public FileScanner(WhitelistManager whitelist, Logger logger)
    {
        this.whitelist = whitelist;
        this.logger = logger;
    }

    public Task<List<CleanupTarget>> ScanAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var found = new List<CleanupTarget>();

            foreach (var root in whitelist.GetAllowedFileRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report($"Scanning files: {root}");

                if (File.Exists(root))
                {
                    AddFileIfAllowed(root, found);
                    continue;
                }

                if (!Directory.Exists(root))
                {
                    logger.Info($"File root not found: {root}");
                    continue;
                }

                if (HasAnyContent(root))
                {
                    AddDirectoryIfAllowed(root, found, "Allowlisted folder");
                }

                WalkDirectory(root, found, cancellationToken);
            }

            return found
                .GroupBy(target => Path.GetFullPath(target.Target), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(target => target.Kind)
                .ThenBy(target => target.Target, StringComparer.OrdinalIgnoreCase)
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
                logger.Info($"Smart deletion enabled for {deletionTargets.Count} file system targets.");
            }

            foreach (var batch in deletionTargets.Chunk(batchSize))
            {
                foreach (var target in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!whitelist.IsFileTargetAllowed(target.Target, out var reason))
                    {
                        target.Status = CleanupTargetStatus.Skipped;
                        target.Details = reason;
                        logger.Warn($"File deletion blocked: {target.Target}. {reason}");
                        progress.Report(target);
                        continue;
                    }

                    var attempts = smartDeletion ? 3 : 1;
                    for (var attempt = 1; attempt <= attempts; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            if (target.Kind == CleanupTargetKind.Directory)
                            {
                                if (!Directory.Exists(target.Target))
                                {
                                    target.Status = CleanupTargetStatus.Skipped;
                                    target.Details = "Folder not found.";
                                    progress.Report(target);
                                    break;
                                }

                                if (!HasAnyContent(target.Target))
                                {
                                    target.Status = CleanupTargetStatus.Skipped;
                                    target.Details = "Empty folder skipped.";
                                    logger.Info($"Empty folder skipped: {target.Target}");
                                    progress.Report(target);
                                    break;
                                }

                                Directory.Delete(target.Target, recursive: true);
                            }
                            else
                            {
                                if (!File.Exists(target.Target))
                                {
                                    target.Status = CleanupTargetStatus.Skipped;
                                    target.Details = "File not found.";
                                    progress.Report(target);
                                    break;
                                }

                                File.Delete(target.Target);
                            }

                            deleted++;
                            target.Status = CleanupTargetStatus.Deleted;
                            target.Details = "Deleted.";
                            logger.Info($"Deleted file system target: {target.Target}");
                            progress.Report(target);
                            break;
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            target.Status = CleanupTargetStatus.AccessDenied;
                            target.Details = ex.Message;
                            logger.Warn($"Access denied deleting {target.Target}: {ex.Message}");
                            progress.Report(target);
                            break;
                        }
                        catch (Exception ex) when (smartDeletion && attempt < attempts)
                        {
                            logger.Warn($"Retry {attempt}/{attempts} deleting {target.Target}: {ex.Message}");
                            SmartDelay(cancellationToken, 150, 450);
                        }
                        catch (Exception ex)
                        {
                            target.Status = CleanupTargetStatus.Failed;
                            target.Details = ex.Message;
                            logger.Error($"Failed deleting {target.Target}: {ex.Message}");
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
        var ordered = MinimalFileTargets(targets);
        if (!smartDeletion)
        {
            return ordered;
        }

        return ordered
            .OrderByDescending(target => target.Kind == CleanupTargetKind.File)
            .ThenByDescending(target => Path.GetFullPath(target.Target).Count(ch => ch == Path.DirectorySeparatorChar))
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

    private void WalkDirectory(string root, List<CleanupTarget> found, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var file in SafeEnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (whitelist.IsTargetFileName(file))
                {
                    AddFileIfAllowed(file, found);
                }
            }

            foreach (var child in SafeEnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (whitelist.IsWholeFolderName(child) && whitelist.IsInsideWowFolder(child) && HasAnyContent(child))
                {
                    AddDirectoryIfAllowed(child, found, "WoW folder match");
                }

                pending.Push(child);
            }
        }
    }

    private void AddFileIfAllowed(string path, List<CleanupTarget> found)
    {
        if (whitelist.IsFileTargetAllowed(path, out _))
        {
            found.Add(new CleanupTarget(CleanupTargetKind.File, Path.GetFullPath(path), "File mask"));
            logger.Info($"File trace found: {path}");
        }
    }

    private void AddDirectoryIfAllowed(string path, List<CleanupTarget> found, string source)
    {
        if (whitelist.IsFileTargetAllowed(path, out _))
        {
            found.Add(new CleanupTarget(CleanupTargetKind.Directory, Path.GetFullPath(path), source));
            logger.Info($"Folder trace found: {path}");
        }
    }

    private IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).ToArray();
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not enumerate files in {directory}: {ex.Message}");
            return [];
        }
    }

    private IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not enumerate folders in {directory}: {ex.Message}");
            return [];
        }
    }

    private bool HasAnyContent(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).Any();
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not inspect folder {directory}: {ex.Message}");
            return false;
        }
    }

    private static List<CleanupTarget> MinimalFileTargets(IEnumerable<CleanupTarget> targets)
    {
        var ordered = targets
            .Where(target => target.Kind is CleanupTargetKind.File or CleanupTargetKind.Directory)
            .DistinctBy(target => Path.GetFullPath(target.Target), StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => Path.GetFullPath(target.Target).Count(ch => ch == Path.DirectorySeparatorChar))
            .ToList();

        var result = new List<CleanupTarget>();
        foreach (var target in ordered)
        {
            if (!result.Any(parent => parent.Kind == CleanupTargetKind.Directory &&
                                      WhitelistManager.IsPathUnderRoot(target.Target, parent.Target)))
            {
                result.Add(target);
            }
        }

        return result;
    }
}
