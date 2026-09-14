using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using TheCleaner.Core;

namespace TheCleaner;

/// <summary>Row shown in the holder and result lists.</summary>
public sealed record ListRow(string Title, string Detail);

public partial class MainWindow : Window
{
    private readonly CleanerService _service;
    private readonly RunLogger _logger = new(RunLogger.DefaultLogPath);
    private readonly CommandLineArgs _startup;

    private ScanResult _scan = ScanResult.Empty;

    public MainWindow() : this(CommandLineArgs.Parse([])) { }

    public MainWindow(CommandLineArgs startup)
    {
        AvaloniaXamlLoader.Load(this);
        _startup = startup;
        _service = new CleanerService(PlatformBackend.Create());

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        Opened += OnOpened;
    }

    // ---- startup ----------------------------------------------------------

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (_startup.HasPaths) await ScanAndShowAsync(_startup.Paths);
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
        if (!ConfirmPanel.IsVisible && !BusyPanel.IsVisible)
        {
            var paths = ExtractPaths(e);
            if (paths.Count > 0) await ScanAndShowAsync(paths);
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

    // ---- scan -------------------------------------------------------------

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

    // ---- run --------------------------------------------------------------

    private void OnUnlock(object? sender, RoutedEventArgs e) => _ = RunAsync(deleteAfter: false);

    private void OnUnlockAndDelete(object? sender, RoutedEventArgs e) => _ = RunAsync(deleteAfter: true);

    private void OnCancel(object? sender, RoutedEventArgs e) => ShowIdle();

    private void OnDone(object? sender, RoutedEventArgs e) => ShowIdle();

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
                // The elevated instance takes over from here.
                Close();
                return;
            }

            _logger.WriteError(error ?? "Elevation required / cancelled.");
            ShowResults(result, extraNote: error);
            return;
        }

        ShowResults(result, extraNote: null);
    }

    // ---- view states ------------------------------------------------------

    private void ShowIdle()
    {
        SubtitleText.Text = "Drop a file or folder to see what is holding it.";
        SetPanels(idle: true, busy: false, confirm: false, results: false);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: false);
    }

    private void ShowBusy(string message)
    {
        BusyText.Text = message;
        SetPanels(idle: false, busy: true, confirm: false, results: false);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: false);
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

        SetPanels(idle: false, busy: false, confirm: true, results: false);
        SetButtons(cancel: true, unlock: true, unlockDelete: true, done: false);
        UnlockDeleteButton.Focus();
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

        SetPanels(idle: false, busy: false, confirm: false, results: true);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: true);
        DoneButton.Focus();
    }

    private void ShowFailure(string message)
    {
        SubtitleText.Text = "Nothing was changed.";
        SummaryText.Text = message;
        ResultList.ItemsSource = Array.Empty<ListRow>();
        LogPathText.Text = $"Log: {RunLogger.DefaultLogPath}";

        SetPanels(idle: false, busy: false, confirm: false, results: true);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: true);
        DoneButton.Focus();
    }

    private void SetPanels(bool idle, bool busy, bool confirm, bool results)
    {
        IdlePanel.IsVisible = idle;
        BusyPanel.IsVisible = busy;
        ConfirmPanel.IsVisible = confirm;
        ResultsPanel.IsVisible = results;
    }

    private void SetButtons(bool cancel, bool unlock, bool unlockDelete, bool done)
    {
        CancelButton.IsVisible = cancel;
        UnlockButton.IsVisible = unlock;
        UnlockDeleteButton.IsVisible = unlockDelete;
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
}
