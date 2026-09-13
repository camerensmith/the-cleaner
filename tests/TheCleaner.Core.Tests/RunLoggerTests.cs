using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class RunLoggerTests
{
    private static ScanResult MakeScan() =>
        new(
            Roots: [@"C:\work\locked.txt"],
            AcceptedPaths: [@"C:\work\locked.txt"],
            Refused: [],
            Holders: [new LockHolder(4321, "notepad", @"C:\work\locked.txt", LockHolderSource.HandleScan)],
            FileCount: 1,
            DirectoryCount: 0,
            Errors: []);

    private static KillResult MakeResult() =>
        new(
            Paths: [new PathResult(@"C:\work\locked.txt", PathOutcome.Deleted)],
            Terminated: [new TerminatedProcess(4321, "notepad")],
            Skipped: [new SkippedProcess(4, "System", "protected process")],
            HandlesClosed: 1,
            ElevationRequired: false);

    [Fact]
    public void Writes_a_log_containing_targets_holders_and_outcomes()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "last-run.log");

        new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(DeleteAfterUnlock: true));

        var text = File.ReadAllText(path);
        Assert.Contains(@"C:\work\locked.txt", text);
        Assert.Contains("notepad", text);
        Assert.Contains("4321", text);
        Assert.Contains("Deleted", text);
        Assert.Contains("protected process", text);
        Assert.Contains("Handles closed: 1", text);
    }

    [Fact]
    public void Creates_the_log_directory_when_it_is_missing()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "nested", "deeper", "last-run.log");

        new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(DeleteAfterUnlock: true));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Overwrites_the_previous_run()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "last-run.log");
        File.WriteAllText(path, "PREVIOUS RUN CONTENT");

        new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(DeleteAfterUnlock: true));

        Assert.DoesNotContain("PREVIOUS RUN CONTENT", File.ReadAllText(path));
    }

    [Fact]
    public void WriteError_records_the_message()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "last-run.log");

        new RunLogger(path).WriteError("elevation required / cancelled");

        Assert.Contains("elevation required / cancelled", File.ReadAllText(path));
    }

    [Fact]
    public void An_unwritable_log_path_does_not_throw()
    {
        // A directory where a file should be: opening it for write always fails.
        using var temp = new TempDir();
        var path = temp.Dir("last-run.log");

        var ex = Record.Exception(() =>
            new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(true)));

        Assert.Null(ex);
    }

    [Fact]
    public void DefaultLogPath_is_under_local_appdata()
    {
        Assert.EndsWith(
            Path.Combine("thecleaner", "last-run.log"), RunLogger.DefaultLogPath);
    }
}
