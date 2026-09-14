using TheCleaner;

namespace TheCleaner.App.Tests;

public class CommandLineTests
{
    [Fact]
    public void No_arguments_yields_no_paths()
    {
        var parsed = CommandLineArgs.Parse([]);

        Assert.Empty(parsed.Paths);
        Assert.False(parsed.HasPaths);
        Assert.False(parsed.Elevated);
        Assert.Null(parsed.DeleteAfterUnlock);
    }

    [Fact]
    public void Bare_paths_are_targets()
    {
        var parsed = CommandLineArgs.Parse([@"C:\a.txt", @"C:\b"]);

        Assert.Equal([@"C:\a.txt", @"C:\b"], parsed.Paths);
        Assert.True(parsed.HasPaths);
        Assert.Null(parsed.DeleteAfterUnlock);
    }

    [Fact]
    public void Parses_the_elevated_relaunch_form()
    {
        var parsed = CommandLineArgs.Parse(
            ["--elevated", "--action", "unlock-delete", "--", @"C:\a.txt"]);

        Assert.True(parsed.Elevated);
        Assert.True(parsed.DeleteAfterUnlock);
        Assert.Equal([@"C:\a.txt"], parsed.Paths);
    }

    [Fact]
    public void Parses_the_unlock_only_action()
    {
        var parsed = CommandLineArgs.Parse(["--elevated", "--action", "unlock", "--", @"C:\a.txt"]);

        Assert.False(parsed.DeleteAfterUnlock!.Value);
    }

    [Fact]
    public void Everything_after_the_separator_is_a_path_even_if_it_looks_like_a_flag()
    {
        var parsed = CommandLineArgs.Parse(["--", "--weird-file-name"]);

        Assert.Equal(["--weird-file-name"], parsed.Paths);
    }

    [Fact]
    public void An_unknown_action_value_leaves_the_action_unset()
    {
        var parsed = CommandLineArgs.Parse(["--action", "nonsense", "--", @"C:\a.txt"]);

        Assert.Null(parsed.DeleteAfterUnlock);
        Assert.Equal([@"C:\a.txt"], parsed.Paths);
    }

    [Fact]
    public void A_trailing_action_flag_with_no_value_does_not_throw()
    {
        var parsed = CommandLineArgs.Parse(["--action"]);

        Assert.Null(parsed.DeleteAfterUnlock);
        Assert.Empty(parsed.Paths);
    }

    [Fact]
    public void Round_trips_the_relaunch_arguments()
    {
        var original = new[] { @"C:\a.txt", @"C:\b c\d.txt" };

        var parsed = CommandLineArgs.Parse(
            ["--elevated", "--action", "unlock-delete", "--", .. original]);

        Assert.Equal(original, parsed.Paths);
        Assert.True(parsed.Elevated);
        Assert.True(parsed.DeleteAfterUnlock);
    }
}
