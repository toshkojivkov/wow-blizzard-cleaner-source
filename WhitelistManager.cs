using System.Text.RegularExpressions;
using System.Security.Principal;

namespace WoWCleaner;

public enum CleanupTargetKind
{
    RegistryKey,
    File,
    Directory,
    Process,
    Service
}

public enum CleanupTargetStatus
{
    Found,
    BackedUp,
    Deleted,
    Stopped,
    Skipped,
    Failed,
    AccessDenied
}

public sealed record CleanupTarget(CleanupTargetKind Kind, string Target, string Source)
{
    public bool IsSelected { get; set; }
    public CleanupTargetStatus Status { get; set; } = CleanupTargetStatus.Found;
    public string Details { get; set; } = "";

    public string TypeName => Kind switch
    {
        CleanupTargetKind.RegistryKey => "Registry",
        CleanupTargetKind.File => "File",
        CleanupTargetKind.Directory => "Folder",
        CleanupTargetKind.Process => "Process",
        CleanupTargetKind.Service => "Service",
        _ => "Unknown"
    };
}

public sealed class CleanupReport
{
    public int RegistryDeleted { get; set; }
    public int FileSystemDeleted { get; set; }
    public int ProcessesStopped { get; set; }
    public int ServicesStopped { get; set; }
    public int Skipped { get; set; }
    public string RegistryBackupPath { get; set; } = "";
    public string ZipBackupPath { get; set; } = "";
    public string LogFilePath { get; set; } = "";
    public TimeSpan Elapsed { get; set; }
}

public sealed class WhitelistManager
{
    public string[] RegistryRoots { get; } =
    [
        @"HKEY_CURRENT_USER\Software\Blizzard Entertainment",
        @"HKEY_CURRENT_USER\Software\Battle.net",
        @"HKEY_CURRENT_USER\Software\World of Warcraft",
        @"HKEY_CURRENT_USER\Software\Wow",
        @"HKEY_CURRENT_USER\Software\Warcraft",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Blizzard Entertainment",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Battle.net",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Blizzard Entertainment",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Battle.net"
    ];

    public string[] BlockedRegistryPrefixes { get; } =
    [
        @"HKEY_LOCAL_MACHINE\SYSTEM",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT",
        @"HKEY_CURRENT_USER\Control Panel",
        @"HKEY_CURRENT_USER\Environment",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows"
    ];

    public string[] BlockedNameWords { get; } =
    [
        "Microsoft",
        "Windows",
        "System32",
        "Driver",
        "Service",
        "Kernel"
    ];

    public string[] WarcraftTerms { get; } =
    [
        "warcraft",
        "world of warcraft",
        "blizzard",
        "battle.net",
        "battlenet",
        "wow.exe",
        "agent.exe",
        "warden",
        "wowclassic",
        "dragonflight",
        "the war within"
    ];

    public string[] FileMasks { get; } =
    [
        ".log",
        ".dat",
        ".cache",
        ".tmp",
        ".lock",
        ".blob",
        ".idx",
        ".wtf",
        ".lua",
        ".toc",
        ".mpq",
        ".db",
        ".wdb",
        ".ini"
    ];

    public string[] WholeFolderNames { get; } =
    [
        "WTF",
        "Cache",
        "Logs",
        "Errors",
        "Account",
        "Screenshots",
        "_retail_",
        "_classic_",
        "_ptr_",
        "_beta_"
    ];

    public string[] ExactProcessNames { get; } =
    [
        "Wow",
        "Wow-64",
        "World of Warcraft",
        "Battle.net",
        "Blizzard",
        "BlizzardUpdate",
        "Agent",
        "CrashHandler",
        "WoWClassic",
        "WoWPTR"
    ];

    public string[] DynamicNameTerms { get; } =
    [
        "warcraft",
        "wow",
        "blizzard",
        "battle.net"
    ];

