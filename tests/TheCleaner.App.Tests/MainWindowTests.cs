using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TheCleaner;

namespace TheCleaner.AppTests;

/// <summary>
/// Guards the window's wiring — the layer that unit tests of Core and the Windows
/// backend cannot reach.
///
/// These tests drive real view-state transitions rather than looking controls up by
/// name. A name lookup reads the XAML namescope and passes even when the code-behind's
/// generated x:Name fields were never assigned, which is exactly the failure that
/// crashed the first published build; only exercising the code paths catches it.
/// </summary>
public class MainWindowTests
{
    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetControl<T>(name);

    /// <summary>
    /// Pumps the dispatcher until <paramref name="condition"/> holds. The scan runs on a
    /// background thread (a real system handle scan takes seconds), so draining the queue
    /// once proves nothing — we have to keep pumping until its continuation arrives.
    /// </summary>
    private static void WaitUntil(Func<bool> condition, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            Thread.Sleep(25);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), $"condition still false after {timeoutMs}ms");
    }

    private static string MakeTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thecleaner-apptests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "target.txt");
        File.WriteAllText(file, "x");
        return file;
    }

    [AvaloniaFact]
    public void Opens_on_the_idle_drop_zone_with_no_action_buttons()
    {
        var window = new MainWindow(CommandLineArgs.Parse([]));
        window.Show();

        Assert.True(window.IsVisible);
        Assert.True(Find<Border>(window, "IdlePanel").IsVisible);
        Assert.False(Find<Grid>(window, "ConfirmPanel").IsVisible);
        Assert.False(Find<Grid>(window, "ResultsPanel").IsVisible);
        Assert.False(Find<Button>(window, "UnlockDeleteButton").IsVisible);
    }

    [AvaloniaFact]
    public void A_refused_volume_root_reports_a_failure_instead_of_crashing()
    {
        // The exact startup path that crashed the published exe: argv targets drive a
        // scan the moment the window opens, which runs ShowBusy then ShowFailure.
        var window = new MainWindow(CommandLineArgs.Parse([@"C:\"]));
        window.Show();
        WaitUntil(() => Find<Grid>(window, "ResultsPanel").IsVisible);

        Assert.True(Find<Grid>(window, "ResultsPanel").IsVisible);
        Assert.False(Find<Border>(window, "IdlePanel").IsVisible);
        Assert.Contains("volume root", Find<TextBlock>(window, "SummaryText").Text ?? "",
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Find<Button>(window, "DoneButton").IsVisible);
    }

    [AvaloniaFact]
    public void A_real_target_reaches_the_confirm_view_with_counts_and_buttons()
    {
        // Drives ShowBusy then ShowConfirm, which touches TargetText, CountText,
        // WarningText, HolderList and the three action buttons.
        var file = MakeTempFile();
        try
        {
            var window = new MainWindow(CommandLineArgs.Parse([file]));
            window.Show();
            WaitUntil(() => Find<Grid>(window, "ConfirmPanel").IsVisible);

            Assert.True(Find<Grid>(window, "ConfirmPanel").IsVisible);
            Assert.Contains("target.txt", Find<TextBlock>(window, "TargetText").Text ?? "");
            Assert.Contains("1 file", Find<TextBlock>(window, "CountText").Text ?? "");
            Assert.False(string.IsNullOrWhiteSpace(Find<TextBlock>(window, "WarningText").Text));
            Assert.NotNull(Find<ListBox>(window, "HolderList").ItemsSource);

            Assert.True(Find<Button>(window, "CancelButton").IsVisible);
            Assert.True(Find<Button>(window, "UnlockButton").IsVisible);
            Assert.True(Find<Button>(window, "UnlockDeleteButton").IsVisible);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [AvaloniaFact]
    public void An_elevated_relaunch_carries_out_the_action_already_confirmed()
    {
        // The user confirmed before the UAC prompt; asking again after it would make the
        // --action argument dead weight. This is the only path that skips the confirm view.
        var file = MakeTempFile();
        var dir = Path.GetDirectoryName(file)!;
        try
        {
            var window = new MainWindow(CommandLineArgs.Parse(
                ["--elevated", "--action", "unlock-delete", "--", file]));
            window.Show();
            WaitUntil(() => Find<Grid>(window, "ResultsPanel").IsVisible);

            Assert.False(File.Exists(file));
            Assert.Contains("1 deleted", Find<TextBlock>(window, "SummaryText").Text ?? "");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_same_action_without_the_elevated_flag_still_asks_for_confirmation()
    {
        // A plain shell invocation must never skip the confirm, whatever flags it carries.
        var file = MakeTempFile();
        try
        {
            var window = new MainWindow(CommandLineArgs.Parse(
                ["--action", "unlock-delete", "--", file]));
            window.Show();
            WaitUntil(() => Find<Grid>(window, "ConfirmPanel").IsVisible);

            Assert.True(Find<Grid>(window, "ConfirmPanel").IsVisible);
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Cancel_returns_to_the_idle_drop_zone_and_changes_nothing()
    {
        var file = MakeTempFile();
        try
        {
            var window = new MainWindow(CommandLineArgs.Parse([file]));
            window.Show();
            WaitUntil(() => Find<Button>(window, "CancelButton").IsVisible);

            var cancel = Find<Button>(window, "CancelButton");
            Assert.True(cancel.IsVisible);

            // Drives ShowIdle, which touches SubtitleText and every panel.
            cancel.Command?.Execute(null);
            var click = new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent);
            cancel.RaiseEvent(click);
            WaitUntil(() => Find<Border>(window, "IdlePanel").IsVisible);

            Assert.True(Find<Border>(window, "IdlePanel").IsVisible);
            Assert.False(Find<Grid>(window, "ConfirmPanel").IsVisible);
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }
}
