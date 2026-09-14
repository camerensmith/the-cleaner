using TheCleaner.Core;

namespace TheCleaner.Windows.Tests;

public class NtHandleLockFinderTests
{
    [WindowsOnlyFact]
    public void Finds_this_process_holding_a_file_and_names_the_exact_path()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");

        using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var holders = new NtHandleLockFinder().Find([file]);

        var mine = holders.Where(h => h.Pid == Environment.ProcessId).ToList();
        Assert.NotEmpty(mine);
        Assert.All(mine, h => Assert.Equal(file, h.Path, ignoreCase: true));
        Assert.All(mine, h => Assert.Equal(LockHolderSource.HandleScan, h.Source));
    }

    [WindowsOnlyFact]
    public void Does_not_report_a_file_nobody_has_open()
    {
        using var temp = new TempDir();
        var file = temp.File("free.txt");

        var holders = new NtHandleLockFinder().Find([file]);

        Assert.DoesNotContain(holders, h => h.Pid == Environment.ProcessId);
    }

    [WindowsOnlyFact]
    public void Reports_one_entry_per_holding_process_not_per_handle()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");

        using var a = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var b = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var mine = new NtHandleLockFinder().Find([file])
            .Where(h => h.Pid == Environment.ProcessId)
            .ToList();

        Assert.Single(mine);
    }

    [WindowsOnlyFact]
    public void Returns_empty_for_an_empty_path_list()
    {
        Assert.Empty(new NtHandleLockFinder().Find([]));
    }

    [WindowsOnlyFact]
    public void Completes_within_thirty_seconds_on_a_full_system_scan()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");
        using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        new NtHandleLockFinder().Find([file]);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"scan took {sw.Elapsed}");
    }
}
