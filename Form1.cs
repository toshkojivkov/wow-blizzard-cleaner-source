using System.Diagnostics;

namespace WoWCleaner;

public partial class Form1 : Form
{
    private static readonly TimeSpan AutoCleanDebounce = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AutoCleanCooldown = TimeSpan.FromMinutes(5);

    private readonly WhitelistManager whitelist = new();
    private readonly Logger logger = new();
    private readonly RegistryScanner registryScanner;
    private readonly FileScanner fileScanner;
    private readonly ProcessKiller processKiller;
    private readonly BackupManager backupManager;
    private readonly BanWaveMonitor banWaveMonitor;
    private readonly List<CleanupTarget> targets = [];
    private readonly System.Windows.Forms.Timer progressTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer autoCleanTimer = new() { Interval = 5000 };
    private readonly Stopwatch stopwatch = new();

    private readonly Color backgroundColor = Color.FromArgb(16, 17, 18);
    private readonly Color panelColor = Color.FromArgb(24, 25, 27);
    private readonly Color headerColor = Color.FromArgb(119, 53, 54);
    private readonly Color accentColor = Color.FromArgb(164, 65, 68);
    private readonly Color mutedButtonColor = Color.FromArgb(59, 68, 82);
    private readonly Color textColor = Color.FromArgb(232, 232, 232);
    private readonly Color mutedTextColor = Color.FromArgb(168, 174, 182);

    private CancellationTokenSource? cancellationTokenSource;
    private bool isBusy;
    private bool allowClose;
    private bool restartAvailable;
    private bool autoCleanEnabled;
    private bool autoCleanPending;
    private bool wowWasRunning;
    private DateTime lastAutoCleanRunUtc = DateTime.MinValue;

    private Button scanButton = null!;
    private Button preCleanButton = null!;
    private Button deleteSelectedButton = null!;
    private Button deleteAllButton = null!;
    private Button restoreButton = null!;
    private Button restartButton = null!;
    private Button banWaveButton = null!;
    private Button autoCleanButton = null!;
    private Button selectAllButton = null!;
    private Button unselectAllButton = null!;
    private Button addManualButton = null!;
    private TextBox manualTargetTextBox = null!;
    private ComboBox manualTypeComboBox = null!;
    private DataGridView resultsGrid = null!;
    private RichTextBox logBox = null!;
    private ProgressBar progressBar = null!;
    private Label progressInfoLabel = null!;
    private Label statusLabel = null!;
    private Label foundLabel = null!;
    private Label deletedLabel = null!;
    private Label skippedLabel = null!;
    private Label selectedLabel = null!;
    private Label footerStatusLabel = null!;
    private Label footerProgressLabel = null!;
    private CheckBox deepRegistryCheckBox = null!;
    private CheckBox smartDeletionCheckBox = null!;
    private NotifyIcon trayIcon = null!;
    private ToolStripMenuItem autoCleanTrayMenuItem = null!;
    private TabControl loaderTabs = null!;

    public Form1()
    {
        registryScanner = new RegistryScanner(whitelist, logger);
        fileScanner = new FileScanner(whitelist, logger);
        processKiller = new ProcessKiller(whitelist, logger);
        backupManager = new BackupManager(whitelist, logger);
        banWaveMonitor = new BanWaveMonitor(logger);

        InitializeComponent();
        BuildInterface();
        WireEvents();
        logger.Info("Application started.");
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        trayIcon.Visible = false;
        autoCleanTimer.Stop();
        base.OnFormClosing(e);
    }

    private void BuildInterface()
    {
        Text = "WoW Blizzard Cleaner";
        Icon = LoadApplicationIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimumSize = new Size(860, 860);
        Size = new Size(860, 860);
        BackColor = backgroundColor;
        ForeColor = textColor;
        Font = new Font("Segoe UI", 9.5F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = backgroundColor,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);

        loaderTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(118, 34),
            SizeMode = TabSizeMode.Fixed,
            Padding = new Point(12, 6),
            BackColor = backgroundColor,
            ForeColor = textColor
        };
        loaderTabs.DrawItem += DrawLoaderTab;
        loaderTabs.TabPages.Add(BuildDashboardTab());
        loaderTabs.TabPages.Add(BuildResultsTab());
        loaderTabs.TabPages.Add(BuildManualTab());
        loaderTabs.TabPages.Add(BuildBackupTab());
        loaderTabs.TabPages.Add(BuildLogTab());
        loaderTabs.TabPages.Add(BuildAboutTab());
        root.Controls.Add(loaderTabs, 0, 1);

        root.Controls.Add(BuildFooter(), 0, 2);

        trayIcon = new NotifyIcon
        {
            Text = "WoW Blizzard Cleaner",
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };

