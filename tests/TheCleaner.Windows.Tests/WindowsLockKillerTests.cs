using TheCleaner.Core;

namespace TheCleaner.Windows.Tests;

public class WindowsLockKillerTests
{
    [WindowsOnlyFact]
    public async Task Finds_a_locker_for_a_held_file()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");
        using var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var holders = await new WindowsLockKiller().FindLockersAsync([file], CancellationToken.None);

        Assert.Contains(holders, h => h.Pid == Environment.ProcessId);
    }

    [WindowsOnlyFact]
    public async Task Unlock_without_delete_frees_the_file_and_leaves_it_on_disk()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");
        var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try
        {
            var result = await new WindowsLockKiller().UnlockAsync(
                [file], new UnlockOptions(DeleteAfterUnlock: false), CancellationToken.None);

            Assert.True(File.Exists(file));
            Assert.Equal(PathOutcome.Unlocked, result.Paths.Single().Outcome);
            using var reopened = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            try { stream.Dispose(); } catch (Exception) { }
        }
    }

    [WindowsOnlyFact]
    public async Task Unlock_and_delete_removes_a_file_this_process_holds_open()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");
        var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try
        {
            var result = await new WindowsLockKiller().UnlockAsync(
                [file], new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

            Assert.False(File.Exists(file));
            Assert.Equal(1, result.DeletedCount);
            Assert.True(result.HandlesClosed >= 1);
        }
        finally
        {
            try { stream.Dispose(); } catch (Exception) { }
        }
    }

    [WindowsOnlyFact]
    public async Task Unlock_and_delete_removes_an_unlocked_folder_tree()
    {
        using var temp = new TempDir();
        var nested = temp.File(Path.Combine("sub", "x.txt"));
        var sub = Path.Combine(temp.Path, "sub");

        var result = await new WindowsLockKiller().UnlockAsync(
            [nested, sub], new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

        Assert.False(Directory.Exists(sub));
        Assert.Equal(2, result.DeletedCount);
    }

    [WindowsOnlyFact]
    public async Task Never_terminates_our_own_process_even_when_we_are_the_locker()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");
        var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try
        {
            var result = await new WindowsLockKiller().UnlockAsync(
                [file], new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

            Assert.DoesNotContain(result.Terminated, t => t.Pid == Environment.ProcessId);
            Assert.True(Environment.ProcessId > 0); // we are, in fact, still alive
        }
        finally
        {
            try { stream.Dispose(); } catch (Exception) { }
        }
    }

    [WindowsOnlyFact]
    public async Task An_unlocked_file_kept_rather_than_deleted_reports_no_lock_found()
    {
        using var temp = new TempDir();
        var file = temp.File("free.txt");

        var result = await new WindowsLockKiller().UnlockAsync(
            [file], new UnlockOptions(DeleteAfterUnlock: false), CancellationToken.None);

        Assert.Equal(PathOutcome.NoLockFound, result.Paths.Single().Outcome);
        Assert.True(File.Exists(file));
    }
}
