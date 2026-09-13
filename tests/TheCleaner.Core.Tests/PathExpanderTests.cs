using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class PathExpanderTests
{
    [Fact]
    public void Expands_a_single_file_to_itself()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var result = new PathExpander().Expand([file]);

        Assert.Equal([file], result.Files);
        Assert.Empty(result.Directories);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public void Expands_a_folder_recursively()
    {
        using var temp = new TempDir();
        var top = temp.File("top.txt");
        var nested = temp.File(Path.Combine("sub", "deep", "nested.txt"));

        var result = new PathExpander().Expand([temp.Path]);

        Assert.Contains(top, result.Files);
        Assert.Contains(nested, result.Files);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public void Orders_directories_deepest_first_with_the_root_last()
    {
        using var temp = new TempDir();
        temp.File(Path.Combine("sub", "deep", "nested.txt"));

        var result = new PathExpander().Expand([temp.Path]);

        var deep = Path.Combine(temp.Path, "sub", "deep");
        var sub = Path.Combine(temp.Path, "sub");

        Assert.Equal([deep, sub, temp.Path], result.Directories);
    }

    [Fact]
    public void Includes_empty_directories()
    {
        using var temp = new TempDir();
        var empty = temp.Dir("empty");

        var result = new PathExpander().Expand([temp.Path]);

        Assert.Contains(empty, result.Directories);
        Assert.Equal(0, result.FileCount);
    }

    [Fact]
    public void Reports_a_missing_path_as_an_error_without_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "thecleaner-does-not-exist-" + Guid.NewGuid());

        var result = new PathExpander().Expand([missing]);

        Assert.Empty(result.Files);
        Assert.Single(result.Errors);
        Assert.Contains(missing, result.Errors[0]);
    }

    [Fact]
    public void A_missing_path_does_not_stop_a_good_one()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");
        var missing = Path.Combine(temp.Path, "nope.txt");

        var result = new PathExpander().Expand([missing, file]);

        Assert.Equal([file], result.Files);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Deduplicates_overlapping_roots()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var result = new PathExpander().Expand([temp.Path, file]);

        Assert.Equal(1, result.FileCount);
        Assert.Single(result.Directories);
    }

    [Fact]
    public void AllPaths_is_files_then_directories()
    {
        using var temp = new TempDir();
        var file = temp.File("a.txt");

        var result = new PathExpander().Expand([temp.Path]);

        Assert.Equal([file, temp.Path], result.AllPaths);
    }
}