        UpdateAutoCleanUi();
        UpdateActionButtons();
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = headerColor,
            Padding = new Padding(14, 0, 14, 0)
        };

        var iconBox = new PictureBox
        {
            Image = Icon?.ToBitmap() ?? SystemIcons.Shield.ToBitmap(),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Location = new Point(12, 10),
            Size = new Size(34, 34)
        };

        var title = new Label
        {
            Text = "WoW Cleaner | Loader",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(54, 14)
        };

        var subtitle = new Label
        {
            Text = "Safe checked-row cleanup",
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(235, 208, 208),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(670, 18)
        };

        header.Controls.Add(iconBox);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        return header;
    }

    private TabPage BuildDashboardTab()
    {
        var tab = CreateTab("Main");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 16),
            BackColor = backgroundColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(layout);

        var actions = CreatePanel("Actions");
        var actionGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(12), BackColor = panelColor };
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        scanButton = CreateMainButton("SCAN", Color.FromArgb(45, 115, 205));
        preCleanButton = CreateMainButton("PRE-CLEAN", Color.FromArgb(52, 119, 88));
        deleteSelectedButton = CreateMainButton("DELETE SELECTED", accentColor);
        deleteAllButton = CreateMainButton("DELETE ALL", Color.FromArgb(130, 47, 52));
        restoreButton = CreateMainButton("RESTORE", mutedButtonColor);
        restartButton = CreateMainButton("RESTART", Color.FromArgb(76, 83, 94));
        banWaveButton = CreateMainButton("BAN WAVE CHECK", Color.FromArgb(73, 81, 96));
        autoCleanButton = CreateMainButton("AUTO-CLEAN OFF", Color.FromArgb(66, 72, 84));
        deepRegistryCheckBox = new CheckBox
        {
            Text = "Deep registry scan",
            Dock = DockStyle.Fill,
            ForeColor = textColor,
            BackColor = panelColor,
            CheckAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 0, 0)
        };
        smartDeletionCheckBox = new CheckBox
        {
            Text = "Smart deletion (retry + batches)",
            Dock = DockStyle.Fill,
            ForeColor = textColor,
            BackColor = panelColor,
            CheckAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft,
            Checked = true,
            Padding = new Padding(0)
        };
        actionGrid.Controls.Add(scanButton, 0, 0);
        actionGrid.Controls.Add(preCleanButton, 1, 0);
        actionGrid.Controls.Add(deleteSelectedButton, 2, 0);
        actionGrid.Controls.Add(deleteAllButton, 3, 0);
        actionGrid.Controls.Add(restoreButton, 0, 1);
        actionGrid.Controls.Add(restartButton, 1, 1);
        actionGrid.Controls.Add(banWaveButton, 2, 1);
        actionGrid.Controls.Add(autoCleanButton, 3, 1);
        actionGrid.SetColumnSpan(deepRegistryCheckBox, 2);
        actionGrid.Controls.Add(deepRegistryCheckBox, 0, 2);
        actionGrid.SetColumnSpan(smartDeletionCheckBox, 2);
        actionGrid.Controls.Add(smartDeletionCheckBox, 2, 2);
        actions.Controls.Add(actionGrid);
        layout.Controls.Add(actions, 0, 0);

        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = backgroundColor };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.Controls.Add(middle, 0, 1);

        var summary = CreatePanel("Statistics");
        var summaryGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14), BackColor = panelColor };
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summaryGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        summaryGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        foundLabel = CreateStatLabel("Found: 0");
        selectedLabel = CreateStatLabel("Selected: 0");
        deletedLabel = CreateStatLabel("Deleted/Stopped: 0");
        skippedLabel = CreateStatLabel("Skipped/Failed: 0");
        summaryGrid.Controls.Add(foundLabel, 0, 0);
        summaryGrid.Controls.Add(selectedLabel, 1, 0);
        summaryGrid.Controls.Add(deletedLabel, 0, 1);
        summaryGrid.Controls.Add(skippedLabel, 1, 1);
        summary.Controls.Add(summaryGrid);
        middle.Controls.Add(summary, 0, 0);

        var statusPanel = CreatePanel("Progress");
        var progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16), BackColor = panelColor };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusLabel = CreateMutedLabel("Ready");
        progressBar = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
        progressInfoLabel = CreateMutedLabel("Progress: 0% | ETA --:--");
        progressInfoLabel.ForeColor = Color.White;
        progressInfoLabel.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        var safety = CreateMutedLabel("Protected: Windows, System32, Microsoft, Riot/Vanguard.");
        progressLayout.Controls.Add(statusLabel, 0, 0);
        progressLayout.Controls.Add(progressInfoLabel, 0, 1);
        progressLayout.Controls.Add(progressBar, 0, 2);
        progressLayout.Controls.Add(safety, 0, 3);
        statusPanel.Controls.Add(progressLayout);
        middle.Controls.Add(statusPanel, 1, 0);

        var selection = CreatePanel("Selection");
        var selectionGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12), BackColor = panelColor };
        selectionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        selectionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        selectionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        selectionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selectAllButton = CreateMainButton("SELECT ALL", Color.FromArgb(54, 89, 74));
        unselectAllButton = CreateMainButton("UNSELECT ALL", Color.FromArgb(70, 77, 91));
        var hint = CreateMutedLabel("SCAN only lists traces. Delete buttons only act on checked rows after backup and confirmation.");
        selectionGrid.Controls.Add(selectAllButton, 0, 0);
        selectionGrid.Controls.Add(unselectAllButton, 1, 0);
        selectionGrid.SetColumnSpan(hint, 2);
        selectionGrid.Controls.Add(hint, 0, 1);
        selection.Controls.Add(selectionGrid);
        layout.Controls.Add(selection, 0, 2);

        var preview = CreatePanel("Quick results");
        var quickLabel = CreateMutedLabel("Open the Results tab after scanning to review and check/uncheck each item.");
        quickLabel.Dock = DockStyle.Fill;
        preview.Controls.Add(quickLabel);
        layout.Controls.Add(preview, 0, 3);

        return tab;
    }

    private TabPage BuildResultsTab()
    {
        var tab = CreateTab("Results");
        var panel = CreatePanel("Detected traces");
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(10, 34, 10, 10);

        resultsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.FromArgb(18, 19, 21),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            EnableHeadersVisualStyles = false,
            GridColor = Color.FromArgb(45, 45, 48),
            ReadOnly = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        resultsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(34, 36, 40);
        resultsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        resultsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        resultsGrid.DefaultCellStyle.BackColor = Color.FromArgb(24, 25, 27);
        resultsGrid.DefaultCellStyle.ForeColor = textColor;
        resultsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(95, 54, 56);
        resultsGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        resultsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(29, 30, 32);
        resultsGrid.RowTemplate.Height = 28;
        resultsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Use", FillWeight = 7 });
        resultsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", FillWeight = 12, ReadOnly = true });
        resultsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Source", FillWeight = 16, ReadOnly = true });
        resultsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "Target", FillWeight = 45, ReadOnly = true });
        resultsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 10, ReadOnly = true });
        resultsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Details", FillWeight = 10, ReadOnly = true });
        panel.Controls.Add(resultsGrid);
        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage BuildManualTab()
    {
        var tab = CreateTab("Manual");
        var panel = CreatePanel("Manual allowlisted target");
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(18, 44, 18, 18);

        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 120, ColumnCount = 3, RowCount = 2, BackColor = panelColor };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

        manualTypeComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(35, 37, 41),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 0)
        };
        manualTypeComboBox.Items.AddRange(["Registry Key", "File or Folder"]);
        manualTypeComboBox.SelectedIndex = 0;

        manualTargetTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Manual WoW/Blizzard allowlisted target...",
            BackColor = Color.FromArgb(31, 32, 35),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 10, 0)
        };

        addManualButton = CreateSmallButton("+ ADD");
        var hint = CreateMutedLabel("Manual items are added checked, but still require DELETE SELECTED and confirmation.");
        layout.Controls.Add(manualTypeComboBox, 0, 0);
        layout.Controls.Add(manualTargetTextBox, 1, 0);
        layout.Controls.Add(addManualButton, 2, 0);
        layout.SetColumnSpan(hint, 3);
        layout.Controls.Add(hint, 0, 1);
        panel.Controls.Add(layout);
        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage BuildBackupTab()
    {
        var tab = CreateTab("Backup");
        var panel = CreatePanel("Backup and restore");
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(18, 44, 18, 18);

        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 160, ColumnCount = 2, RowCount = 2, BackColor = panelColor };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var restoreCopy = CreateMainButton("RESTORE BACKUP", mutedButtonColor);
        restoreCopy.Click += async (_, _) => await RestoreAsync();
        var restartCopy = CreateMainButton("RESTART PC", Color.FromArgb(76, 83, 94));
        restartCopy.Click += (_, _) => RestartComputer();
        var hint = CreateMutedLabel("Every cleanup creates a .reg backup and a .zip backup on Desktop before deletion.");
        layout.Controls.Add(restoreCopy, 0, 0);
        layout.Controls.Add(restartCopy, 1, 0);
        layout.SetColumnSpan(hint, 2);
        layout.Controls.Add(hint, 0, 1);
        panel.Controls.Add(layout);
        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage BuildLogTab()
    {
        var tab = CreateTab("Logs");
        var panel = CreatePanel("Live log");
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(10, 34, 10, 10);

        logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 11, 12),
            ForeColor = Color.FromArgb(155, 221, 171),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Font = new Font("Consolas", 9.5F)
        };
        panel.Controls.Add(logBox);
        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage BuildAboutTab()
    {
        var tab = CreateTab("About");
        var panel = CreatePanel("Safety model");
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(18, 44, 18, 18);
        var text = CreateMutedLabel(
            "This loader scans WoW/Blizzard/Battle.net traces and never deletes on SCAN.\r\n\r\n" +
            "Use checkboxes in Results, then DELETE SELECTED. DELETE ALL simply checks every available row and still asks for confirmation.\r\n\r\n" +
            "PRE-CLEAN scans, selects available rows, then asks for the same backup and deletion confirmation.\r\n\r\n" +
            "AUTO-CLEAN ON EXIT watches for real WoW game processes to close, waits 30 seconds, cancels if WoW starts again, then starts the same safe pre-clean workflow.\r\n\r\n" +
            "BAN WAVE CHECK only reads public community signals. It does not bypass, hide, spoof, or change the game.\r\n\r\n" +
            "Protected areas stay blocked: Windows, System32, Microsoft, Driver, Service, Kernel, Riot and Vanguard.");
        text.Dock = DockStyle.Fill;
        panel.Controls.Add(text);
        tab.Controls.Add(panel);
        return tab;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = Color.FromArgb(13, 14, 15)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

        var version = CreateMutedLabel("v1.0.0");
        version.TextAlign = ContentAlignment.MiddleCenter;
        footerStatusLabel = CreateMutedLabel("Ready");
        footerProgressLabel = CreateMutedLabel("Progress: 0% | ETA --:--");
        footerProgressLabel.TextAlign = ContentAlignment.MiddleRight;
        footerProgressLabel.ForeColor = Color.White;

        footer.Controls.Add(version, 0, 0);
        footer.Controls.Add(footerStatusLabel, 1, 0);
        footer.Controls.Add(footerProgressLabel, 2, 0);
        return footer;
    }

    private static Icon LoadApplicationIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        return System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    }

    private void WireEvents()
    {
        scanButton.Click += async (_, _) => await ScanAsync();
        preCleanButton.Click += async (_, _) => await PreCleanAsync(autoTriggered: false);
        deleteSelectedButton.Click += async (_, _) => await DeleteAsync(deleteAll: false);
        deleteAllButton.Click += async (_, _) =>
        {
            SetAllSelections(true);
            await DeleteAsync(deleteAll: true);
        };
        restoreButton.Click += async (_, _) => await RestoreAsync();
        restartButton.Click += (_, _) => RestartComputer();
        banWaveButton.Click += async (_, _) => await CheckBanWaveAsync();
        autoCleanButton.Click += (_, _) => ToggleAutoClean();
        selectAllButton.Click += (_, _) => SetAllSelections(true);
        unselectAllButton.Click += (_, _) => SetAllSelections(false);
        addManualButton.Click += (_, _) => AddManualTarget();
        progressTimer.Tick += (_, _) => AdvanceProgress();
        autoCleanTimer.Tick += async (_, _) => await AutoCleanTimerTickAsync();
        resultsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (resultsGrid.IsCurrentCellDirty)
            {
                resultsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        resultsGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && resultsGrid.Columns[e.ColumnIndex].Name == "Selected")
            {
                SyncSelectionFromGridRow(e.RowIndex);
                UpdateStats();
                UpdateActionButtons();
            }
        };
        logger.LineWritten += AppendLogLine;
        trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void AppendLogLine(string line)
    {
        if (IsDisposed || logBox.IsDisposed)
        {
            return;
        }

        if (logBox.InvokeRequired && logBox.IsHandleCreated)
        {
            logBox.BeginInvoke(() => AppendLogLine(line));
            return;
        }

        logBox.AppendText(line + Environment.NewLine);
        if (logBox.IsHandleCreated)
        {
            logBox.ScrollToCaret();
        }
    }

    private async Task ScanAsync()
    {
        if (isBusy)
        {
            cancellationTokenSource?.Cancel();
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;
        SetBusy(true, "Scanning...");
        ResetProgress();
        stopwatch.Restart();
        progressTimer.Start();
        targets.Clear();
        resultsGrid.Rows.Clear();
        UpdateActionButtons();

        try
        {
            var statusProgress = new Progress<string>(SetStatusText);
            logger.Info("Scan started.");

            var registry = await registryScanner.ScanAsync(statusProgress, token, deepRegistryCheckBox.Checked);
            var files = await fileScanner.ScanAsync(statusProgress, token);
            var processes = await processKiller.ScanAsync(statusProgress, token);

            targets.AddRange(registry);
            targets.AddRange(files);
            targets.AddRange(processes);
            foreach (var target in targets)
            {
                target.IsSelected = false;
            }

            RefreshGrid();
            UpdateStats();
            UpdateActionButtons();
            loaderTabs.SelectedIndex = 1;

            SetStatusText(targets.Count == 0
                ? "No WoW/Blizzard traces found."
                : $"Scan complete. Found {targets.Count} targets. Check only what you want to delete.");
            logger.Info($"Scan complete. Found {targets.Count} targets.");
        }
        catch (OperationCanceledException)
        {
            SetStatusText("Scan cancelled.");
            logger.Warn("Scan cancelled.");
        }
        finally
        {
            progressTimer.Stop();
            if (!token.IsCancellationRequested)
            {
                progressBar.Value = 100;
            }

            UpdateProgressInfo();
            stopwatch.Stop();
            SetBusy(false, statusLabel.Text);
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private async Task PreCleanAsync(bool autoTriggered)
    {
        if (isBusy)
        {
            return;
        }

        logger.Info(autoTriggered ? "Auto-clean pre-clean started after WoW exit." : "Pre-Clean started.");
        if (autoTriggered)
        {
            ShowFromTray();
            ShowTrayMessage("Auto-Clean", "WoW closed. A safe scan will start now and still requires confirmation.");
        }

        await ScanAsync();

        if (targets.Count == 0)
        {
            if (autoTriggered)
            {
                SetStatusText("Auto-clean scan complete. No targets found.");
            }

            return;
        }

        SetAllSelections(true);
        SetStatusText(autoTriggered
            ? "Auto-clean scan complete. Confirmation is required before deletion."
            : "Pre-Clean scan complete. All available targets selected.");
        await DeleteAsync(deleteAll: true);
    }

    private async Task CheckBanWaveAsync()
    {
        if (isBusy)
        {
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;
        SetBusy(true, "Checking public ban wave signals...");
        ResetProgress();
        stopwatch.Restart();
        progressTimer.Start();

        try
        {
            var report = await banWaveMonitor.CheckAsync(new Progress<string>(SetStatusText), token);
            progressBar.Value = 100;
            UpdateProgressInfo();
            logger.Info($"Ban wave check completed. Risk: {report.RiskLevel}, signals: {report.SignalCount}.");
            MessageBox.Show(report.ToDisplayText(), "Ban wave check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatusText($"Ban wave check complete: {report.RiskLevel}.");
        }
        catch (OperationCanceledException)
        {
            SetStatusText("Ban wave check cancelled.");
            logger.Warn("Ban wave check cancelled.");
        }
        catch (Exception ex)
        {
            SetStatusText("Ban wave check failed.");
            logger.Warn($"Ban wave check failed: {ex.Message}");
            MessageBox.Show($"Ban wave check failed:\n{ex.Message}", "Ban wave check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            stopwatch.Stop();
            progressTimer.Stop();
            SetBusy(false, statusLabel.Text);
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private void ToggleAutoClean()
    {
        autoCleanEnabled = !autoCleanEnabled;
        var snapshot = GetWowProcessSnapshot();
        wowWasRunning = snapshot.IsRunning;
        autoCleanPending = false;

        if (autoCleanEnabled)
        {
            autoCleanTimer.Start();
            UpdateAutoCleanUi();
            SetStatusText(snapshot.IsRunning
                ? $"Auto-clean armed. Watching: {snapshot.DisplayName}."
                : "Auto-clean armed. Waiting for WoW to start.");
            logger.Info($"Auto-clean on exit enabled. Initial WoW snapshot: {snapshot.DisplayName}.");
            ShowTrayMessage("Auto-Clean ON", statusLabel.Text);
            return;
        }

        autoCleanTimer.Stop();
        UpdateAutoCleanUi();
        SetStatusText("Auto-clean on exit disabled.");
        logger.Info("Auto-clean on exit disabled.");
        ShowTrayMessage("Auto-Clean OFF", "Automatic cleanup after WoW exit is disabled.");
    }

    private void UpdateAutoCleanUi()
    {
        if (autoCleanButton != null)
        {
            autoCleanButton.Text = autoCleanEnabled ? "AUTO-CLEAN ON" : "AUTO-CLEAN OFF";
            autoCleanButton.BackColor = autoCleanEnabled
                ? Color.FromArgb(52, 119, 88)
                : Color.FromArgb(66, 72, 84);
        }

        if (autoCleanTrayMenuItem != null)
        {
            autoCleanTrayMenuItem.Text = autoCleanEnabled ? "Auto-Clean: ON" : "Auto-Clean: OFF";
            autoCleanTrayMenuItem.Checked = autoCleanEnabled;
        }
    }

    private async Task AutoCleanTimerTickAsync()
    {
        if (!autoCleanEnabled || isBusy || autoCleanPending)
        {
            return;
        }

        var snapshot = GetWowProcessSnapshot();
        if (snapshot.IsRunning)
        {
            wowWasRunning = true;
            SetStatusText($"Auto-clean armed. Watching: {snapshot.DisplayName}.");
            return;
        }

        if (!wowWasRunning)
        {
            SetStatusText("Auto-clean armed. Waiting for WoW to start.");
            return;
        }

        if (DateTime.UtcNow - lastAutoCleanRunUtc < AutoCleanCooldown)
        {
            wowWasRunning = false;
            var remaining = AutoCleanCooldown - (DateTime.UtcNow - lastAutoCleanRunUtc);
            SetStatusText($"Auto-clean cooldown active ({remaining.Minutes:00}:{remaining.Seconds:00}).");
            logger.Info($"Auto-clean skipped by cooldown. Remaining: {remaining:mm\\:ss}.");
            return;
        }

        wowWasRunning = false;
        autoCleanPending = true;
        SetStatusText($"WoW closed. Auto-clean will scan in {(int)AutoCleanDebounce.TotalSeconds} seconds.");
        logger.Info($"WoW process closed. Auto-clean scan scheduled in {(int)AutoCleanDebounce.TotalSeconds} seconds.");
        ShowTrayMessage("Auto-Clean scheduled", statusLabel.Text);

        try
        {
            for (var remaining = (int)AutoCleanDebounce.TotalSeconds; remaining > 0; remaining -= 5)
            {
                if (!autoCleanEnabled)
                {
                    logger.Info("Auto-clean countdown cancelled because Auto-Clean was disabled.");
                    return;
                }

                if (GetWowProcessSnapshot().IsRunning)
                {
                    wowWasRunning = true;
                    SetStatusText("Auto-clean countdown cancelled because WoW started again.");
                    logger.Info("Auto-clean countdown cancelled because WoW started again.");
                    ShowTrayMessage("Auto-Clean cancelled", "WoW started again before cleanup.");
                    return;
                }

                SetStatusText($"WoW closed. Auto-clean scan in {remaining} seconds.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, remaining)));
            }

            if (autoCleanEnabled && !isBusy && !GetWowProcessSnapshot().IsRunning)
            {
                lastAutoCleanRunUtc = DateTime.UtcNow;
                await PreCleanAsync(autoTriggered: true);
            }
        }
        finally
        {
            autoCleanPending = false;
        }
    }

    private static WowProcessSnapshot GetWowProcessSnapshot()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var names = new List<string>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentProcess.Id)
                {
                    continue;
                }

                var name = process.ProcessName;
                if (IsWowGameProcessName(name))
                {
                    names.Add(name);
                }
            }
            catch
            {
                // Process disappeared while checking; ignore and continue.
            }
            finally
            {
                process.Dispose();
            }
        }

        return new WowProcessSnapshot(names.Count > 0, names.Count, names.Count == 0 ? "no WoW process" : string.Join(", ", names.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static bool IsWowGameProcessName(string processName)
    {
        var name = processName.Trim();
        var exactNames = new[]
        {
            "Wow",
            "Wow-64",
            "WoWClassic",
            "WoWPTR",
            "World of Warcraft"
        };

        if (exactNames.Any(exact => string.Equals(name, exact, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return name.Contains("warcraft", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("cleaner", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowTrayMessage(string title, string text)
    {
        if (trayIcon == null)
        {
            return;
        }

        trayIcon.BalloonTipTitle = title;
        trayIcon.BalloonTipText = text;
        trayIcon.ShowBalloonTip(2000);
    }

    private sealed record WowProcessSnapshot(bool IsRunning, int Count, string DisplayName);

    private async Task DeleteAsync(bool deleteAll)
    {
        if (targets.Count == 0)
        {
            MessageBox.Show("Run a scan first.", "WoW Cleaner", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedTargets = GetSelectedTargets();
        if (selectedTargets.Count == 0)
        {
            MessageBox.Show("Select at least one item first, or use Delete ALL.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var actionName = deleteAll ? "DELETE ALL" : "DELETE SELECTED";
        var confirm = MessageBox.Show(
            $"Action: {actionName}\n\nOnly checked rows will be cleaned.\n\nSelected: {selectedTargets.Count}\nRegistry: {selectedTargets.Count(t => t.Kind == CleanupTargetKind.RegistryKey)}\nFiles/Folders: {selectedTargets.Count(t => t.Kind is CleanupTargetKind.File or CleanupTargetKind.Directory)}\nProcesses/Services: {selectedTargets.Count(t => t.Kind is CleanupTargetKind.Process or CleanupTargetKind.Service)}\n\nBackups will be created on the Desktop before deletion.\nProceed?",
            "Confirm cleanup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes)
        {
            logger.Warn("Cleanup cancelled by user.");
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;
        var report = new CleanupReport { LogFilePath = logger.LogFilePath };
        var targetProgress = new Progress<CleanupTarget>(UpdateTargetRow);
        var statusProgress = new Progress<string>(SetStatusText);
        SetBusy(true, "Creating backup...");
        ResetProgress();
        progressTimer.Start();
        stopwatch.Restart();

        try
        {
            var backup = await backupManager.CreateBackupAsync(selectedTargets, statusProgress, token);
            report.RegistryBackupPath = backup.RegistryBackupPath;
            report.ZipBackupPath = backup.ZipBackupPath;

            SetStatusText("Stopping selected processes and services...");
            var stopped = await processKiller.StopAsync(selectedTargets, targetProgress, token);
            report.ProcessesStopped = stopped.Processes;
            report.ServicesStopped = stopped.Services;

            SetStatusText("Deleting selected registry keys...");
            report.RegistryDeleted = await registryScanner.DeleteAsync(selectedTargets, targetProgress, token, smartDeletionCheckBox.Checked);

            SetStatusText("Deleting selected files and folders...");
            report.FileSystemDeleted = await fileScanner.DeleteAsync(selectedTargets, targetProgress, token, smartDeletionCheckBox.Checked);

            stopwatch.Stop();
            report.Elapsed = stopwatch.Elapsed;
            report.Skipped = selectedTargets.Count(t => t.Status is CleanupTargetStatus.Skipped or CleanupTargetStatus.Failed or CleanupTargetStatus.AccessDenied);
            progressBar.Value = 100;
            UpdateProgressInfo();
            UpdateStats();
            restartAvailable = true;

            MessageBox.Show(
                $"Selected items: {selectedTargets.Count}\nRegistry keys deleted: {report.RegistryDeleted}\nFiles/folders deleted: {report.FileSystemDeleted}\nProcesses stopped: {report.ProcessesStopped}\nServices stopped: {report.ServicesStopped}\nRegistry backup: {report.RegistryBackupPath}\nZip backup: {report.ZipBackupPath}\nLog file: {report.LogFilePath}\nTotal time: {report.Elapsed:mm\\:ss}",
                "Final report",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            logger.Info("Cleanup completed.");
            SetStatusText("Cleanup complete.");
        }
        catch (OperationCanceledException)
        {
            SetStatusText("Cleanup cancelled.");
            logger.Warn("Cleanup cancelled.");
        }
        finally
        {
            stopwatch.Stop();
            progressTimer.Stop();
            SetBusy(false, statusLabel.Text);
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private async Task RestoreAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select backup to restore",
            Filter = "Backup files (*.reg;*.zip)|*.reg;*.zip|All files (*.*)|*.*",
            InitialDirectory = whitelist.DesktopPath
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var confirm = MessageBox.Show($"Restore this backup?\n\n{dialog.FileName}", "Confirm restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        SetBusy(true, "Restoring backup...");
        ResetProgress();
        progressTimer.Start();
        stopwatch.Restart();
        try
        {
            await backupManager.RestoreAsync(dialog.FileName, new Progress<string>(SetStatusText), cancellationTokenSource.Token);
            progressBar.Value = 100;
            UpdateProgressInfo();
            MessageBox.Show("Restore completed.", "Restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatusText("Restore complete.");
        }
        catch (Exception ex)
        {
            logger.Error($"Restore failed: {ex.Message}");
            MessageBox.Show($"Restore failed:\n{ex.Message}", "Restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            stopwatch.Stop();
            progressTimer.Stop();
            SetBusy(false, statusLabel.Text);
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private void AddManualTarget()
    {
        var input = manualTargetTextBox.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        CleanupTarget target;
        if (manualTypeComboBox.SelectedIndex == 0)
        {
            var normalized = WhitelistManager.NormalizeRegistryPath(input);
            if (!whitelist.IsRegistryTargetAllowed(normalized, out var reason))
            {
                MessageBox.Show(reason, "Blocked manual registry target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            target = new CleanupTarget(CleanupTargetKind.RegistryKey, normalized, "Manual");
        }
        else
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input));
            if (!whitelist.IsFileTargetAllowed(fullPath, out var reason))
            {
                MessageBox.Show(reason, "Blocked manual file target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            target = new CleanupTarget(Directory.Exists(fullPath) ? CleanupTargetKind.Directory : CleanupTargetKind.File, fullPath, "Manual");
        }

        target.IsSelected = true;
        targets.Add(target);
        manualTargetTextBox.Clear();
        RefreshGrid();
        UpdateStats();
        UpdateActionButtons();
        loaderTabs.SelectedIndex = 1;
        logger.Info($"Manual target added: {target.Target}");
    }

    private void RestartComputer()
    {
        var confirm = MessageBox.Show("Restart the computer in 5 seconds?", "Restart", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/r /t 5",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        logger.Info("Restart scheduled in 5 seconds.");
    }

    private void RefreshGrid()
    {
        resultsGrid.Rows.Clear();
        foreach (var target in targets.OrderBy(t => t.Kind).ThenBy(t => t.Target, StringComparer.OrdinalIgnoreCase))
        {
            resultsGrid.Rows.Add(target.IsSelected, target.TypeName, target.Source, target.Target, target.Status.ToString(), target.Details);
        }
    }

    private void UpdateTargetRow(CleanupTarget target)
    {
        foreach (DataGridViewRow row in resultsGrid.Rows)
        {
            if (string.Equals(row.Cells["Target"].Value?.ToString(), target.Target, StringComparison.OrdinalIgnoreCase))
            {
                row.Cells["Status"].Value = target.Status.ToString();
                row.Cells["Details"].Value = target.Details;
            }
        }

        UpdateStats();
    }

    private void UpdateStats()
    {
        foundLabel.Text = $"Found: {targets.Count}";
        selectedLabel.Text = $"Selected: {targets.Count(t => t.IsSelected)}";
        deletedLabel.Text = $"Deleted/Stopped: {targets.Count(t => t.Status is CleanupTargetStatus.Deleted or CleanupTargetStatus.Stopped)}";
        skippedLabel.Text = $"Skipped/Failed: {targets.Count(t => t.Status is CleanupTargetStatus.Skipped or CleanupTargetStatus.Failed or CleanupTargetStatus.AccessDenied)}";
    }

    private void SetBusy(bool busy, string status)
    {
        isBusy = busy;
        scanButton.Text = busy ? "STOP" : "SCAN";
        scanButton.BackColor = busy ? Color.FromArgb(177, 70, 70) : Color.FromArgb(45, 115, 205);
        restoreButton.Enabled = !busy;
        addManualButton.Enabled = !busy;
        manualTargetTextBox.Enabled = !busy;
        manualTypeComboBox.Enabled = !busy;
        deepRegistryCheckBox.Enabled = !busy;
        smartDeletionCheckBox.Enabled = !busy;
        resultsGrid.Enabled = !busy;
        SetStatusText(status);
        UpdateActionButtons();
    }

    private void SetStatusText(string status)
    {
        statusLabel.Text = status;
        if (footerStatusLabel != null)
        {
            footerStatusLabel.Text = status;
        }
    }

    private void ResetProgress()
    {
        progressBar.Value = 0;
        UpdateProgressInfo();
    }

    private void UpdateActionButtons()
    {
        if (deleteSelectedButton == null)
        {
            return;
        }

        var hasTargets = targets.Count > 0;
        var hasSelected = targets.Any(target => target.IsSelected);
        scanButton.Enabled = true;
        preCleanButton.Enabled = !isBusy;
        deleteSelectedButton.Enabled = !isBusy && hasSelected;
        deleteAllButton.Enabled = !isBusy && hasTargets;
        banWaveButton.Enabled = !isBusy;
        autoCleanButton.Enabled = !isBusy;
        selectAllButton.Enabled = !isBusy;
        unselectAllButton.Enabled = !isBusy;
        restartButton.Enabled = !isBusy && restartAvailable;
    }

    private List<CleanupTarget> GetSelectedTargets()
    {
        return targets.Where(target => target.IsSelected).ToList();
    }

    private void SetAllSelections(bool selected)
    {
        if (targets.Count == 0)
        {
            SetStatusText("Run a scan first.");
            return;
        }

        foreach (var target in targets)
        {
            if (target.Status is CleanupTargetStatus.Found or CleanupTargetStatus.BackedUp)
            {
                target.IsSelected = selected;
            }
        }

        foreach (DataGridViewRow row in resultsGrid.Rows)
        {
            if (row.Cells["Status"].Value?.ToString() is "Found" or "BackedUp")
            {
                row.Cells["Selected"].Value = selected;
            }
        }

        UpdateStats();
        UpdateActionButtons();
        SetStatusText(selected ? "All available targets selected." : "All targets unselected.");
    }

    private void SyncSelectionFromGridRow(int rowIndex)
    {
        var row = resultsGrid.Rows[rowIndex];
        var targetPath = row.Cells["Target"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        var target = targets.FirstOrDefault(item => string.Equals(item.Target, targetPath, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return;
        }

        target.IsSelected = Convert.ToBoolean(row.Cells["Selected"].Value ?? false);
    }

    private void AdvanceProgress()
    {
        if (progressBar.Value < 95)
        {
            progressBar.Value = Math.Min(95, progressBar.Value + 1);
        }

        UpdateProgressInfo();
    }

    private void UpdateProgressInfo()
    {
        var percent = progressBar.Value;
        var elapsed = stopwatch.Elapsed;
        var eta = "--:--";

        if (percent > 1 && percent < 100)
        {
            var estimatedTotalSeconds = elapsed.TotalSeconds / percent * 100;
            var remaining = TimeSpan.FromSeconds(Math.Max(0, estimatedTotalSeconds - elapsed.TotalSeconds));
            eta = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }
        else if (percent >= 100)
        {
            eta = "00:00";
        }

        var progressText = $"Progress: {percent}% | ETA {eta}";
        progressInfoLabel.Text = progressText;
        if (footerProgressLabel != null)
        {
            footerProgressLabel.Text = progressText;
        }
    }

    private void HideToTray()
    {
        Hide();
        trayIcon.BalloonTipTitle = "WoW Blizzard Cleaner";
        trayIcon.BalloonTipText = "The app is still running in the system tray.";
        trayIcon.ShowBalloonTip(1500);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowFromTray());
        autoCleanTrayMenuItem = new ToolStripMenuItem("Auto-Clean: OFF")
        {
            CheckOnClick = false
        };
        autoCleanTrayMenuItem.Click += (_, _) => ToggleAutoClean();
        menu.Items.Add(autoCleanTrayMenuItem);
        menu.Items.Add("Exit", null, (_, _) =>
        {
            allowClose = true;
            Close();
        });
        return menu;
    }

    private TabPage CreateTab(string title)
    {
        return new TabPage(title)
        {
            BackColor = backgroundColor,
            ForeColor = textColor,
            Padding = new Padding(0)
        };
    }

    private Panel CreatePanel(string title)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = panelColor,
            Margin = new Padding(6),
            Padding = new Padding(12, 42, 12, 12)
        };

        var label = new Label
        {
            Text = title,
            AutoSize = false,
            Location = new Point(12, 10),
            Size = new Size(360, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(31, 32, 34)
        };
        panel.Controls.Add(label);
        label.BringToFront();
        return panel;
    }

    private Button CreateMainButton(string text, Color color)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(5)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private Button CreateSmallButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = mutedButtonColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 0)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(83, 99, 121);
        return button;
    }

    private Label CreateStatLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = textColor,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
        };
    }

    private Label CreateMutedLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = mutedTextColor,
            Font = new Font("Segoe UI", 9F)
        };
    }

    private void DrawLoaderTab(object? sender, DrawItemEventArgs e)
    {
        var selected = e.Index == loaderTabs.SelectedIndex;
        var bounds = e.Bounds;
        using var backBrush = new SolidBrush(selected ? headerColor : Color.FromArgb(28, 29, 31));
        using var textBrush = new SolidBrush(selected ? Color.White : mutedTextColor);
        e.Graphics.FillRectangle(backBrush, bounds);
        TextRenderer.DrawText(
            e.Graphics,
            loaderTabs.TabPages[e.Index].Text,
            new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            bounds,
            selected ? Color.White : mutedTextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
