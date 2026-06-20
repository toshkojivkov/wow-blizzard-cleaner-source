using System.Diagnostics;

namespace WoWCleaner;

public sealed class ProcessKiller
{
    private readonly WhitelistManager whitelist;
    private readonly Logger logger;

    public ProcessKiller(WhitelistManager whitelist, Logger logger)
    {
        this.whitelist = whitelist;
        this.logger = logger;
    }

    public Task<List<CleanupTarget>> ScanAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var found = new List<CleanupTarget>();
            progress.Report("Scanning processes and services...");

            foreach (var process in Process.GetProcesses())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (whitelist.ProcessNameLooksLikeTarget(process.ProcessName))
                    {
                        found.Add(new CleanupTarget(CleanupTargetKind.Process, $"{process.ProcessName} ({process.Id})", "Running process"));
                        logger.Info($"Process trace found: {process.ProcessName} ({process.Id})");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn($"Could not inspect process: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            foreach (var service in QueryServices(cancellationToken))
            {
                if (whitelist.ServiceNameLooksLikeTarget(service.Name) || whitelist.ServiceNameLooksLikeTarget(service.DisplayName))
                {
                    found.Add(new CleanupTarget(CleanupTargetKind.Service, service.Name, $"Service: {service.DisplayName}"));
                    logger.Info($"Service trace found: {service.Name} ({service.DisplayName})");
                }
            }

            return found
                .GroupBy(target => target.Target, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(target => target.Kind)
                .ThenBy(target => target.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public Task<(int Processes, int Services)> StopAsync(IEnumerable<CleanupTarget> targets, IProgress<CleanupTarget> progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var processes = 0;
            var services = 0;

            foreach (var target in targets.Where(target => target.Kind == CleanupTargetKind.Process))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = ExtractProcessId(target.Target);
                if (id <= 0)
                {
                    target.Status = CleanupTargetStatus.Skipped;
                    target.Details = "Process id not found.";
                    progress.Report(target);
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(id);
                    if (!whitelist.ProcessNameLooksLikeTarget(process.ProcessName))
                    {
                        target.Status = CleanupTargetStatus.Skipped;
                        target.Details = "Process no longer matches safety filter.";
                        progress.Report(target);
                        continue;
                    }

                    process.CloseMainWindow();
                    if (!process.WaitForExit(2500))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }

                    target.Status = CleanupTargetStatus.Stopped;
                    target.Details = "Stopped.";
                    processes++;
                    logger.Info($"Stopped process: {target.Target}");
                    progress.Report(target);
                }
                catch (Exception ex)
                {
                    target.Status = CleanupTargetStatus.Failed;
                    target.Details = ex.Message;
                    logger.Warn($"Could not stop process {target.Target}: {ex.Message}");
                    progress.Report(target);
                }
            }

            foreach (var target in targets.Where(target => target.Kind == CleanupTargetKind.Service))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!whitelist.CanStopServices(out var adminReason))
                {
                    target.Status = CleanupTargetStatus.Skipped;
                    target.Details = adminReason;
                    logger.Warn($"Service stop skipped: {target.Target}. {adminReason}");
                    progress.Report(target);
                    continue;
                }

                if (!whitelist.ServiceNameLooksLikeTarget(target.Target) && !whitelist.ServiceNameLooksLikeTarget(target.Source))
                {
                    target.Status = CleanupTargetStatus.Skipped;
                    target.Details = "Service blocked by safety filter.";
                    progress.Report(target);
                    continue;
                }

                try
                {
                    RunProcess("sc.exe", $"stop \"{target.Target}\"", waitMilliseconds: 8000);
                    target.Status = CleanupTargetStatus.Stopped;
                    target.Details = "Stop command sent.";
                    services++;
                    logger.Info($"Stopped service: {target.Target}");
                    progress.Report(target);
                }
                catch (Exception ex)
                {
                    target.Status = CleanupTargetStatus.Failed;
                    target.Details = ex.Message;
                    logger.Warn($"Could not stop service {target.Target}: {ex.Message}");
                    progress.Report(target);
                }
            }

            return (processes, services);
        }, cancellationToken);
    }

    private static int ExtractProcessId(string text)
    {
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        return open >= 0 && close > open && int.TryParse(text[(open + 1)..close], out var id) ? id : -1;
    }

    private IEnumerable<ServiceInfo> QueryServices(CancellationToken cancellationToken)
    {
        var output = RunProcess("sc.exe", "queryex type= service state= all", waitMilliseconds: 15000);
        var currentName = "";
        var currentDisplay = "";

        foreach (var rawLine in output.Split(Environment.NewLine))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = rawLine.Trim();

            if (line.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(currentName))
                {
                    yield return new ServiceInfo(currentName, currentDisplay);
                }

                currentName = line["SERVICE_NAME:".Length..].Trim();
                currentDisplay = "";
            }
            else if (line.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                currentDisplay = line["DISPLAY_NAME:".Length..].Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(currentName))
        {
            yield return new ServiceInfo(currentName, currentDisplay);
        }
    }

    private static string RunProcess(string fileName, string arguments, int waitMilliseconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        if (!process.WaitForExit(waitMilliseconds))
        {
            process.Kill(entireProcessTree: true);
        }

        return process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    }

    private sealed record ServiceInfo(string Name, string DisplayName);
}
