using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class CleanerServiceTests
{
    private static CleanerService Make(FakeLockKiller killer) =>
        new(killer, new PathSafety([@"C:\Users\tester", @"C:\Windows"]), new PathExpander());

    [Fact]
    public async Task Scan_expands_folders_and_reports_counts()
    {
        using var temp = new TempDir();
        temp.File("a.txt");
        temp.File(Path.Combine("sub", "b.txt"));

        var killer = new FakeLockKiller();
        var scan = await Make(killer).ScanAsync([temp.Path], CancellationToken.None);

        Assert.Equal(2, scan.FileCount);
        Assert.Equal(2, scan.DirectoryCount);   // temp root + sub
        Assert.Equal(4, scan.AcceptedPaths.Count);
        Assert.True(scan.HasWork);
    }

    [Fact]
    public async Task Scan_passes_every_expanded_path_to_the_backend()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var killer = new FakeLockKiller();
        await Make(killer).ScanAsync([temp.Path], CancellationToken.None);

        Assert.Contains(file, killer.FindCalledWith);
        Assert.Contains(temp.Path, killer.FindCalledWith);
    }

    [Fact]
    public async Task Scan_returns_the_holders_the_backend_found()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");
        var killer = new FakeLockKiller
        {
            Holders = [new LockHolder(42, "notepad", file, LockHolderSource.HandleScan)]
        };

        var scan = await Make(killer).ScanAsync([file], CancellationToken.None);

        Assert.Single(scan.Holders);
        Assert.Equal(42, scan.Holders[0].Pid);
    }

    [Fact]
    public async Task Scan_refuses_an_unsafe_root_and_never_asks_the_backend_about_it()
    {
        var killer = new FakeLockKiller();
        var scan = await Make(killer).ScanAsync([@"C:\Windows"], CancellationToken.None);

        Assert.Empty(scan.AcceptedPaths);
        Assert.Empty(killer.FindCalledWith);
        Assert.Single(scan.Refused);
        Assert.Equal(PathOutcome.Refused, scan.Refused[0].Outcome);
        Assert.False(scan.HasWork);
        Assert.True(scan.AnyRefusals);
    }

    [Fact]
    public async Task Scan_keeps_the_safe_roots_when_one_is_refused()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var killer = new FakeLockKiller();
        var scan = await Make(killer).ScanAsync([@"C:\Windows", file], CancellationToken.None);

        Assert.Single(scan.Refused);
        Assert.Equal([file], scan.AcceptedPaths);
    }

    [Fact]
    public async Task Scan_reports_a_missing_path_as_an_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), "thecleaner-missing-" + Guid.NewGuid());

        var killer = new FakeLockKiller();
        var scan = await Make(killer).ScanAsync([missing], CancellationToken.None);

        Assert.Single(scan.Errors);
        Assert.False(scan.HasWork);
    }

    [Fact]
    public async Task Run_forwards_the_accepted_paths_and_options_to_the_backend()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var killer = new FakeLockKiller();
        var service = Make(killer);
        var scan = await service.ScanAsync([file], CancellationToken.None);

        var options = new UnlockOptions(DeleteAfterUnlock: true);
        await service.RunAsync(scan, options, CancellationToken.None);

        Assert.Equal([file], killer.UnlockCalledWith);
        Assert.Equal(options, killer.UnlockOptionsUsed);
    }

    [Fact]
    public async Task Run_merges_the_scan_refusals_into_the_result()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var killer = new FakeLockKiller();
        var service = Make(killer);
        var scan = await service.ScanAsync([@"C:\Windows", file], CancellationToken.None);

        var result = await service.RunAsync(
            scan, new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

        Assert.Equal(1, result.RefusedCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.True(result.AnyFailures);
    }

    [Fact]
    public async Task Run_with_nothing_accepted_skips_the_backend_entirely()
    {
        var killer = new FakeLockKiller();
        var service = Make(killer);
        var scan = await service.ScanAsync([@"C:\Windows"], CancellationToken.None);

        var result = await service.RunAsync(
            scan, new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

        Assert.Empty(killer.UnlockCalledWith);
        Assert.Equal(1, result.RefusedCount);
    }

    [Fact]
    public async Task Run_turns_a_backend_exception_into_a_failure_per_path()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var killer = new FakeLockKiller
        {
            UnlockResultFactory = _ => throw new InvalidOperationException("backend exploded")
        };
        var service = Make(killer);
        var scan = await service.ScanAsync([file], CancellationToken.None);

        var result = await service.RunAsync(
            scan, new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

        Assert.Equal(1, result.FailedCount);
        Assert.Contains("backend exploded", result.Paths[0].Message!);
    }
}
