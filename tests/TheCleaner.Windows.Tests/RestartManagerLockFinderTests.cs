using TheCleaner.Core;

namespace TheCleaner.Windows.Tests;

public class RestartManagerLockFinderTests
{
    [WindowsOnlyFact]
    public void Finds_this_process_when_it_holds_the_file_open()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");

        using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var holders = new RestartManagerLockFinder().Find([file], file);

        Assert.Contains(holders, h => h.Pid == Environment.ProcessId);
    }

    [WindowsOnlyFact]
    public void Reports_the_display_path_and_the_restart_manager_source()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");
        using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var holder = new RestartManagerLockFinder().Find([file], file)
            .Single(h => h.Pid == Environment.ProcessId);

        Assert.Equal(file, holder.Path);
        Assert.Equal(LockHolderSource.RestartManager, holder.Source);
        Assert.False(string.IsNullOrWhiteSpace(holder.ProcessName));
    }

    [WindowsOnlyFact]
    public void Finds_nothing_for_an_unlocked_file()
    {
        using var temp = new TempDir();
        var file = temp.File("free.txt");

        var holders = new RestartManagerLockFinder().Find([file], file);

        Assert.DoesNotContain(holders, h => h.Pid == Environment.ProcessId);
    }

    [WindowsOnlyFact]
    public void Returns_empty_for_an_empty_path_list()
    {
        Assert.Empty(new RestartManagerLockFinder().Find([], "root"));
    }

    [WindowsOnlyFact]
    public void Does_not_throw_on_a_path_that_does_not_exist()
    {
        var missing = Path.Combine(Path.GetTempPath(), "thecleaner-missing-" + Guid.NewGuid());
        var ex = Record.Exception(() => new RestartManagerLockFinder().Find([missing], missing));
        Assert.Null(ex);
    }
}
