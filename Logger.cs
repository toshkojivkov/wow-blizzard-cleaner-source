namespace WoWCleaner;

public sealed class Logger
{
    private readonly object syncRoot = new();

    public Logger()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        LogFilePath = Path.Combine(desktop, $"WoW_Killer_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        Info("Logger initialized.");
    }

    public string LogFilePath { get; }

    public event Action<string>? LineWritten;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        lock (syncRoot)
        {
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }

        LineWritten?.Invoke(line);
    }
}
