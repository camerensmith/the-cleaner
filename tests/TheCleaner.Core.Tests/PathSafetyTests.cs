using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class PathSafetyTests
{
    // A fixed, fake protected set keeps these tests identical on every machine.
    private static PathSafety MakeSafety() =>
        new([@"C:\Users\tester", @"C:\Windows"]);

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"D:\")]
    public void Refuses_volume_roots(string path)
    {
        var verdict = MakeSafety().Check(path);
        Assert.False(verdict.Allowed);
        Assert.Contains("volume root", verdict.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Users\tester")]
    [InlineData(@"C:\Users\tester\")]
    [InlineData(@"C:\users\TESTER")]
    [InlineData(@"C:\Windows")]
    public void Refuses_protected_paths_case_insensitively(string path)
    {
        var verdict = MakeSafety().Check(path);
        Assert.False(verdict.Allowed);
        Assert.Contains("protected", verdict.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_an_ancestor_of_a_protected_path()
    {
        // Deleting C:\Users would take the profile root with it.
        var verdict = MakeSafety().Check(@"C:\Users");
        Assert.False(verdict.Allowed);
        Assert.Contains("protected", verdict.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Users\tester\Desktop\junk.txt")]
    [InlineData(@"C:\Windows.old\stuff")]
    [InlineData(@"D:\builds\output")]
    public void Allows_ordinary_paths(string path)
    {
        Assert.True(MakeSafety().Check(path).Allowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_blank_paths(string path)
    {
        var verdict = MakeSafety().Check(path);
        Assert.False(verdict.Allowed);
        Assert.Contains("empty", verdict.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_an_unparseable_path_instead_of_throwing()
    {
        var verdict = MakeSafety().Check("C:\\bad\0path");
        Assert.False(verdict.Allowed);
        Assert.NotNull(verdict.Reason);
    }

    [Fact]
    public void Default_protected_paths_include_the_profile_and_windows_directories()
    {
        var defaults = PathSafety.DefaultProtectedPaths();
        Assert.Contains(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            defaults);
        Assert.Contains(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            defaults);
    }
}
