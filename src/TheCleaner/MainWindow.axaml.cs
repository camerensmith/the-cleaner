using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TheCleaner.Core;

namespace TheCleaner;

/// <summary>Row shown in the holder and result lists.</summary>
public sealed record ListRow(string Title, string Detail);

/// <summary>Category header row shown in the trace list.</summary>
public sealed class TraceHeaderRow
{
    public string Label { get; set; } = string.Empty;
}

/// <summary>Individual item shown in the trace list; the user can uncheck it to exclude
/// it from the deep uninstall.</summary>
public sealed class TraceItemRow
{
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
    /// <summary>The underlying value (file path, registry key, service name, etc.).</summary>
    public string Value { get; set; } = string.Empty;
}

public partial class MainWindow : Window
{
    private readonly CleanerService _service;
    private readonly RunLogger _logger = new(RunLogger.DefaultLogPath);
    private readonly CommandLineArgs _startup;
    private readonly IInstallTracer? _installTracer;
    private readonly IDeepUninstallBackend? _deepUninstallBackend;

    private ScanResult _scan = ScanResult.Empty;
    private InstallFootprint _footprint = InstallFootprint.Empty;
    private IReadOnlyList<string> _tracedPaths = [];

    public MainWindow() : this(CommandLineArgs.Parse([])) { }

    public MainWindow(CommandLineArgs startup)
    {
        InitializeComponent();
        _startup = startup;
        _service = new CleanerService(PlatformBackend.Create());
        _installTracer = PlatformBackend.CreateInstallTracer();
        _deepUninstallBackend = PlatformBackend.CreateDeepUninstallBackend();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        Opened += OnOpened;
    }

    // ---- startup ----------------------------------------------------------

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (!_startup.HasPaths) return;

        await ScanAndShowAsync(_startup.Paths);