    public string DesktopPath { get; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public IReadOnlyList<string> GetAllowedFileRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var temp = Path.GetTempPath();

        var roots = new List<string>
        {
            Expand(Environment.SpecialFolder.ApplicationData, "Blizzard Entertainment"),
            Expand(Environment.SpecialFolder.ApplicationData, "Battle.net"),
            Expand(Environment.SpecialFolder.ApplicationData, "World of Warcraft"),
            Expand(Environment.SpecialFolder.LocalApplicationData, "Blizzard Entertainment"),
            Expand(Environment.SpecialFolder.LocalApplicationData, "Battle.net"),
            Expand(Environment.SpecialFolder.LocalApplicationData, "World of Warcraft"),
            Expand(Environment.SpecialFolder.CommonApplicationData, "Blizzard Entertainment"),
            Expand(Environment.SpecialFolder.CommonApplicationData, "Battle.net"),
            Path.Combine(userProfile, "Documents", "World of Warcraft"),
            Path.Combine(userProfile, "Saved Games", "World of Warcraft"),
            Path.Combine(programFilesX86, "World of Warcraft"),
            Path.Combine(programFilesX86, "Blizzard App"),
            Path.Combine(programFilesX86, "Battle.net"),
            Path.Combine(programFiles, "World of Warcraft")
        };

        roots.AddRange(Directory.Exists(temp)
            ? Directory.EnumerateFileSystemEntries(temp)
                .Where(path => FileSystemNameLooksLikeWarcraft(path))
            : []);

        return roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool IsRegistryTargetAllowed(string path, out string reason)
    {
        var normalized = NormalizeRegistryPath(path);

        if (!IsAdministrator() && normalized.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Requires Administrator; skipped in normal mode.";
            return false;
        }

        if (BlockedRegistryPrefixes.Any(prefix => RegistryPathStartsWith(normalized, prefix)))
        {
            reason = "Protected registry prefix.";
            return false;
        }

        if (ContainsBlockedName(normalized))
        {
            reason = "Protected word in registry path.";
            return false;
        }

        if (normalized.Contains(@"\Riot Games", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(@"\Vanguard", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Riot/Vanguard is protected.";
            return false;
        }

        var isAllowedRoot = RegistryRoots.Any(root => RegistryPathStartsWith(normalized, root));
        var isUsersBlizzard = Regex.IsMatch(normalized, @"^HKEY_USERS\\[^\\]+\\Software\\(Blizzard Entertainment|Battle\.net|World of Warcraft)(\\|$)", RegexOptions.IgnoreCase);
        var isDeepWarcraftMatch = IsWarcraftRegistryMatch(normalized);

        if (!isAllowedRoot && !isUsersBlizzard && !isDeepWarcraftMatch)
        {
            reason = "Registry key is outside the WoW/Blizzard allowlist.";
            return false;
        }

        reason = "OK";
        return true;
    }

    public bool IsRegistryScanRootAllowed(string path)
    {
        var normalized = NormalizeRegistryPath(path);
        if (!IsAdministrator() && normalized.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !BlockedRegistryPrefixes.Any(prefix => RegistryPathStartsWith(normalized, prefix)) &&
               !ContainsBlockedName(normalized) &&
               !normalized.Contains(@"\Riot Games", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(@"\Vanguard", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsFileTargetAllowed(string path, out string reason)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

        if (!IsAdministrator() && RequiresAdministratorForFilePath(fullPath))
        {
            reason = "Requires Administrator; skipped in normal mode.";
            return false;
        }

        if (ContainsBlockedName(fullPath))
        {
            reason = "Protected word in file path.";
            return false;
        }

        if (fullPath.Contains("Riot Games", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains("Vanguard", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Riot/Vanguard is protected.";
            return false;
        }

        if (!GetAllowedFileRoots().Any(root => SamePath(fullPath, root) || IsPathUnderRoot(fullPath, root)))
        {
            reason = "File path is outside the WoW/Blizzard allowlist.";
            return false;
        }

        reason = "OK";
        return true;
    }

    public bool IsWarcraftRegistryMatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace('_', ' ');
        if (WarcraftTerms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return Regex.IsMatch(normalized, @"(^|[^a-z0-9])wow([^a-z0-9]|$)", RegexOptions.IgnoreCase) &&
               !normalized.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTargetFileName(string path)
    {
        var name = Path.GetFileName(path);
        return FileMasks.Any(mask => name.EndsWith(mask, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsWholeFolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return WholeFolderNames.Any(folder => string.Equals(folder, name, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsInsideWowFolder(string path)
    {
        return path.Contains("World of Warcraft", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}Warcraft{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    public bool ProcessNameLooksLikeTarget(string processName)
    {
        return ExactProcessNames.Any(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase)) ||
               DynamicNameTerms.Any(term => processName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public bool ServiceNameLooksLikeTarget(string serviceName)
    {
        return serviceName.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) ||
               serviceName.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(serviceName, @"(^|[^a-z0-9])WoW([^a-z0-9]|$)", RegexOptions.IgnoreCase);
    }

    public bool CanStopServices(out string reason)
    {
        if (IsAdministrator())
        {
            reason = "OK";
            return true;
        }

        reason = "Requires Administrator; skipped in normal mode.";
        return false;
    }

    public static string NormalizeRegistryPath(string path)
    {
        var normalized = path.Trim().Trim('\\').Replace('/', '\\');

        if (normalized.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"HKEY_CURRENT_USER\" + normalized[5..];
        }
        else if (normalized.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"HKEY_LOCAL_MACHINE\" + normalized[5..];
        }
        else if (normalized.StartsWith(@"HKU\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"HKEY_USERS\" + normalized[4..];
        }

        while (normalized.Contains(@"\\", StringComparison.Ordinal))
        {
            normalized = normalized.Replace(@"\\", @"\");
        }

        return normalized;
    }

    public static bool RegistryPathStartsWith(string path, string prefix)
    {
        var normalizedPath = NormalizeRegistryPath(path);
        var normalizedPrefix = NormalizeRegistryPath(prefix);
        return string.Equals(normalizedPath, normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedPrefix + @"\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool ContainsBlockedName(string path)
    {
        return BlockedNameWords.Any(word => path.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private bool FileSystemNameLooksLikeWarcraft(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains("wow", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("blizzard", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("warcraft", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresAdministratorForFilePath(string path)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return IsProtectedRoot(path, programFiles) ||
               IsProtectedRoot(path, programFilesX86) ||
               IsProtectedRoot(path, programData);
    }

    private static bool IsProtectedRoot(string path, string root)
    {
        return !string.IsNullOrWhiteSpace(root) &&
               (SamePath(path, root) || IsPathUnderRoot(path, root));
    }

    private static string Expand(Environment.SpecialFolder specialFolder, string child)
    {
        return Path.GetFullPath(Path.Combine(Environment.GetFolderPath(specialFolder), child));
    }
}
