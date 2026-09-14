namespace TheCleaner.Windows.Tests;

public class HandleReleaserTests
{
    [WindowsOnlyFact]
    public void Closes_a_handle_this_process_holds_and_reports_the_count()
    {
        using var temp = new TempDir();
        var file = temp.File("locked.txt");

        var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var closed = new HandleReleaser().CloseHandlesFor([file]);

            Assert.True(closed >= 1, $"expected at least one handle closed, got {closed}");
            // The lock is gone even though the FileStream still thinks it owns it.
            using var reopened = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            // The underlying handle is already closed; disposing it throws, which is fine.
            try { stream.Dispose(); } catch (Exception) { /* expected */ }
        }
    }

    [WindowsOnlyFact]
    public void Closes_nothing_when_no_handle_matches()
    {
        using var temp = new TempDir();
        var file = temp.File("free.txt");

        Assert.Equal(0, new HandleReleaser().CloseHandlesFor([file]));
    }

    [WindowsOnlyFact]
    public void Returns_zero_for_an_empty_path_list()
    {
        Assert.Equal(0, new HandleReleaser().CloseHandlesFor([]));
    }
}