        if (_startup.Elevated
            && _startup.DeleteAfterUnlock is { } deleteAfter
            && ConfirmPanel.IsVisible)
        {
            await RunAsync(deleteAfter);
        }
    }

    // ---- drag and drop ----------------------------------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ConfirmPanel.IsVisible || TracePanel.IsVisible || BusyPanel.IsVisible)
            return;

        var paths = ExtractPaths(e);
        if (paths.Count == 0) return;

        // For a single dropped item, try the install tracer first when available.
        if (_installTracer is not null && paths.Count == 1)
        {
            await TraceAndShowAsync(paths[0], paths);
        }
        else
        {
            await ScanAndShowAsync(paths);
        }
    }

    private static List<string> ExtractPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        var files = e.Data.GetFiles();
        if (files is null) return paths;

        foreach (var item in files)
        {
            var local = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local)) paths.Add(local);
        }
        return paths;
    }

    // ---- trace (install footprint) ----------------------------------------

    private async Task TraceAndShowAsync(string droppedPath, IReadOnlyList<string> paths)
    {
        ShowBusy("Tracing install footprint…");

        InstallFootprint footprint;
        try
        {
            footprint = await _installTracer!.TraceAsync(droppedPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.WriteError(ex.Message);
            // Fall back to lock scan on trace error.
            await ScanAndShowAsync(paths);
            return;
        }

        if (!footprint.HasWork)
        {
            // No install footprint found — fall through to the lock scan.
            await ScanAndShowAsync(paths);
            return;
        }

        _footprint = footprint;
        _tracedPaths = paths;
        ShowTrace(footprint);
    }

    // ---- scan (lock finder) -----------------------------------------------

    private async Task ScanAndShowAsync(IReadOnlyList<string> paths)
    {
        ShowBusy("Scanning for locking processes…");

        ScanResult scan;
        try
        {
            scan = await _service.ScanAsync(paths, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.WriteError(ex.Message);
            ShowFailure($"Scan failed: {ex.Message}");
            return;
        }

        _scan = scan;

        if (!scan.HasWork)
        {
            var reasons = scan.Refused.Select(r => r.Message ?? "Refused.")
                .Concat(scan.Errors)
                .ToList();
            _logger.WriteError(string.Join(" ", reasons));
            ShowFailure(reasons.Count > 0
                ? string.Join(Environment.NewLine, reasons)
                : "Nothing to do.");
            return;
        }

        ShowConfirm(scan);
    }

    // ---- run (lock killer) ------------------------------------------------

    private void OnUnlock(object? sender, RoutedEventArgs e) => _ = RunAsync(deleteAfter: false);

    private void OnUnlockAndDelete(object? sender, RoutedEventArgs e) => _ = RunAsync(deleteAfter: true);

    private void OnCancel(object? sender, RoutedEventArgs e) => ShowIdle();

    private void OnDone(object? sender, RoutedEventArgs e) => ShowIdle();

    private void OnSwitchToLockScan(object? sender, RoutedEventArgs e)
    {
        _ = ScanAndShowAsync(_tracedPaths.Count > 0 ? _tracedPaths : [_footprint.InstallRoot]);
    }

    private async Task RunAsync(bool deleteAfter)
    {
        ShowBusy(deleteAfter ? "Unlocking and deleting…" : "Unlocking…");

        var options = new UnlockOptions(DeleteAfterUnlock: deleteAfter);
        KillResult result;
        try
        {
            result = await _service.RunAsync(_scan, options, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.WriteError(ex.Message);
            ShowFailure($"Unlock failed: {ex.Message}");
            return;
        }

        _logger.Write(_scan, result, options);

        if (result.ElevationRequired && PlatformBackend.SupportsElevation && !PlatformBackend.IsElevated)
        {
            if (PlatformBackend.Relaunch(_scan.Roots, deleteAfter, out var error))
            {
                Close();
                return;
            }

            _logger.WriteError(error ?? "Elevation required / cancelled.");
            ShowResults(result, extraNote: error);
            return;
        }

        ShowResults(result, extraNote: null);
    }

    // ---- deep uninstall ---------------------------------------------------

    private void OnDeepUninstall(object? sender, RoutedEventArgs e) => _ = RunDeepUninstallAsync();

    private async Task RunDeepUninstallAsync()
    {
        ShowBusy("Deep uninstalling…");

        var (footprint, options) = BuildFilteredFootprint();

        DeepUninstallResult result;
        try
        {
            var deepService = new DeepUninstallService(
                PlatformBackend.Create(),
                _deepUninstallBackend!);
            result = await deepService.UninstallAsync(footprint, options, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.WriteError(ex.Message);
            ShowFailure($"Deep uninstall failed: {ex.Message}");
            return;
        }

        ShowDeepUninstallResults(result);
    }

    /// <summary>Reads the checked state of every <see cref="TraceItemRow"/> in
    /// <see cref="TraceList"/> and produces a footprint and options that reflect only the
    /// items the user kept selected.</summary>
    private (InstallFootprint Footprint, InstallCleanOptions Options) BuildFilteredFootprint()
    {
        var items = TraceList.ItemsSource?.OfType<TraceItemRow>().ToList()
                   ?? new List<TraceItemRow>();

        var selected = items.Where(i => i.IsSelected).ToLookup(i => i.Category);

        var dataPaths = selected["data"].Select(i => i.Value).ToList();
        var regKeys = selected["registry"].Select(i => i.Value).ToList();
        var services = selected["service"].Select(i => i.Value).ToList();
        var tasks = selected["task"].Select(i => i.Value).ToList();
        var startMenuShortcuts = selected["shortcut-start"].Select(i => i.Value).ToList();
        var desktopShortcuts = selected["shortcut-desktop"].Select(i => i.Value).ToList();

        var installRoot = selected["install"].Any()
            ? _footprint.InstallRoot
            : string.Empty;

        var filtered = new InstallFootprint(
            InstallRoot: installRoot,
            AppName: _footprint.AppName,
            ExePaths: _footprint.ExePaths,
            DataPaths: dataPaths,
            RegistryKeys: regKeys,
            Services: services,
            ScheduledTasks: tasks,
            StartMenuShortcuts: startMenuShortcuts,
            DesktopShortcuts: desktopShortcuts);

        var options = new InstallCleanOptions(
            IncludeUserData: dataPaths.Count > 0,
            IncludeRegistry: regKeys.Count > 0,
            IncludeServices: services.Count > 0,
            IncludeTasks: tasks.Count > 0,
            IncludeShortcuts: startMenuShortcuts.Count > 0 || desktopShortcuts.Count > 0);

        return (filtered, options);
    }

    // ---- view states ------------------------------------------------------

    private void ShowIdle()
    {
        SubtitleText.Text = "Drop a file or folder to see what is holding it.";
        SetPanels(idle: true, busy: false, confirm: false, trace: false, results: false);
        SetButtons(cancel: false, switchScan: false, unlock: false, unlockDelete: false,
                   deepUninstall: false, done: false);
    }

    private void ShowBusy(string message)
    {
        BusyText.Text = message;
        SetPanels(idle: false, busy: true, confirm: false, trace: false, results: false);
        SetButtons(cancel: false, switchScan: false, unlock: false, unlockDelete: false,
                   deepUninstall: false, done: false);
    }

    private void ShowConfirm(ScanResult scan)
    {
        SubtitleText.Text = "Review what will happen, then choose an action.";
        TargetText.Text = DescribeTargets(scan.Roots);
        CountText.Text = $"{scan.FileCount} file(s), {scan.DirectoryCount} folder(s) will be affected.";

        WarningText.Text = scan.Holders.Count == 0
            ? "Nothing is holding these targets right now. Unlock & Delete will remove them."
            : "Open handles will be closed and every locking process listed below will be terminated. "
              + "Unsaved work in those processes will be lost.";

        var rows = new List<ListRow>();
        foreach (var h in scan.Holders)
        {
            rows.Add(new ListRow(
                $"{h.ProcessName}  (PID {h.Pid})",
                string.IsNullOrEmpty(h.Path) ? h.Source.ToString() : h.Path));
        }
        if (rows.Count == 0) rows.Add(new ListRow("No locking processes found.", string.Empty));

        foreach (var refused in scan.Refused)
            rows.Add(new ListRow($"Refused: {refused.Path}", refused.Message ?? string.Empty));
        foreach (var error in scan.Errors)
            rows.Add(new ListRow("Scan warning", error));

        HolderList.ItemsSource = rows;

        SetPanels(idle: false, busy: false, confirm: true, trace: false, results: false);
        SetButtons(cancel: true, switchScan: false, unlock: true, unlockDelete: true,
                   deepUninstall: false, done: false);
        UnlockDeleteButton.Focus();
    }

    private void ShowTrace(InstallFootprint footprint)
    {
        SubtitleText.Text = "Review all artifacts found, then choose an action.";
        TraceTargetText.Text = $"{footprint.AppName}  —  {Middle(footprint.InstallRoot, 72)}";

        var totalItems = footprint.DataPaths.Count + footprint.RegistryKeys.Count
            + footprint.Services.Count + footprint.ScheduledTasks.Count
            + footprint.StartMenuShortcuts.Count + footprint.DesktopShortcuts.Count;
        TraceSummaryText.Text =
            $"Install root + {totalItems} additional artifact(s) found across "
            + $"{footprint.RegistryKeys.Count} registry key(s), "
            + $"{footprint.Services.Count} service(s), "
            + $"{footprint.ScheduledTasks.Count} task(s).";

        var rows = new List<object>();

        // Install root — always shown.
        rows.Add(new TraceHeaderRow { Label = "📁 Install files" });
        rows.Add(new TraceItemRow
        {
            Category = "install",
            Label = Path.GetFileName(footprint.InstallRoot.TrimEnd('\\', '/')),
            Detail = footprint.InstallRoot,
            Value = footprint.InstallRoot,
        });

        AddCategory(rows, "🗂 User data", "data", footprint.DataPaths,
            v => (Path.GetFileName(v.TrimEnd('\\', '/')), v));
        AddCategory(rows, "🔑 Registry keys", "registry", footprint.RegistryKeys,
            v => (TruncateMiddle(v, 64), string.Empty));
        AddCategory(rows, "⚙️ Services", "service", footprint.Services,
            v => (v, string.Empty));
        AddCategory(rows, "📅 Scheduled tasks", "task", footprint.ScheduledTasks,
            v => (v, string.Empty));
        AddCategory(rows, "🔗 Start menu shortcuts", "shortcut-start", footprint.StartMenuShortcuts,
            v => (Path.GetFileNameWithoutExtension(v), v));
        AddCategory(rows, "🖥 Desktop shortcuts", "shortcut-desktop", footprint.DesktopShortcuts,
            v => (Path.GetFileNameWithoutExtension(v), v));

        TraceList.ItemsSource = rows;

        SetPanels(idle: false, busy: false, confirm: false, trace: true, results: false);
        SetButtons(cancel: true, switchScan: _installTracer is not null, unlock: false,
                   unlockDelete: false, deepUninstall: true, done: false);
        DeepUninstallButton.Focus();
    }

    private static void AddCategory<T>(
        List<object> rows,
        string header,
        string category,
        IReadOnlyList<T> items,
        Func<string, (string Label, string Detail)> describe) where T : notnull
    {
        if (items.Count == 0) return;
        rows.Add(new TraceHeaderRow { Label = $"{header} ({items.Count})" });
        foreach (var item in items)
        {
            var (label, detail) = describe(item.ToString()!);
            rows.Add(new TraceItemRow
            {
                Category = category,
                Label = label,
                Detail = detail,
                Value = item.ToString()!,
            });
        }
    }

    private void ShowResults(KillResult result, string? extraNote)
    {
        SubtitleText.Text = "Run complete.";

        var summary = $"{result.DeletedCount} deleted, {result.UnlockedCount} unlocked, "
                      + $"{result.FailedCount} failed, {result.RefusedCount} refused. "
                      + $"{result.HandlesClosed} handle(s) closed, "
                      + $"{result.Terminated.Count} process(es) terminated.";
        SummaryText.Text = extraNote is null ? summary : summary + Environment.NewLine + extraNote;

        var rows = new List<ListRow>();
        foreach (var t in result.Terminated)
            rows.Add(new ListRow($"Terminated {t.ProcessName} (PID {t.Pid})", string.Empty));
        foreach (var s in result.Skipped)
            rows.Add(new ListRow($"Skipped {s.ProcessName} (PID {s.Pid})", s.Reason));
        foreach (var p in result.Paths)
            rows.Add(new ListRow($"{p.Outcome}: {p.Path}", p.Message ?? string.Empty));

        ResultList.ItemsSource = rows;
        LogPathText.Text = $"Log: {RunLogger.DefaultLogPath}";

        SetPanels(idle: false, busy: false, confirm: false, trace: false, results: true);
        SetButtons(cancel: false, switchScan: false, unlock: false, unlockDelete: false,
                   deepUninstall: false, done: true);
        DoneButton.Focus();
    }

    private void ShowDeepUninstallResults(DeepUninstallResult result)
    {
        SubtitleText.Text = "Deep uninstall complete.";
        SummaryText.Text =
            $"Files/dirs: {result.FilesDeleted} deleted, {result.FilesFailed} failed. "
            + $"Registry: {result.RegistryKeysDeleted} deleted, {result.RegistryKeysFailed} failed. "
            + $"Services: {result.ServicesDeleted} deleted, {result.ServicesFailed} failed. "
            + $"Tasks: {result.TasksDeleted} deleted, {result.TasksFailed} failed.";

        var rows = result.Errors.Select(e => new ListRow("Error", e)).ToList();
        if (rows.Count == 0) rows.Add(new ListRow("No errors.", string.Empty));

        ResultList.ItemsSource = rows;
        LogPathText.Text = $"Log: {RunLogger.DefaultLogPath}";

        SetPanels(idle: false, busy: false, confirm: false, trace: false, results: true);
        SetButtons(cancel: false, switchScan: false, unlock: false, unlockDelete: false,
                   deepUninstall: false, done: true);
        DoneButton.Focus();
    }

    private void ShowFailure(string message)
    {
        SubtitleText.Text = "Nothing was changed.";
        SummaryText.Text = message;
        ResultList.ItemsSource = Array.Empty<ListRow>();
        LogPathText.Text = $"Log: {RunLogger.DefaultLogPath}";

        SetPanels(idle: false, busy: false, confirm: false, trace: false, results: true);
        SetButtons(cancel: false, switchScan: false, unlock: false, unlockDelete: false,
                   deepUninstall: false, done: true);
        DoneButton.Focus();
    }

    private void SetPanels(bool idle, bool busy, bool confirm, bool trace, bool results)
    {
        IdlePanel.IsVisible = idle;
        BusyPanel.IsVisible = busy;
        ConfirmPanel.IsVisible = confirm;
        TracePanel.IsVisible = trace;
        ResultsPanel.IsVisible = results;
    }

    private void SetButtons(
        bool cancel, bool switchScan, bool unlock, bool unlockDelete,
        bool deepUninstall, bool done)
    {
        CancelButton.IsVisible = cancel;
        SwitchToLockScanButton.IsVisible = switchScan;
        UnlockButton.IsVisible = unlock;
        UnlockDeleteButton.IsVisible = unlockDelete;
        DeepUninstallButton.IsVisible = deepUninstall;
        DoneButton.IsVisible = done;
    }

    /// <summary>One line for one target, a count for several; long paths trim in the middle.</summary>
    private static string DescribeTargets(IReadOnlyList<string> roots) => roots.Count switch
    {
        0 => "No targets.",
        1 => Middle(roots[0], 80),
        _ => $"{roots.Count} targets — {Middle(roots[0], 60)} and {roots.Count - 1} more"
    };

    private static string Middle(string text, int max)
    {
        if (text.Length <= max) return text;
        var keep = (max - 3) / 2;
        return text[..keep] + "..." + text[^keep..];
    }

    private static string TruncateMiddle(string text, int max) => Middle(text, max);
}
