namespace WoWCleaner;

static class Program
{
    private const string SingleInstanceMutexName = @"Local\WoWCleaner_SingleInstance_0D4E8B0A_7D94_4213_AA48_9B53B5E6F9C1";

    /// <summary>
    /// Main application entry point.
    /// </summary>
    [STAThread]
    static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            var runningCopies = CountRunningCopies();
            MessageBox.Show(
                $"WoW Blizzard Cleaner is already running.\n\nDetected running copies: {runningCopies}\nOnly one copy is allowed at a time.\n\nOpen the existing app from the taskbar or system tray.",
                "Already running",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }

    private static int CountRunningCopies()
    {
        try
        {
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            return System.Diagnostics.Process.GetProcessesByName(current.ProcessName).Length;
        }
        catch
        {
            return 1;
        }
    }
}
