using TheCleaner.Core;

namespace TheCleaner.Windows.Tests;

public class DeleterTests
{
    [WindowsOnlyFact]
    public void Deletes_a_file()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var report = new Deleter().Delete([file]);

        Assert.False(File.Exists(file));
        Assert.Equal(PathOutcome.Deleted, report.Results.Single().Outcome);
    }

    [WindowsOnlyFact]
    public void Deletes_a_read_only_file()
    {
        using var temp = new TempDir();
        var file = temp.File("ro.txt");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var report = new Deleter().Delete([file]);

        Assert.False(File.Exists(file));
        Assert.Equal(PathOutcome.Deleted, report.Results.Single().Outcome);
    }

    [WindowsOnlyFact]
    public void Deletes_a_folder_tree_bottom_up_regardless_of_input_order()
    {
        using var temp = new TempDir();
        var nested = temp.File(Path.Combine("sub", "deep", "x.txt"));
        var deep = Path.Combine(temp.Path, "sub", "deep");
        var sub = Path.Combine(temp.Path, "sub");

        // Deliberately shallowest-first: the deleter must re-order.
        var report = new Deleter().Delete([sub, deep, nested]);

        Assert.False(Directory.Exists(sub));
        Assert.All(report.Results, r => Assert.Equal(PathOutcome.Deleted, r.Outcome));
    }

    [WindowsOnlyFact]
    public void Deletes_a_read_only_directory()
    {
        using var temp = new TempDir();
        var dir = temp.Dir("ro-dir");
        File.SetAttributes(dir, File.GetAttributes(dir) | FileAttributes.ReadOnly);

        var report = new Deleter().Delete([dir]);

        Assert.False(Directory.Exists(dir));
    }

    [WindowsOnlyFact]
    public void Reports_a_still_locked_file_as_failed_without_stopping_the_rest()
    {
        using var temp = new TempDir();
        var locked = temp.File("locked.txt");
        var free = temp.File("free.txt");

        using var stream = File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var report = new Deleter().Delete([locked, free]);

        Assert.False(File.Exists(free));
        Assert.True(File.Exists(locked));
        Assert.Contains(report.Results, r => r.Path == locked && r.Outcome == PathOutcome.Failed);
        Assert.Contains(report.Results, r => r.Path == free && r.Outcome == PathOutcome.Deleted);
    }

    [WindowsOnlyFact]
    public void Treats_an_already_gone_path_as_deleted()
    {
        var missing = Path.Combine(Path.GetTempPath(), "thecleaner-gone-" + Guid.NewGuid());

        var report = new Deleter().Delete([missing]);

        Assert.Equal(PathOutcome.Deleted, report.Results.Single().Outcome);
    }

    [WindowsOnlyFact]
    public void Returns_an_empty_report_for_an_empty_list()
    {
        var report = new Deleter().Delete([]);
        Assert.Empty(report.Results);
        Assert.False(report.AccessDenied);
    }
}
