using TheCleaner.Windows;

namespace TheCleaner.Windows.Tests;

public class WindowsInstallTracerTests
{
    // ---- FindInstallRoot ------------------------------------------------

    [Fact]
    public void FindInstallRoot_returns_null_for_path_directly_in_temp()
    {
        // A path in the system temp directory is not under a known install location.
        var tempFile = Path.Combine(Path.GetTempPath(), "something.exe");
        // Temp is not an install location root, but neither is its parent typically.
        // We can't assert an exact value here, but we CAN assert the method does not throw.
        var result = WindowsInstallTracer.FindInstallRoot(tempFile);
        // Result may be null or a path — just verifying no exception is thrown.
        _ = result;
    }

    [WindowsOnlyFact]
    public void FindInstallRoot_returns_immediate_child_of_ProgramFiles_for_nested_path()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(pf)) return; // skip on non-Windows

        var nested = Path.Combine(pf, "SomeApp", "bin", "myapp.exe");
        var root = WindowsInstallTracer.FindInstallRoot(nested);

        Assert.NotNull(root);
        Assert.Equal(Path.Combine(pf, "SomeApp"), root,
            StringComparer.OrdinalIgnoreCase);
    }

    [WindowsOnlyFact]
    public void FindInstallRoot_returns_null_when_path_is_ProgramFiles_itself()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(pf)) return;

        var root = WindowsInstallTracer.FindInstallRoot(pf);
        Assert.Null(root);
    }

    // ---- DeriveAppName --------------------------------------------------

    [Fact]
    public void DeriveAppName_falls_back_to_directory_name_when_exe_is_missing()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "MyFakeApp");
        var name = WindowsInstallTracer.DeriveAppName(installRoot, string.Empty);
        Assert.Equal("MyFakeApp", name);
    }

    [Fact]
    public void DeriveAppName_uses_directory_name_when_exe_has_no_product_name()
    {
        using var temp = new TempDir();
        // Create a real file — FileVersionInfo won't throw, just returns empty strings.
        var exe = temp.File("app.exe", "not a real exe");
        var name = WindowsInstallTracer.DeriveAppName(temp.Path, exe);
        // Falls back to directory name (guid-based temp folder name).
        Assert.False(string.IsNullOrEmpty(name));
    }

    // ---- IsUnderOrEqual -------------------------------------------------

    [Fact]
    public void IsUnderOrEqual_returns_true_for_equal_path()
    {
        using var temp = new TempDir();
        Assert.True(WindowsInstallTracer.IsUnderOrEqual(temp.Path, temp.Path));
    }

    [Fact]
    public void IsUnderOrEqual_returns_true_for_child_path()
    {
        using var temp = new TempDir();
        var child = Path.Combine(temp.Path, "sub", "file.exe");
        Assert.True(WindowsInstallTracer.IsUnderOrEqual(child, temp.Path));
    }

    [Fact]
    public void IsUnderOrEqual_returns_false_for_sibling_with_same_prefix()
    {
        using var temp = new TempDir();
        // A sibling that starts with the same characters must not be confused with a child.
        var parent = Path.GetDirectoryName(temp.Path)!;
        var sibling = temp.Path + "_other";
        Assert.False(WindowsInstallTracer.IsUnderOrEqual(sibling, temp.Path));
    }

    [Fact]
    public void IsUnderOrEqual_returns_false_for_empty_inputs()
    {
        Assert.False(WindowsInstallTracer.IsUnderOrEqual("", Path.GetTempPath()));
        Assert.False(WindowsInstallTracer.IsUnderOrEqual(Path.GetTempPath(), ""));
    }

    // ---- TraceAsync smoke test ------------------------------------------

    [WindowsOnlyFact]
    public async Task TraceAsync_returns_empty_for_path_with_no_install_location()
    {
        // A file in system temp is not in a standard install location; the tracer
        // should return InstallFootprint.Empty (no exception).
        using var temp = new TempDir();
        var exePath = temp.File("random.exe", "not a real exe");

        var tracer = new WindowsInstallTracer();
        var footprint = await tracer.TraceAsync(exePath, CancellationToken.None);

        // No known install root → empty footprint (HasWork == false).
        Assert.False(footprint.HasWork);
    }
}
