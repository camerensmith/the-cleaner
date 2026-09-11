# thecleaner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a drag-and-drop Windows desktop tool that shows which processes lock a file or folder, then on confirm closes their handles, terminates the lockers, and optionally deletes the target recursively.

**Architecture:** A platform-neutral `TheCleaner.Core` owns path safety, recursive expansion, orchestration, result models, and run logging, and depends only on the `ILockKiller` interface. Platform assemblies (`TheCleaner.Windows` real, `TheCleaner.Linux` / `TheCleaner.Mac` stubs) implement `ILockKiller`; the Avalonia app picks one at **compile time** via an MSBuild-conditional `ProjectReference` keyed off the publish RID. The Windows backend layers Restart Manager → NT handle enumeration → handle close → terminate → delete → UAC re-launch.

**Tech Stack:** .NET 8 (`net8.0`), Avalonia UI 11.2.3, xUnit 2.9.2, Win32 P/Invoke (`rstrtmgr.dll`, `ntdll.dll`, `kernel32.dll`).

## Global Constraints

- Target framework is `net8.0` for **every** project. Do not use `net8.0-windows` — the Windows backend must compile on any host so the solution builds everywhere. (Verified: the installed .NET 9.0.202 SDK builds `net8.0` fine.)
- Avalonia package version is exactly `11.2.3` everywhere. xUnit `2.9.2`, `xunit.runner.visualstudio` `2.8.2`, `Microsoft.NET.Test.Sdk` `17.12.0`.
- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in every project.
- `TheCleaner.Core` must have **zero** `System.Runtime.InteropServices` P/Invoke and zero Avalonia references. All Win32 lives in `TheCleaner.Windows`.
- Termination is **always on** in v1 (`UnlockOptions.TerminateLockers` defaults to `true`), but only ever runs after an explicit user confirm.
- Never terminate: our own PID, PID 0, PID 4, or a process whose name (case-insensitive, no extension) is one of `csrss`, `smss`, `wininit`, `services`, `lsass`, `System`, `winlogon`. Report these as skipped/protected — never as a crash.
- Refused paths are a **hard error with no action taken**: volume roots (`C:\`), the user profile root, the Windows directory, and any ancestor of those.
- Log file path is exactly `%LOCALAPPDATA%\thecleaner\last-run.log`, overwritten each run.
- The app starts **unelevated**; it relaunches elevated only after a real access-denied.
- One failure never aborts the rest — results are per-path.
- Commit after every task. Use `feat:`, `test:`, `chore:` prefixes.

## File Structure

```
thecleaner.sln
src/
  TheCleaner/                        Avalonia app: drop zone, confirm, results
    TheCleaner.csproj                MSBuild platform selection + publish config
    app.manifest                     asInvoker + DPI awareness
    Program.cs                       entry point, argv parsing
    CommandLine.cs                   argv <-> relaunch contract
    PlatformBackend.cs               #if-based ILockKiller selection
    App.axaml / App.axaml.cs         Avalonia app + Fluent theme
    MainWindow.axaml / .cs           4-state view: Idle, Confirm, Busy, Results
  TheCleaner.Core/
    LockHolder.cs                    LockHolder, LockHolderSource
    UnlockOptions.cs                 UnlockOptions
    Results.cs                       PathOutcome, PathResult, TerminatedProcess,
                                     SkippedProcess, TerminationReport, KillResult
    ILockKiller.cs                   the platform contract
    PathSafety.cs                    refusal rules
    PathExpander.cs                  recursive expansion, deepest-first dirs
    ScanResult.cs                    what the confirm UI renders
    CleanerService.cs                orchestration
    RunLogger.cs                     last-run.log
  TheCleaner.Windows/
    Interop/NativeMethods.cs         all DllImports + structs + constants
    RestartManagerLockFinder.cs      primary finder
    NtHandleLockFinder.cs            fallback finder
    HandleReleaser.cs                remote handle close
    ProcessTerminator.cs             terminate + protected list
    Deleter.cs                       bottom-up delete, read-only clear
    Elevation.cs                     IsElevated + Relaunch
    WindowsLockKiller.cs             pipeline composition
  TheCleaner.Linux/LinuxLockKiller.cs
  TheCleaner.Mac/MacLockKiller.cs
tests/
  TheCleaner.Core.Tests/             pure unit tests, run on any OS
  TheCleaner.Windows.Tests/          real-lock integration tests, skipped off-Windows
docs/superpowers/
  specs/2026-09-06-thecleaner-design.md
  plans/2026-09-11-thecleaner-implementation.md
  MANUAL-TESTS.md                    the manual checklist from the spec
README.md
```

---

### Task 1: Solution scaffold and Core contracts

**Files:**
- Create: `thecleaner.sln`
- Create: `src/TheCleaner.Core/TheCleaner.Core.csproj`
- Create: `src/TheCleaner.Core/LockHolder.cs`
- Create: `src/TheCleaner.Core/UnlockOptions.cs`
- Create: `src/TheCleaner.Core/Results.cs`
- Create: `src/TheCleaner.Core/ILockKiller.cs`
- Create: `Directory.Build.props`
- Create: `.gitignore`
- Test: `tests/TheCleaner.Core.Tests/TheCleaner.Core.Tests.csproj`, `tests/TheCleaner.Core.Tests/ResultsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: every type below. Every later task depends on these exact names and signatures — do not rename them.

- [ ] **Step 1: Create the repo-wide props and gitignore**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`.gitignore`:

```
bin/
obj/
*.user
.vs/
artifacts/
```

- [ ] **Step 2: Create the solution and the two projects**

```bash
dotnet new sln -n thecleaner
dotnet new classlib -o src/TheCleaner.Core -n TheCleaner.Core
dotnet new xunit -o tests/TheCleaner.Core.Tests -n TheCleaner.Core.Tests
rm src/TheCleaner.Core/Class1.cs tests/TheCleaner.Core.Tests/UnitTest1.cs
dotnet sln add src/TheCleaner.Core/TheCleaner.Core.csproj tests/TheCleaner.Core.Tests/TheCleaner.Core.Tests.csproj
dotnet add tests/TheCleaner.Core.Tests reference src/TheCleaner.Core
```

Then pin the test package versions in `tests/TheCleaner.Core.Tests/TheCleaner.Core.Tests.csproj` so its `ItemGroup` reads exactly:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
```

Remove any `<TargetFramework>`, `<Nullable>`, or `<ImplicitUsings>` lines the templates added to the two `.csproj` files — `Directory.Build.props` supplies them. Leave `<IsPackable>false</IsPackable>` in the test project.

- [ ] **Step 3: Write the failing test**

`tests/TheCleaner.Core.Tests/ResultsTests.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class ResultsTests
{
    [Fact]
    public void KillResult_counts_outcomes_by_category()
    {
        var result = new KillResult(
            Paths:
            [
                new PathResult(@"C:\a.txt", PathOutcome.Deleted),
                new PathResult(@"C:\b.txt", PathOutcome.Deleted),
                new PathResult(@"C:\c.txt", PathOutcome.Unlocked),
                new PathResult(@"C:\d.txt", PathOutcome.Failed, "still locked"),
                new PathResult(@"C:\", PathOutcome.Refused, "volume root")
            ],
            Terminated: [new TerminatedProcess(123, "notepad")],
            Skipped: [new SkippedProcess(4, "System", "protected process")],
            HandlesClosed: 2,
            ElevationRequired: false);

        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(1, result.UnlockedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RefusedCount);
        Assert.True(result.AnyFailures);
    }

    [Fact]
    public void KillResult_with_no_failures_reports_none()
    {
        var result = new KillResult(
            Paths: [new PathResult(@"C:\a.txt", PathOutcome.Deleted)],
            Terminated: [],
            Skipped: [],
            HandlesClosed: 0,
            ElevationRequired: false);

        Assert.False(result.AnyFailures);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public void UnlockOptions_terminates_lockers_by_default()
    {
        var options = new UnlockOptions(DeleteAfterUnlock: true);
        Assert.True(options.TerminateLockers);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Core.Tests`
Expected: FAIL — compile errors, `KillResult`/`PathResult`/`UnlockOptions` do not exist.

- [ ] **Step 5: Write the contracts**

`src/TheCleaner.Core/LockHolder.cs`:

```csharp
namespace TheCleaner.Core;

/// <summary>How a <see cref="LockHolder"/> was discovered.</summary>
public enum LockHolderSource
{
    /// <summary>Windows Restart Manager. Reports processes for the whole registered
    /// batch, so it cannot attribute a holder to a single file.</summary>
    RestartManager,

    /// <summary>System handle-table scan. Attributes each holder to an exact path.</summary>
    HandleScan
}

/// <param name="Pid">Process id holding the lock.</param>
/// <param name="ProcessName">Process name without extension, e.g. "notepad".</param>
/// <param name="Path">
/// The locked path when the source can attribute one (<see cref="LockHolderSource.HandleScan"/>),
/// otherwise the scanned root that the batch was registered under
/// (<see cref="LockHolderSource.RestartManager"/>). Never null; may be empty.
/// </param>
/// <param name="Source">Which discovery mechanism found this holder.</param>
public sealed record LockHolder(int Pid, string ProcessName, string Path, LockHolderSource Source);
```

`src/TheCleaner.Core/UnlockOptions.cs`:

```csharp
namespace TheCleaner.Core;

/// <param name="DeleteAfterUnlock">Delete the targets once the lockers are gone.</param>
/// <param name="TerminateLockers">Always true in v1; kept explicit for v2 opt-out.</param>
public sealed record UnlockOptions(bool DeleteAfterUnlock, bool TerminateLockers = true);
```

`src/TheCleaner.Core/Results.cs`:

```csharp
namespace TheCleaner.Core;

public enum PathOutcome
{
    /// <summary>Nothing held the path to begin with.</summary>
    NoLockFound,

    /// <summary>Lockers were released; the path was kept.</summary>
    Unlocked,

    /// <summary>The path was removed from disk.</summary>
    Deleted,

    /// <summary>Unlock or delete failed. <see cref="PathResult.Message"/> says why.</summary>
    Failed,

    /// <summary>A safety rail rejected the path before any action was taken.</summary>
    Refused
}

public sealed record PathResult(string Path, PathOutcome Outcome, string? Message = null);

public sealed record TerminatedProcess(int Pid, string ProcessName);

public sealed record SkippedProcess(int Pid, string ProcessName, string Reason);

/// <param name="AccessDenied">True when a terminate failed with ERROR_ACCESS_DENIED,
/// which is the signal to re-launch elevated.</param>
public sealed record TerminationReport(
    IReadOnlyList<TerminatedProcess> Terminated,
    IReadOnlyList<SkippedProcess> Skipped,
    bool AccessDenied);

public sealed record KillResult(
    IReadOnlyList<PathResult> Paths,
    IReadOnlyList<TerminatedProcess> Terminated,
    IReadOnlyList<SkippedProcess> Skipped,
    int HandlesClosed,
    bool ElevationRequired)
{
    public int DeletedCount => Count(PathOutcome.Deleted);
    public int UnlockedCount => Count(PathOutcome.Unlocked);
    public int FailedCount => Count(PathOutcome.Failed);
    public int RefusedCount => Count(PathOutcome.Refused);

    public bool AnyFailures => FailedCount > 0 || RefusedCount > 0;

    private int Count(PathOutcome outcome)
    {
        var n = 0;
        foreach (var p in Paths)
        {
            if (p.Outcome == outcome) n++;
        }
        return n;
    }

    public static KillResult Empty { get; } = new([], [], [], 0, false);
}
```

`src/TheCleaner.Core/ILockKiller.cs`:

```csharp
namespace TheCleaner.Core;

/// <summary>
/// Platform backend. Implementations receive <b>already-expanded, already-safety-checked</b>
/// concrete paths from <see cref="CleanerService"/> — they never expand folders themselves
/// and never re-apply safety rules.
/// </summary>
public interface ILockKiller
{
    Task<IReadOnlyList<LockHolder>> FindLockersAsync(
        IReadOnlyList<string> paths, CancellationToken ct);

    /// <summary>
    /// Closes handles, terminates lockers, and optionally deletes. <paramref name="paths"/>
    /// may mix files and directories in any order; the implementation is responsible for
    /// deleting deepest-first.
    /// </summary>
    Task<KillResult> UnlockAsync(
        IReadOnlyList<string> paths, UnlockOptions options, CancellationToken ct);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Core.Tests`
Expected: PASS — 3 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution and TheCleaner.Core contracts"
```

---

### Task 2: Path safety rails

**Files:**
- Create: `src/TheCleaner.Core/PathSafety.cs`
- Test: `tests/TheCleaner.Core.Tests/PathSafetyTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `PathSafetyVerdict(bool Allowed, string? Reason)`; `PathSafety` with `PathSafety(IEnumerable<string>? protectedPaths = null)`, `PathSafetyVerdict Check(string path)`, and `static IReadOnlyList<string> DefaultProtectedPaths()`.

- [ ] **Step 1: Write the failing test**

`tests/TheCleaner.Core.Tests/PathSafetyTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Core.Tests --filter PathSafetyTests`
Expected: FAIL — `PathSafety` does not exist.

- [ ] **Step 3: Write the implementation**

`src/TheCleaner.Core/PathSafety.cs`:

```csharp
namespace TheCleaner.Core;

public sealed record PathSafetyVerdict(bool Allowed, string? Reason)
{
    public static PathSafetyVerdict Ok { get; } = new(true, null);
    public static PathSafetyVerdict Refuse(string reason) => new(false, reason);
}

/// <summary>
/// Hard refusals. A refused path produces an error and <b>no action at all</b> —
/// it is never unlocked, never deleted, never passed to a platform backend.
/// </summary>
public sealed class PathSafety
{
    private readonly string[] _protected;

    /// <param name="protectedPaths">
    /// Paths that may not be targeted, nor have an ancestor targeted.
    /// Defaults to <see cref="DefaultProtectedPaths"/>.
    /// </param>
    public PathSafety(IEnumerable<string>? protectedPaths = null)
    {
        var source = protectedPaths ?? DefaultProtectedPaths();
        var normalized = new List<string>();
        foreach (var p in source)
        {
            if (TryNormalize(p, out var n)) normalized.Add(n);
        }
        _protected = normalized.ToArray();
    }

    public static IReadOnlyList<string> DefaultProtectedPaths() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows)
    ];

    public PathSafetyVerdict Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathSafetyVerdict.Refuse("Path is empty.");

        if (!TryNormalize(path, out var full))
            return PathSafetyVerdict.Refuse($"Path could not be parsed: {path}");

        var root = Path.GetPathRoot(full);
        if (root is not null && Equals(TrimSeparators(root), full))
            return PathSafetyVerdict.Refuse($"Refusing to touch the volume root {full}.");

        foreach (var prot in _protected)
        {
            if (Equals(full, prot))
                return PathSafetyVerdict.Refuse($"Refusing to touch the protected path {full}.");

            if (IsAncestorOf(full, prot))
            {
                return PathSafetyVerdict.Refuse(
                    $"Refusing to touch {full} because it contains the protected path {prot}.");
            }
        }

        return PathSafetyVerdict.Ok;
    }

    /// <summary>Full path, separators normalized, trailing separators trimmed
    /// (except on a bare volume root, which keeps none either).</summary>
    private static bool TryNormalize(string path, out string normalized)
    {
        try
        {
            normalized = TrimSeparators(Path.GetFullPath(path));
            return normalized.Length > 0;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static string TrimSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // "C:" trims to "C:" — keep it, it compares equal to the root form below.
        return trimmed.Length == 0 ? path : trimmed;
    }

    private static bool Equals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>candidate</c> is a strict parent directory of <c>descendant</c>.</summary>
    private static bool IsAncestorOf(string candidate, string descendant)
    {
        if (descendant.Length <= candidate.Length) return false;
        if (!descendant.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return false;
        var next = descendant[candidate.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Core.Tests`
Expected: PASS — all tests including the Task 1 set.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: refuse volume roots, profile root, and Windows directory"
```

---

### Task 3: Recursive path expansion

**Files:**
- Create: `src/TheCleaner.Core/PathExpander.cs`
- Test: `tests/TheCleaner.Core.Tests/PathExpanderTests.cs`
- Test: `tests/TheCleaner.Core.Tests/TempDir.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `ExpandedTarget(IReadOnlyList<string> Files, IReadOnlyList<string> Directories, IReadOnlyList<string> Errors)` with `int FileCount`, `IReadOnlyList<string> AllPaths`; `PathExpander` with `ExpandedTarget Expand(IEnumerable<string> roots)`.
  `Directories` is ordered **deepest-first** so a caller can delete bottom-up. `AllPaths` is `Files` followed by `Directories`.

- [ ] **Step 1: Write the test helper**

`tests/TheCleaner.Core.Tests/TempDir.cs`:

```csharp
namespace TheCleaner.Core.Tests;

/// <summary>A real directory under the temp folder, deleted on dispose.
/// These tests hit the actual filesystem — expansion is about the filesystem.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "thecleaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string relative, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Dir(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp dir must not fail a test run.
        }
    }
}
```

- [ ] **Step 2: Write the failing test**

`tests/TheCleaner.Core.Tests/PathExpanderTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Core.Tests --filter PathExpanderTests`
Expected: FAIL — `PathExpander` does not exist.

- [ ] **Step 4: Write the implementation**

`src/TheCleaner.Core/PathExpander.cs`:

```csharp
namespace TheCleaner.Core;

/// <param name="Files">Every concrete file under the roots.</param>
/// <param name="Directories">Every directory, deepest-first, roots last.</param>
/// <param name="Errors">Human-readable problems; expansion never throws for these.</param>
public sealed record ExpandedTarget(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Directories,
    IReadOnlyList<string> Errors)
{
    public int FileCount => Files.Count;

    /// <summary>Files first, then directories deepest-first — the order a deleter wants.</summary>
    public IReadOnlyList<string> AllPaths => [.. Files, .. Directories];

    public static ExpandedTarget Empty { get; } = new([], [], []);
}

/// <summary>Walks roots depth-first into concrete paths. Symlinked directories are
/// recorded but not followed, so a link loop cannot hang the walk.</summary>
public sealed class PathExpander
{
    public ExpandedTarget Expand(IEnumerable<string> roots)
    {
        var files = new List<string>();
        var dirs = new List<string>();
        var errors = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            string full;
            try
            {
                full = Path.GetFullPath(root);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"{root}: {e.Message}");
                continue;
            }

            if (File.Exists(full))
            {
                if (seenFiles.Add(full)) files.Add(full);
            }
            else if (Directory.Exists(full))
            {
                Walk(full, files, dirs, errors, seenFiles, seenDirs);
            }
            else
            {
                errors.Add($"{full}: path not found.");
            }
        }

        dirs.Sort(static (a, b) => Depth(b).CompareTo(Depth(a)));
        return new ExpandedTarget(files, dirs, errors);
    }

    private static void Walk(
        string dir,
        List<string> files,
        List<string> dirs,
        List<string> errors,
        HashSet<string> seenFiles,
        HashSet<string> seenDirs)
    {
        if (!seenDirs.Add(dir)) return;
        dirs.Add(dir);

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{dir}: {e.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(entry);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{entry}: {e.Message}");
                continue;
            }

            var isDir = attrs.HasFlag(FileAttributes.Directory);
            var isLink = attrs.HasFlag(FileAttributes.ReparsePoint);

            if (isDir && !isLink)
            {
                Walk(entry, files, dirs, errors, seenFiles, seenDirs);
            }
            else if (isDir)
            {
                // A directory symlink/junction: delete the link, never its contents.
                if (seenDirs.Add(entry)) dirs.Add(entry);
            }
            else
            {
                if (seenFiles.Add(entry)) files.Add(entry);
            }
        }
    }

    private static int Depth(string path)
    {
        var n = 0;
        foreach (var c in path)
        {
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) n++;
        }
        return n;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Core.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: expand folders depth-first with deepest-first directory order"
```

---

### Task 4: CleanerService orchestration

**Files:**
- Create: `src/TheCleaner.Core/ScanResult.cs`
- Create: `src/TheCleaner.Core/CleanerService.cs`
- Test: `tests/TheCleaner.Core.Tests/FakeLockKiller.cs`
- Test: `tests/TheCleaner.Core.Tests/CleanerServiceTests.cs`

**Interfaces:**
- Consumes: `ILockKiller`, `LockHolder`, `LockHolderSource`, `UnlockOptions`, `KillResult`, `PathResult`, `PathOutcome` (Task 1); `PathSafety` (Task 2); `PathExpander`, `ExpandedTarget` (Task 3).
- Produces:
  - `ScanResult(IReadOnlyList<string> Roots, IReadOnlyList<string> AcceptedPaths, IReadOnlyList<PathResult> Refused, IReadOnlyList<LockHolder> Holders, int FileCount, int DirectoryCount, IReadOnlyList<string> Errors)` with `bool HasWork` and `bool AnyRefusals`.
  - `CleanerService(ILockKiller killer, PathSafety? safety = null, PathExpander? expander = null)` with `Task<ScanResult> ScanAsync(IReadOnlyList<string> roots, CancellationToken ct)` and `Task<KillResult> RunAsync(ScanResult scan, UnlockOptions options, CancellationToken ct)`.

- [ ] **Step 1: Write the fake backend**

`tests/TheCleaner.Core.Tests/FakeLockKiller.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public sealed class FakeLockKiller : ILockKiller
{
    public List<string> FindCalledWith { get; } = [];
    public List<string> UnlockCalledWith { get; } = [];
    public UnlockOptions? UnlockOptionsUsed { get; private set; }

    public IReadOnlyList<LockHolder> Holders { get; set; } = [];
    public Func<IReadOnlyList<string>, KillResult>? UnlockResultFactory { get; set; }

    public Task<IReadOnlyList<LockHolder>> FindLockersAsync(
        IReadOnlyList<string> paths, CancellationToken ct)
    {
        FindCalledWith.AddRange(paths);
        return Task.FromResult(Holders);
    }

    public Task<KillResult> UnlockAsync(
        IReadOnlyList<string> paths, UnlockOptions options, CancellationToken ct)
    {
        UnlockCalledWith.AddRange(paths);
        UnlockOptionsUsed = options;

        var result = UnlockResultFactory?.Invoke(paths)
            ?? new KillResult(
                Paths: [.. paths.Select(p => new PathResult(p, PathOutcome.Deleted))],
                Terminated: [],
                Skipped: [],
                HandlesClosed: 0,
                ElevationRequired: false);

        return Task.FromResult(result);
    }
}
```

- [ ] **Step 2: Write the failing test**

`tests/TheCleaner.Core.Tests/CleanerServiceTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Core.Tests --filter CleanerServiceTests`
Expected: FAIL — `CleanerService` and `ScanResult` do not exist.

- [ ] **Step 4: Write the implementation**

`src/TheCleaner.Core/ScanResult.cs`:

```csharp
namespace TheCleaner.Core;

/// <summary>Everything the confirm UI needs to render before the user commits.</summary>
public sealed record ScanResult(
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> AcceptedPaths,
    IReadOnlyList<PathResult> Refused,
    IReadOnlyList<LockHolder> Holders,
    int FileCount,
    int DirectoryCount,
    IReadOnlyList<string> Errors)
{
    public bool HasWork => AcceptedPaths.Count > 0;
    public bool AnyRefusals => Refused.Count > 0;

    public static ScanResult Empty { get; } = new([], [], [], [], 0, 0, []);
}
```

`src/TheCleaner.Core/CleanerService.cs`:

```csharp
namespace TheCleaner.Core;

/// <summary>
/// Owns the order of operations: safety-check the roots, expand them, ask the backend
/// who holds them, and — only after the caller confirms — hand the expanded list over.
/// </summary>
public sealed class CleanerService
{
    private readonly ILockKiller _killer;
    private readonly PathSafety _safety;
    private readonly PathExpander _expander;

    public CleanerService(ILockKiller killer, PathSafety? safety = null, PathExpander? expander = null)
    {
        _killer = killer;
        _safety = safety ?? new PathSafety();
        _expander = expander ?? new PathExpander();
    }

    public async Task<ScanResult> ScanAsync(IReadOnlyList<string> roots, CancellationToken ct)
    {
        var accepted = new List<string>();
        var refused = new List<PathResult>();

        foreach (var root in roots)
        {
            var verdict = _safety.Check(root);
            if (verdict.Allowed) accepted.Add(root);
            else refused.Add(new PathResult(root, PathOutcome.Refused, verdict.Reason));
        }

        var expanded = accepted.Count > 0 ? _expander.Expand(accepted) : ExpandedTarget.Empty;

        var holders = expanded.AllPaths.Count > 0
            ? await _killer.FindLockersAsync(expanded.AllPaths, ct).ConfigureAwait(false)
            : [];

        return new ScanResult(
            Roots: roots,
            AcceptedPaths: expanded.AllPaths,
            Refused: refused,
            Holders: holders,
            FileCount: expanded.FileCount,
            DirectoryCount: expanded.Directories.Count,
            Errors: expanded.Errors);
    }

    /// <summary>Runs the unlock the user confirmed. Never throws: a backend failure
    /// becomes a <see cref="PathOutcome.Failed"/> entry for every path it was given.</summary>
    public async Task<KillResult> RunAsync(ScanResult scan, UnlockOptions options, CancellationToken ct)
    {
        if (!scan.HasWork)
            return new KillResult(scan.Refused, [], [], 0, false);

        KillResult backend;
        try
        {
            backend = await _killer.UnlockAsync(scan.AcceptedPaths, options, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            backend = new KillResult(
                Paths: [.. scan.AcceptedPaths.Select(p => new PathResult(p, PathOutcome.Failed, e.Message))],
                Terminated: [],
                Skipped: [],
                HandlesClosed: 0,
                ElevationRequired: false);
        }

        return backend with { Paths = [.. scan.Refused, .. backend.Paths] };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Core.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: orchestrate scan and run with per-path results"
```

---

### Task 5: Run logging

**Files:**
- Create: `src/TheCleaner.Core/RunLogger.cs`
- Test: `tests/TheCleaner.Core.Tests/RunLoggerTests.cs`

**Interfaces:**
- Consumes: `ScanResult` (Task 4), `KillResult`, `UnlockOptions`, `PathResult`, `TerminatedProcess`, `SkippedProcess` (Task 1).
- Produces: `RunLogger(string logPath)`, `static string DefaultLogPath { get; }`, `void Write(ScanResult scan, KillResult result, UnlockOptions options)`, `void WriteError(string message)`.

- [ ] **Step 1: Write the failing test**

`tests/TheCleaner.Core.Tests/RunLoggerTests.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class RunLoggerTests
{
    private static ScanResult MakeScan() =>
        new(
            Roots: [@"C:\work\locked.txt"],
            AcceptedPaths: [@"C:\work\locked.txt"],
            Refused: [],
            Holders: [new LockHolder(4321, "notepad", @"C:\work\locked.txt", LockHolderSource.HandleScan)],
            FileCount: 1,
            DirectoryCount: 0,
            Errors: []);

    private static KillResult MakeResult() =>
        new(
            Paths: [new PathResult(@"C:\work\locked.txt", PathOutcome.Deleted)],
            Terminated: [new TerminatedProcess(4321, "notepad")],
            Skipped: [new SkippedProcess(4, "System", "protected process")],
            HandlesClosed: 1,
            ElevationRequired: false);

    [Fact]
    public void Writes_a_log_containing_targets_holders_and_outcomes()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "last-run.log");

        new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(DeleteAfterUnlock: true));

        var text = File.ReadAllText(path);
        Assert.Contains(@"C:\work\locked.txt", text);
        Assert.Contains("notepad", text);
        Assert.Contains("4321", text);
        Assert.Contains("Deleted", text);
        Assert.Contains("protected process", text);
        Assert.Contains("Handles closed: 1", text);
    }

    [Fact]
    public void Creates_the_log_directory_when_it_is_missing()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "nested", "deeper", "last-run.log");

        new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(DeleteAfterUnlock: true));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Overwrites_the_previous_run()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "last-run.log");
        File.WriteAllText(path, "PREVIOUS RUN CONTENT");

        new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(DeleteAfterUnlock: true));

        Assert.DoesNotContain("PREVIOUS RUN CONTENT", File.ReadAllText(path));
    }

    [Fact]
    public void WriteError_records_the_message()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "last-run.log");

        new RunLogger(path).WriteError("elevation required / cancelled");

        Assert.Contains("elevation required / cancelled", File.ReadAllText(path));
    }

    [Fact]
    public void An_unwritable_log_path_does_not_throw()
    {
        // A directory where a file should be: opening it for write always fails.
        using var temp = new TempDir();
        var path = temp.Dir("last-run.log");

        var ex = Record.Exception(() =>
            new RunLogger(path).Write(MakeScan(), MakeResult(), new UnlockOptions(true)));

        Assert.Null(ex);
    }

    [Fact]
    public void DefaultLogPath_is_under_local_appdata()
    {
        Assert.EndsWith(
            Path.Combine("thecleaner", "last-run.log"), RunLogger.DefaultLogPath);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Core.Tests --filter RunLoggerTests`
Expected: FAIL — `RunLogger` does not exist.

- [ ] **Step 3: Write the implementation**

`src/TheCleaner.Core/RunLogger.cs`:

```csharp
using System.Text;

namespace TheCleaner.Core;

/// <summary>Writes a plain-text record of the most recent run. Logging is best-effort:
/// it must never be the reason a run fails.</summary>
public sealed class RunLogger
{
    private readonly string _path;

    public RunLogger(string logPath) => _path = logPath;

    public static string DefaultLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "thecleaner",
        "last-run.log");

    public void Write(ScanResult scan, KillResult result, UnlockOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("thecleaner run ").AppendLine(DateTimeOffset.Now.ToString("u"));
        sb.Append("Action: ").AppendLine(options.DeleteAfterUnlock ? "Unlock & Delete" : "Unlock");
        sb.AppendLine();

        sb.AppendLine("Targets:");
        foreach (var root in scan.Roots) sb.Append("  ").AppendLine(root);
        sb.Append("  (").Append(scan.FileCount).Append(" files, ")
          .Append(scan.DirectoryCount).AppendLine(" directories)");
        sb.AppendLine();

        sb.AppendLine("Lockers found:");
        if (scan.Holders.Count == 0) sb.AppendLine("  (none)");
        foreach (var h in scan.Holders)
        {
            sb.Append("  ").Append(h.ProcessName).Append(" (pid ").Append(h.Pid).Append(") [")
              .Append(h.Source).Append("] ").AppendLine(h.Path);
        }
        sb.AppendLine();

        sb.Append("Handles closed: ").AppendLine(result.HandlesClosed.ToString());

        sb.AppendLine("Terminated:");
        if (result.Terminated.Count == 0) sb.AppendLine("  (none)");
        foreach (var t in result.Terminated)
            sb.Append("  ").Append(t.ProcessName).Append(" (pid ").Append(t.Pid).AppendLine(")");

        sb.AppendLine("Skipped:");
        if (result.Skipped.Count == 0) sb.AppendLine("  (none)");
        foreach (var s in result.Skipped)
        {
            sb.Append("  ").Append(s.ProcessName).Append(" (pid ").Append(s.Pid).Append("): ")
              .AppendLine(s.Reason);
        }
        sb.AppendLine();

        sb.AppendLine("Results:");
        foreach (var p in result.Paths)
        {
            sb.Append("  [").Append(p.Outcome).Append("] ").Append(p.Path);
            if (p.Message is not null) sb.Append(" — ").Append(p.Message);
            sb.AppendLine();
        }

        if (scan.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Scan errors:");
            foreach (var e in scan.Errors) sb.Append("  ").AppendLine(e);
        }

        sb.AppendLine();
        sb.Append("Summary: ").Append(result.DeletedCount).Append(" deleted, ")
          .Append(result.UnlockedCount).Append(" unlocked, ")
          .Append(result.FailedCount).Append(" failed, ")
          .Append(result.RefusedCount).AppendLine(" refused");
        if (result.ElevationRequired) sb.AppendLine("Elevation was required.");

        Save(sb.ToString());
    }

    public void WriteError(string message)
    {
        Save($"thecleaner run {DateTimeOffset.Now:u}{Environment.NewLine}ERROR: {message}{Environment.NewLine}");
    }

    private void Save(string text)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Best-effort only.
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: write last-run.log after every run"
```

---

### Task 6: Windows interop surface and the Restart Manager finder

**Files:**
- Create: `src/TheCleaner.Windows/TheCleaner.Windows.csproj`
- Create: `src/TheCleaner.Windows/Interop/NativeMethods.cs`
- Create: `src/TheCleaner.Windows/RestartManagerLockFinder.cs`
- Test: `tests/TheCleaner.Windows.Tests/TheCleaner.Windows.Tests.csproj`
- Test: `tests/TheCleaner.Windows.Tests/WindowsOnlyFactAttribute.cs`
- Test: `tests/TheCleaner.Windows.Tests/TempDir.cs`
- Test: `tests/TheCleaner.Windows.Tests/RestartManagerLockFinderTests.cs`

**Interfaces:**
- Consumes: `LockHolder`, `LockHolderSource` (Task 1).
- Produces: `internal static class NativeMethods` (all P/Invoke for the whole backend — later Windows tasks add to this one file); `public sealed class RestartManagerLockFinder` with `IReadOnlyList<LockHolder> Find(IReadOnlyList<string> paths, string displayPath)` and `public const int MaxRegisteredPaths = 2000`.

- [ ] **Step 1: Create the project and wire it up**

```bash
dotnet new classlib -o src/TheCleaner.Windows -n TheCleaner.Windows
rm src/TheCleaner.Windows/Class1.cs
dotnet sln add src/TheCleaner.Windows/TheCleaner.Windows.csproj
dotnet add src/TheCleaner.Windows reference src/TheCleaner.Core

dotnet new xunit -o tests/TheCleaner.Windows.Tests -n TheCleaner.Windows.Tests
rm tests/TheCleaner.Windows.Tests/UnitTest1.cs
dotnet sln add tests/TheCleaner.Windows.Tests/TheCleaner.Windows.Tests.csproj
dotnet add tests/TheCleaner.Windows.Tests reference src/TheCleaner.Windows src/TheCleaner.Core
```

Strip the template `<TargetFramework>`/`<Nullable>`/`<ImplicitUsings>` from both new `.csproj` files, and pin the test packages to `Microsoft.NET.Test.Sdk` `17.12.0`, `xunit` `2.9.2`, `xunit.runner.visualstudio` `2.8.2`.

Add to `src/TheCleaner.Windows/TheCleaner.Windows.csproj` so the test project can reach `internal` interop types:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="TheCleaner.Windows.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Write the test support files**

`tests/TheCleaner.Windows.Tests/WindowsOnlyFactAttribute.cs`:

```csharp
using System.Runtime.InteropServices;

namespace TheCleaner.Windows.Tests;

/// <summary>A fact that only runs on Windows — the backend is Win32-only, but the
/// solution must still build and test green on other hosts.</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "Windows-only test.";
    }
}
```

`tests/TheCleaner.Windows.Tests/TempDir.cs`: copy the file created in Task 3 verbatim, changing only the namespace to `TheCleaner.Windows.Tests`.

- [ ] **Step 3: Write the failing test**

`tests/TheCleaner.Windows.Tests/RestartManagerLockFinderTests.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Windows.Tests`
Expected: FAIL — `RestartManagerLockFinder` does not exist.

- [ ] **Step 5: Write the interop declarations**

`src/TheCleaner.Windows/Interop/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace TheCleaner.Windows.Interop;

internal static class NativeMethods
{
    // ---- Error codes -------------------------------------------------------
    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_MORE_DATA = 234;

    // ---- Restart Manager ---------------------------------------------------
    internal const int CCH_RM_SESSION_KEY = 32;
    internal const int CCH_RM_MAX_APP_NAME = 255;
    internal const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmStartSession(
        out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[]? rgsFilenames,
        uint nApplications,
        RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        out uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmEndSession(uint pSessionHandle);
}
```

- [ ] **Step 6: Write the finder**

`src/TheCleaner.Windows/RestartManagerLockFinder.cs`:

```csharp
using System.Text;
using TheCleaner.Core;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>
/// Primary locker discovery. Restart Manager answers for the whole registered batch,
/// so every holder it reports carries the caller-supplied <c>displayPath</c> rather
/// than a per-file attribution — <see cref="NtHandleLockFinder"/> supplies exact paths.
/// </summary>
public sealed class RestartManagerLockFinder
{
    /// <summary>Registering a huge batch makes RmRegisterResources crawl. Past this
    /// many paths we register a prefix and let the handle scan cover the rest.</summary>
    public const int MaxRegisteredPaths = 2000;

    private const int RegisterChunkSize = 250;

    public IReadOnlyList<LockHolder> Find(IReadOnlyList<string> paths, string displayPath)
    {
        if (paths.Count == 0) return [];

        var key = new StringBuilder(NativeMethods.CCH_RM_SESSION_KEY + 1);
        if (NativeMethods.RmStartSession(out var session, 0, key) != NativeMethods.ERROR_SUCCESS)
            return [];

        try
        {
            var toRegister = paths.Count > MaxRegisteredPaths
                ? paths.Take(MaxRegisteredPaths).ToArray()
                : paths.ToArray();

            for (var i = 0; i < toRegister.Length; i += RegisterChunkSize)
            {
                var chunk = toRegister.Skip(i).Take(RegisterChunkSize).ToArray();
                var rc = NativeMethods.RmRegisterResources(
                    session, (uint)chunk.Length, chunk, 0, null, 0, null);

                // A bad path in one chunk must not sink the whole scan.
                if (rc != NativeMethods.ERROR_SUCCESS) continue;
            }

            return GetList(session, displayPath);
        }
        finally
        {
            NativeMethods.RmEndSession(session);
        }
    }

    private static IReadOnlyList<LockHolder> GetList(uint session, string displayPath)
    {
        uint count = 0;
        var rc = NativeMethods.RmGetList(session, out var needed, ref count, null, out _);

        if (rc == NativeMethods.ERROR_SUCCESS || needed == 0) return [];
        if (rc != NativeMethods.ERROR_MORE_DATA) return [];

        var infos = new NativeMethods.RM_PROCESS_INFO[needed];
        count = needed;
        rc = NativeMethods.RmGetList(session, out needed, ref count, infos, out _);
        if (rc != NativeMethods.ERROR_SUCCESS) return [];

        var holders = new List<LockHolder>((int)count);
        for (var i = 0; i < count; i++)
        {
            var info = infos[i];
            var name = string.IsNullOrWhiteSpace(info.strAppName)
                ? (string.IsNullOrWhiteSpace(info.strServiceShortName)
                    ? $"pid {info.Process.dwProcessId}"
                    : info.strServiceShortName)
                : info.strAppName;

            holders.Add(new LockHolder(
                info.Process.dwProcessId,
                StripExtension(name),
                displayPath,
                LockHolderSource.RestartManager));
        }

        return holders;
    }

    private static string StripExtension(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Windows.Tests`
Expected: PASS — 5 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: find lockers via Restart Manager"
```

---

### Task 7: NT handle-scan fallback finder

**Files:**
- Modify: `src/TheCleaner.Windows/Interop/NativeMethods.cs` (append the new declarations)
- Create: `src/TheCleaner.Windows/NtHandleLockFinder.cs`
- Test: `tests/TheCleaner.Windows.Tests/NtHandleLockFinderTests.cs`

**Interfaces:**
- Consumes: `NativeMethods` (Task 6), `LockHolder`, `LockHolderSource` (Task 1).
- Produces: `public sealed class NtHandleLockFinder` with `IReadOnlyList<LockHolder> Find(IReadOnlyList<string> paths)`.
  Each returned holder carries the **exact** matching path and `LockHolderSource.HandleScan`.

- [ ] **Step 1: Write the failing test**

`tests/TheCleaner.Windows.Tests/NtHandleLockFinderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Windows.Tests --filter NtHandleLockFinderTests`
Expected: FAIL — `NtHandleLockFinder` does not exist.

- [ ] **Step 3: Append the interop declarations**

Append inside `internal static class NativeMethods` in `src/TheCleaner.Windows/Interop/NativeMethods.cs`:

```csharp
    // ---- Handle enumeration -----------------------------------------------
    internal const int SystemExtendedHandleInformation = 64;
    internal const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;

    internal const int FILE_TYPE_DISK = 0x0001;

    internal const uint PROCESS_DUP_HANDLE = 0x0040;
    internal const uint PROCESS_TERMINATE = 0x0001;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    internal const uint DUPLICATE_SAME_ACCESS = 0x0002;
    internal const uint DUPLICATE_CLOSE_SOURCE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [DllImport("ntdll.dll")]
    internal static extern uint NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int GetFileType(IntPtr hFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetFinalPathNameByHandleW(
        IntPtr hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);
```

- [ ] **Step 4: Write the finder**

`src/TheCleaner.Windows/NtHandleLockFinder.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TheCleaner.Core;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>
/// Fallback locker discovery: walks the system handle table and matches file handles
/// against the target paths. Slower than Restart Manager but attributes each holder
/// to an exact path and catches holders RM does not report.
/// </summary>
public sealed class NtHandleLockFinder
{
    public IReadOnlyList<LockHolder> Find(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return [];

        var targets = new HashSet<string>(paths.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            try { targets.Add(Path.GetFullPath(p)); }
            catch (ArgumentException) { /* unparseable target: nothing can match it */ }
        }

        var entries = Snapshot();
        if (entries.Length == 0) return [];

        // Every file handle shares one object-type index. Learn ours from a handle we
        // opened ourselves, so the expensive duplicate-and-name work runs on file
        // handles only instead of on every handle in the system.
        var fileTypeIndex = FindFileObjectTypeIndex(entries);

        var self = NativeMethods.GetCurrentProcess();
        var currentPid = Environment.ProcessId;
        var found = new Dictionary<(int Pid, string Path), LockHolder>();
        var processHandles = new Dictionary<int, IntPtr>();

        try
        {
            foreach (var entry in entries)
            {
                if (fileTypeIndex.HasValue && entry.ObjectTypeIndex != fileTypeIndex.Value) continue;

                var pid = (int)entry.UniqueProcessId;
                if (pid is 0 or 4) continue;

                if (!processHandles.TryGetValue(pid, out var process))
                {
                    process = pid == currentPid
                        ? self
                        : NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, pid);
                    processHandles[pid] = process;
                }

                if (process == IntPtr.Zero) continue;

                if (!NativeMethods.DuplicateHandle(
                        process, entry.HandleValue, self, out var dup,
                        0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                {
                    continue;
                }

                try
                {
                    // Naming a pipe or socket handle can block forever; disk files cannot.
                    if (NativeMethods.GetFileType(dup) != NativeMethods.FILE_TYPE_DISK) continue;

                    var name = FinalPath(dup);
                    if (name is null || !targets.Contains(name)) continue;

                    var key = (pid, name);
                    if (found.ContainsKey(key)) continue;

                    found[key] = new LockHolder(
                        pid, ProcessNameOf(pid), name, LockHolderSource.HandleScan);
                }
                finally
                {
                    NativeMethods.CloseHandle(dup);
                }
            }
        }
        finally
        {
            foreach (var (pid, handle) in processHandles)
            {
                if (handle != IntPtr.Zero && pid != currentPid) NativeMethods.CloseHandle(handle);
            }
        }

        return [.. found.Values];
    }

    private static NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[] Snapshot()
    {
        var size = 1 << 20;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NativeMethods.NtQuerySystemInformation(
                    NativeMethods.SystemExtendedHandleInformation, buffer, size, out _);

                if (status == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
                {
                    size *= 2;
                    continue;
                }

                if (status != 0) return [];

                var count = Marshal.ReadIntPtr(buffer).ToInt64();
                var entrySize = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
                var start = buffer + (IntPtr.Size * 2); // NumberOfHandles + Reserved

                var entries = new NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[count];
                for (long i = 0; i < count; i++)
                {
                    entries[i] = Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(
                        start + (int)(i * entrySize));
                }
                return entries;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return [];
    }

    /// <summary>Opens a throwaway file and looks its handle up in the snapshot to learn
    /// which object-type index means "File" on this machine.</summary>
    private static ushort? FindFileObjectTypeIndex(
        NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[] entries)
    {
        var probePath = Path.Combine(Path.GetTempPath(), $"thecleaner-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var probe = new FileStream(
                probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);

            var handle = probe.SafeFileHandle.DangerousGetHandle();
            var pid = Environment.ProcessId;

            foreach (var entry in entries)
            {
                if ((int)entry.UniqueProcessId == pid && entry.HandleValue == handle)
                    return entry.ObjectTypeIndex;
            }
        }
        catch (IOException)
        {
            // No probe: fall back to inspecting every handle.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string? FinalPath(IntPtr handle)
    {
        var sb = new StringBuilder(1024);
        var len = NativeMethods.GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
        if (len == 0) return null;

        if (len > sb.Capacity)
        {
            sb = new StringBuilder((int)len + 1);
            len = NativeMethods.GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
            if (len == 0) return null;
        }

        var path = sb.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }

    private static string ProcessNameOf(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return $"pid {pid}";
        }
    }
}
```

Note: the snapshot buffer is freed before the entries are used, but `PtrToStructure` has already copied every entry into managed memory, so no freed memory is read.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Windows.Tests`
Expected: PASS — 10 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add NT handle-scan fallback locker finder"
```

---

### Task 8: Handle release and process termination

**Files:**
- Create: `src/TheCleaner.Windows/HandleReleaser.cs`
- Create: `src/TheCleaner.Windows/ProcessTerminator.cs`
- Test: `tests/TheCleaner.Windows.Tests/HandleReleaserTests.cs`
- Test: `tests/TheCleaner.Windows.Tests/ProcessTerminatorTests.cs`

**Interfaces:**
- Consumes: `NativeMethods` (Tasks 6–7), `LockHolder`, `TerminatedProcess`, `SkippedProcess`, `TerminationReport` (Task 1).
- Produces:
  - `public sealed class HandleReleaser` with `int CloseHandlesFor(IReadOnlyList<string> paths)` returning the number of handles closed.
  - `public sealed class ProcessTerminator` with `static IReadOnlySet<string> ProtectedNames`, `TerminationReport Terminate(IEnumerable<LockHolder> holders)`.

- [ ] **Step 1: Write the failing tests**

`tests/TheCleaner.Windows.Tests/HandleReleaserTests.cs`:

```csharp
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
```

`tests/TheCleaner.Windows.Tests/ProcessTerminatorTests.cs`:

```csharp
using System.Diagnostics;
using TheCleaner.Core;

namespace TheCleaner.Windows.Tests;

public class ProcessTerminatorTests
{
    private static LockHolder Holder(int pid, string name) =>
        new(pid, name, @"C:\irrelevant", LockHolderSource.HandleScan);

    [WindowsOnlyFact]
    public void Terminates_a_real_child_process()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c pause")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        })!;

        try
        {
            var report = new ProcessTerminator().Terminate([Holder(child.Id, "cmd")]);

            Assert.Contains(report.Terminated, t => t.Pid == child.Id);
            Assert.True(child.WaitForExit(10_000), "child did not exit");
            Assert.False(report.AccessDenied);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }

    [WindowsOnlyFact]
    public void Never_terminates_our_own_process()
    {
        var report = new ProcessTerminator().Terminate([Holder(Environment.ProcessId, "testhost")]);

        Assert.Empty(report.Terminated);
        Assert.Contains(report.Skipped, s => s.Pid == Environment.ProcessId);
        Assert.Contains("own process", report.Skipped[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsOnlyFact]
    public void Skips_protected_system_processes_by_name()
    {
        var report = new ProcessTerminator().Terminate([Holder(999_001, "lsass")]);

        Assert.Empty(report.Terminated);
        Assert.Contains(report.Skipped, s => s.ProcessName == "lsass");
        Assert.Contains("protected", report.Skipped[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsOnlyFact]
    public void Skips_the_system_pids()
    {
        var report = new ProcessTerminator().Terminate([Holder(4, "System"), Holder(0, "Idle")]);

        Assert.Empty(report.Terminated);
        Assert.Equal(2, report.Skipped.Count);
    }

    [WindowsOnlyFact]
    public void Deduplicates_repeated_pids()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c pause")
        {
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
        })!;

        try
        {
            var report = new ProcessTerminator().Terminate(
                [Holder(child.Id, "cmd"), Holder(child.Id, "cmd")]);

            Assert.Single(report.Terminated);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }

    [WindowsOnlyFact]
    public void An_already_dead_pid_is_skipped_not_thrown()
    {
        var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
        {
            UseShellExecute = false, CreateNoWindow = true
        })!;
        child.WaitForExit();
        var deadPid = child.Id;
        child.Dispose();

        var report = new ProcessTerminator().Terminate([Holder(deadPid, "cmd")]);

        Assert.Empty(report.Terminated);
        Assert.Single(report.Skipped);
    }

    [WindowsOnlyFact]
    public void Protected_names_cover_the_documented_set()
    {
        foreach (var name in new[] { "csrss", "smss", "wininit", "services", "lsass", "System", "winlogon" })
            Assert.Contains(name, ProcessTerminator.ProtectedNames);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TheCleaner.Windows.Tests --filter "HandleReleaserTests|ProcessTerminatorTests"`
Expected: FAIL — neither class exists.

- [ ] **Step 3: Write HandleReleaser**

`src/TheCleaner.Windows/HandleReleaser.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>
/// Best-effort remote handle close: duplicates each matching handle into this process
/// with DUPLICATE_CLOSE_SOURCE, which severs it in the owning process, then closes our
/// copy. Some holders survive this; termination is the backstop.
/// </summary>
public sealed class HandleReleaser
{
    public int CloseHandlesFor(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return 0;

        var targets = new HashSet<string>(paths.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            try { targets.Add(Path.GetFullPath(p)); }
            catch (ArgumentException) { }
        }

        var entries = HandleSnapshot.Take();
        if (entries.Length == 0) return 0;

        var self = NativeMethods.GetCurrentProcess();
        var currentPid = Environment.ProcessId;
        var closed = 0;
        var processHandles = new Dictionary<int, IntPtr>();

        try
        {
            foreach (var entry in entries)
            {
                var pid = (int)entry.UniqueProcessId;
                if (pid is 0 or 4) continue;

                if (!processHandles.TryGetValue(pid, out var process))
                {
                    process = pid == currentPid
                        ? self
                        : NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, pid);
                    processHandles[pid] = process;
                }
                if (process == IntPtr.Zero) continue;

                // Inspect first with a non-destructive duplicate — closing the source is
                // irreversible, so it must only happen once the path is confirmed.
                if (!NativeMethods.DuplicateHandle(
                        process, entry.HandleValue, self, out var probe,
                        0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                {
                    continue;
                }

                bool matches;
                try
                {
                    matches = NativeMethods.GetFileType(probe) == NativeMethods.FILE_TYPE_DISK
                              && FinalPath(probe) is { } name
                              && targets.Contains(name);
                }
                finally
                {
                    NativeMethods.CloseHandle(probe);
                }

                if (!matches) continue;

                if (NativeMethods.DuplicateHandle(
                        process, entry.HandleValue, self, out var stolen,
                        0, false, NativeMethods.DUPLICATE_CLOSE_SOURCE | NativeMethods.DUPLICATE_SAME_ACCESS))
                {
                    NativeMethods.CloseHandle(stolen);
                    closed++;
                }
            }
        }
        finally
        {
            foreach (var (pid, handle) in processHandles)
            {
                if (handle != IntPtr.Zero && pid != currentPid) NativeMethods.CloseHandle(handle);
            }
        }

        return closed;
    }

    private static string? FinalPath(IntPtr handle)
    {
        var sb = new StringBuilder(1024);
        var len = NativeMethods.GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
        if (len == 0) return null;
        if (len > sb.Capacity)
        {
            sb = new StringBuilder((int)len + 1);
            if (NativeMethods.GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0) == 0)
                return null;
        }

        var path = sb.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path[4..];
        return path;
    }
}
```

- [ ] **Step 4: Extract the shared snapshot helper**

Both finders and the releaser need the handle-table snapshot. Create `src/TheCleaner.Windows/HandleSnapshot.cs`:

```csharp
using System.Runtime.InteropServices;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>Copies the system handle table into managed memory. Returns an empty array
/// rather than throwing when the query fails.</summary>
internal static class HandleSnapshot
{
    public static NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[] Take()
    {
        var size = 1 << 20;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NativeMethods.NtQuerySystemInformation(
                    NativeMethods.SystemExtendedHandleInformation, buffer, size, out _);

                if (status == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
                {
                    size *= 2;
                    continue;
                }
                if (status != 0) return [];

                var count = Marshal.ReadIntPtr(buffer).ToInt64();
                var entrySize = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
                var start = buffer + (IntPtr.Size * 2); // NumberOfHandles + Reserved

                var entries = new NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[count];
                for (long i = 0; i < count; i++)
                {
                    entries[i] = Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(
                        start + (int)(i * entrySize));
                }
                return entries;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return [];
    }
}
```

Then in `src/TheCleaner.Windows/NtHandleLockFinder.cs`, delete the private `Snapshot()` method and change its one call site from `Snapshot()` to `HandleSnapshot.Take()`.

- [ ] **Step 5: Write ProcessTerminator**

`src/TheCleaner.Windows/ProcessTerminator.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using TheCleaner.Core;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>Terminates locking processes, refusing the ones that would take the
/// session or the machine down with them.</summary>
public sealed class ProcessTerminator
{
    public static IReadOnlySet<string> ProtectedNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "smss", "wininit", "services", "lsass", "System", "winlogon"
        };

    public TerminationReport Terminate(IEnumerable<LockHolder> holders)
    {
        var terminated = new List<TerminatedProcess>();
        var skipped = new List<SkippedProcess>();
        var seen = new HashSet<int>();
        var accessDenied = false;
        var currentPid = Environment.ProcessId;

        foreach (var holder in holders)
        {
            if (!seen.Add(holder.Pid)) continue;

            if (holder.Pid == currentPid)
            {
                skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, "our own process"));
                continue;
            }

            if (holder.Pid is 0 or 4 || ProtectedNames.Contains(holder.ProcessName))
            {
                skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, "protected process"));
                continue;
            }

            var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_TERMINATE, false, holder.Pid);
            if (process == IntPtr.Zero)
            {
                var err = new Win32Exception();
                if (err.NativeErrorCode == NativeMethods.ERROR_ACCESS_DENIED) accessDenied = true;
                skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, err.Message));
                continue;
            }

            try
            {
                if (NativeMethods.TerminateProcess(process, 1))
                {
                    WaitForExit(holder.Pid);
                    terminated.Add(new TerminatedProcess(holder.Pid, holder.ProcessName));
                }
                else
                {
                    var err = new Win32Exception();
                    if (err.NativeErrorCode == NativeMethods.ERROR_ACCESS_DENIED) accessDenied = true;
                    skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, err.Message));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }
        }

        return new TerminationReport(terminated, skipped, accessDenied);
    }

    /// <summary>Termination is asynchronous; deleting before the process is gone
    /// would fail for no good reason.</summary>
    private static void WaitForExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit(5_000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Already gone.
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Windows.Tests`
Expected: PASS — 20 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: close remote handles and terminate lockers with a protected list"
```

---

### Task 9: Deletion

**Files:**
- Create: `src/TheCleaner.Windows/Deleter.cs`
- Test: `tests/TheCleaner.Windows.Tests/DeleterTests.cs`

**Interfaces:**
- Consumes: `PathResult`, `PathOutcome` (Task 1).
- Produces: `public sealed class Deleter` with `DeleteReport Delete(IReadOnlyList<string> paths)` and `public sealed record DeleteReport(IReadOnlyList<PathResult> Results, bool AccessDenied)`.
  Handles a mixed file/directory list in any order by sorting deepest-first itself.

- [ ] **Step 1: Write the failing test**

`tests/TheCleaner.Windows.Tests/DeleterTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Windows.Tests --filter DeleterTests`
Expected: FAIL — `Deleter` does not exist.

- [ ] **Step 3: Write the implementation**

`src/TheCleaner.Windows/Deleter.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner.Windows;

/// <param name="AccessDenied">True when a delete failed with access denied — the
/// signal to offer an elevated retry.</param>
public sealed record DeleteReport(IReadOnlyList<PathResult> Results, bool AccessDenied);

/// <summary>Removes targets deepest-first so a directory is only attempted once its
/// contents are gone. A path that is already absent counts as deleted.</summary>
public sealed class Deleter
{
    public DeleteReport Delete(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return new DeleteReport([], false);

        var ordered = paths.ToList();
        ordered.Sort(static (a, b) => Depth(b).CompareTo(Depth(a)));

        var results = new List<PathResult>(ordered.Count);
        var accessDenied = false;

        foreach (var path in ordered)
        {
            try
            {
                if (File.Exists(path))
                {
                    ClearReadOnly(path);
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    ClearReadOnly(path);
                    Directory.Delete(path, recursive: false);
                }
                // Absent already: nothing to do.

                results.Add(new PathResult(path, PathOutcome.Deleted));
            }
            catch (UnauthorizedAccessException e)
            {
                accessDenied = true;
                results.Add(new PathResult(path, PathOutcome.Failed, e.Message));
            }
            catch (IOException e)
            {
                results.Add(new PathResult(path, PathOutcome.Failed, e.Message));
            }
        }

        return new DeleteReport(results, accessDenied);
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Let the delete itself report the failure.
        }
    }

    private static int Depth(string path)
    {
        var n = 0;
        foreach (var c in path)
        {
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) n++;
        }
        return n;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Windows.Tests`
Expected: PASS — 27 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: delete targets bottom-up, clearing read-only attributes"
```

---

### Task 10: Elevation and the Windows pipeline

**Files:**
- Create: `src/TheCleaner.Windows/Elevation.cs`
- Create: `src/TheCleaner.Windows/WindowsLockKiller.cs`
- Test: `tests/TheCleaner.Windows.Tests/WindowsLockKillerTests.cs`
- Test: `tests/TheCleaner.Windows.Tests/ElevationTests.cs`

**Interfaces:**
- Consumes: `RestartManagerLockFinder` (Task 6), `NtHandleLockFinder` (Task 7), `HandleReleaser`, `ProcessTerminator` (Task 8), `Deleter`, `DeleteReport` (Task 9), `ILockKiller`, `KillResult`, `PathResult`, `PathOutcome`, `UnlockOptions`, `LockHolder` (Tasks 1, 4).
- Produces:
  - `public static class Elevation` with `static bool IsElevated { get; }`, `static bool Relaunch(IReadOnlyList<string> paths, bool deleteAfterUnlock, out string? error)`.
  - `public sealed class WindowsLockKiller : ILockKiller`.

- [ ] **Step 1: Write the failing tests**

`tests/TheCleaner.Windows.Tests/ElevationTests.cs`:

```csharp
namespace TheCleaner.Windows.Tests;

public class ElevationTests
{
    [WindowsOnlyFact]
    public void IsElevated_answers_without_throwing()
    {
        var ex = Record.Exception(() => _ = Elevation.IsElevated);
        Assert.Null(ex);
    }
}
```

`tests/TheCleaner.Windows.Tests/WindowsLockKillerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TheCleaner.Windows.Tests --filter "WindowsLockKillerTests|ElevationTests"`
Expected: FAIL — `WindowsLockKiller` and `Elevation` do not exist.

- [ ] **Step 3: Write Elevation**

`src/TheCleaner.Windows/Elevation.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace TheCleaner.Windows;

/// <summary>UAC re-launch. The app always starts unelevated; this runs only after a
/// real access-denied, and only with the user's consent at the UAC prompt.</summary>
public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Starts an elevated copy of this executable with the same targets and
    /// action. Returns false with a reason when the user declines the UAC prompt.</summary>
    public static bool Relaunch(IReadOnlyList<string> paths, bool deleteAfterUnlock, out string? error)
    {
        error = null;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            error = "Could not determine the executable path to relaunch.";
            return false;
        }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        psi.ArgumentList.Add("--elevated");
        psi.ArgumentList.Add("--action");
        psi.ArgumentList.Add(deleteAfterUnlock ? "unlock-delete" : "unlock");
        psi.ArgumentList.Add("--");
        foreach (var p in paths) psi.ArgumentList.Add(p);

        try
        {
            var started = Process.Start(psi);
            if (started is null)
            {
                error = "Elevated relaunch did not start.";
                return false;
            }
            return true;
        }
        catch (Win32Exception e)
        {
            // ERROR_CANCELLED (1223) is the user clicking No.
            error = e.NativeErrorCode == 1223
                ? "Elevation required / cancelled."
                : $"Elevation failed: {e.Message}";
            return false;
        }
    }
}
```

- [ ] **Step 4: Write WindowsLockKiller**

`src/TheCleaner.Windows/WindowsLockKiller.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner.Windows;

/// <summary>
/// The v1 pipeline: find lockers (Restart Manager, then handle scan as fallback),
/// close their handles, terminate whatever is left, re-scan once, then optionally delete.
/// </summary>
public sealed class WindowsLockKiller : ILockKiller
{
    private readonly RestartManagerLockFinder _restartManager = new();
    private readonly NtHandleLockFinder _handleScan = new();
    private readonly HandleReleaser _releaser = new();
    private readonly ProcessTerminator _terminator = new();
    private readonly Deleter _deleter = new();

    public Task<IReadOnlyList<LockHolder>> FindLockersAsync(
        IReadOnlyList<string> paths, CancellationToken ct) =>
        Task.Run<IReadOnlyList<LockHolder>>(() => FindCore(paths, ct), ct);

    public Task<KillResult> UnlockAsync(
        IReadOnlyList<string> paths, UnlockOptions options, CancellationToken ct) =>
        Task.Run(() => UnlockCore(paths, options, ct), ct);

    private IReadOnlyList<LockHolder> FindCore(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return [];

        ct.ThrowIfCancellationRequested();
        var display = paths[0];
        var holders = _restartManager.Find(paths, display);

        ct.ThrowIfCancellationRequested();
        // The handle scan is the fallback, but it also attributes exact paths, so run it
        // whenever RM came back empty — that is precisely the case RM could not explain.
        if (holders.Count == 0) holders = _handleScan.Find(paths);

        return holders;
    }

    private KillResult UnlockCore(
        IReadOnlyList<string> paths, UnlockOptions options, CancellationToken ct)
    {
        if (paths.Count == 0) return KillResult.Empty;

        ct.ThrowIfCancellationRequested();
        var holders = FindCore(paths, ct);

        ct.ThrowIfCancellationRequested();
        var handlesClosed = _releaser.CloseHandlesFor(paths);

        ct.ThrowIfCancellationRequested();
        // Re-scan: closing handles may have released some holders outright.
        var remaining = FindCore(paths, ct);

        var termination = options.TerminateLockers
            ? _terminator.Terminate(remaining)
            : new TerminationReport([], [], false);

        ct.ThrowIfCancellationRequested();
        var stillLocked = FindCore(paths, ct);
        var stillLockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in stillLocked)
        {
            if (h.Source == LockHolderSource.HandleScan) stillLockedPaths.Add(h.Path);
        }

        var elevationRequired = termination.AccessDenied;
        IReadOnlyList<PathResult> pathResults;

        if (options.DeleteAfterUnlock)
        {
            var report = _deleter.Delete(paths);
            pathResults = report.Results;
            elevationRequired |= report.AccessDenied;
        }
        else
        {
            var results = new List<PathResult>(paths.Count);
            foreach (var path in paths)
            {
                if (stillLockedPaths.Contains(path))
                    results.Add(new PathResult(path, PathOutcome.Failed, "Still locked after terminate."));
                else if (holders.Count == 0)
                    results.Add(new PathResult(path, PathOutcome.NoLockFound));
                else
                    results.Add(new PathResult(path, PathOutcome.Unlocked));
            }
            pathResults = results;
        }

        return new KillResult(
            Paths: pathResults,
            Terminated: termination.Terminated,
            Skipped: termination.Skipped,
            HandlesClosed: handlesClosed,
            ElevationRequired: elevationRequired && !Elevation.IsElevated);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TheCleaner.Windows.Tests`
Expected: PASS — 34 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: compose the Windows unlock pipeline with elevation detection"
```

---

### Task 11: Linux and macOS stubs

**Files:**
- Create: `src/TheCleaner.Linux/TheCleaner.Linux.csproj`
- Create: `src/TheCleaner.Linux/LinuxLockKiller.cs`
- Create: `src/TheCleaner.Mac/TheCleaner.Mac.csproj`
- Create: `src/TheCleaner.Mac/MacLockKiller.cs`
- Test: `tests/TheCleaner.Core.Tests/StubBackendTests.cs`

**Interfaces:**
- Consumes: `ILockKiller`, `KillResult`, `PathResult`, `PathOutcome`, `UnlockOptions`, `LockHolder` (Task 1).
- Produces: `public sealed class LinuxLockKiller : ILockKiller`, `public sealed class MacLockKiller : ILockKiller`. Both return failures rather than throwing, so the UI renders a message instead of crashing.

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/TheCleaner.Linux -n TheCleaner.Linux
dotnet new classlib -o src/TheCleaner.Mac -n TheCleaner.Mac
rm src/TheCleaner.Linux/Class1.cs src/TheCleaner.Mac/Class1.cs
dotnet sln add src/TheCleaner.Linux/TheCleaner.Linux.csproj src/TheCleaner.Mac/TheCleaner.Mac.csproj
dotnet add src/TheCleaner.Linux reference src/TheCleaner.Core
dotnet add src/TheCleaner.Mac reference src/TheCleaner.Core
dotnet add tests/TheCleaner.Core.Tests reference src/TheCleaner.Linux src/TheCleaner.Mac
```

Strip the template `<TargetFramework>`/`<Nullable>`/`<ImplicitUsings>` lines from both `.csproj` files.

- [ ] **Step 2: Write the failing test**

`tests/TheCleaner.Core.Tests/StubBackendTests.cs`:

```csharp
using TheCleaner.Core;
using TheCleaner.Linux;
using TheCleaner.Mac;

namespace TheCleaner.Core.Tests;

public class StubBackendTests
{
    public static TheoryData<ILockKiller, string> Stubs => new()
    {
        { new LinuxLockKiller(), "Linux" },
        { new MacLockKiller(), "macOS" }
    };

    [Theory]
    [MemberData(nameof(Stubs))]
    public async Task Find_returns_no_holders(ILockKiller killer, string _)
    {
        Assert.Empty(await killer.FindLockersAsync([@"/tmp/x"], CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Stubs))]
    public async Task Unlock_fails_every_path_with_a_not_implemented_message(
        ILockKiller killer, string platform)
    {
        var result = await killer.UnlockAsync(
            ["/tmp/a", "/tmp/b"], new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

        Assert.Equal(2, result.FailedCount);
        Assert.All(result.Paths, p => Assert.Contains("not implemented", p.Message!, StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Paths, p => Assert.Contains(platform, p.Message!, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Stubs))]
    public async Task Unlock_does_not_throw_for_an_empty_list(ILockKiller killer, string _)
    {
        var result = await killer.UnlockAsync([], new UnlockOptions(false), CancellationToken.None);
        Assert.Empty(result.Paths);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.Core.Tests --filter StubBackendTests`
Expected: FAIL — `LinuxLockKiller` and `MacLockKiller` do not exist.

- [ ] **Step 4: Write the stubs**

`src/TheCleaner.Linux/LinuxLockKiller.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner.Linux;

/// <summary>v2 placeholder. The real backend will find holders via /proc and fuser,
/// and release them by killing the holding processes.</summary>
public sealed class LinuxLockKiller : ILockKiller
{
    private const string Message =
        "Unlocking is not implemented in this release on Linux. " +
        "Linux support ships in a later release.";

    public Task<IReadOnlyList<LockHolder>> FindLockersAsync(
        IReadOnlyList<string> paths, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LockHolder>>([]);

    public Task<KillResult> UnlockAsync(
        IReadOnlyList<string> paths, UnlockOptions options, CancellationToken ct) =>
        Task.FromResult(new KillResult(
            Paths: [.. paths.Select(p => new PathResult(p, PathOutcome.Failed, Message))],
            Terminated: [],
            Skipped: [],
            HandlesClosed: 0,
            ElevationRequired: false));
}
```

`src/TheCleaner.Mac/MacLockKiller.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner.Mac;

/// <summary>v3 placeholder. The real backend will find holders via lsof and release
/// them by killing the holding processes.</summary>
public sealed class MacLockKiller : ILockKiller
{
    private const string Message =
        "Unlocking is not implemented in this release on macOS. " +
        "macOS support ships in a later release.";

    public Task<IReadOnlyList<LockHolder>> FindLockersAsync(
        IReadOnlyList<string> paths, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LockHolder>>([]);

    public Task<KillResult> UnlockAsync(
        IReadOnlyList<string> paths, UnlockOptions options, CancellationToken ct) =>
        Task.FromResult(new KillResult(
            Paths: [.. paths.Select(p => new PathResult(p, PathOutcome.Failed, Message))],
            Terminated: [],
            Skipped: [],
            HandlesClosed: 0,
            ElevationRequired: false));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS — the whole solution.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add Linux and macOS stub backends"
```

---

### Task 12: Avalonia shell, argv contract, and compile-time platform selection

**Files:**
- Create: `src/TheCleaner/TheCleaner.csproj`
- Create: `src/TheCleaner/app.manifest`
- Create: `src/TheCleaner/CommandLine.cs`
- Create: `src/TheCleaner/PlatformBackend.cs`
- Create: `src/TheCleaner/Program.cs`
- Create: `src/TheCleaner/App.axaml`
- Create: `src/TheCleaner/App.axaml.cs`
- Test: `tests/TheCleaner.Core.Tests/CommandLineTests.cs` — **no**: the app is not referenced by that project. Create `tests/TheCleaner.App.Tests/TheCleaner.App.Tests.csproj` and `tests/TheCleaner.App.Tests/CommandLineTests.cs`.

**Interfaces:**
- Consumes: `ILockKiller` (Task 1); `WindowsLockKiller` (Task 10); `LinuxLockKiller`, `MacLockKiller` (Task 11).
- Produces:
  - `public sealed record CommandLineArgs(IReadOnlyList<string> Paths, bool Elevated, bool? DeleteAfterUnlock)` with `static CommandLineArgs Parse(string[] args)` and `bool HasPaths`.
  - `internal static class PlatformBackend` with `static ILockKiller Create()`.
  - `public partial class App : Application`.

- [ ] **Step 1: Create the app project**

```bash
dotnet new avalonia.app -o src/TheCleaner -n TheCleaner
```

If the Avalonia templates are not installed, run `dotnet new install Avalonia.Templates::11.2.3` first. If template install is unavailable offline, create the project by hand: `dotnet new console -o src/TheCleaner -n TheCleaner` and write every file listed in this task (the csproj below is complete).

```bash
dotnet sln add src/TheCleaner/TheCleaner.csproj
dotnet new xunit -o tests/TheCleaner.App.Tests -n TheCleaner.App.Tests
rm tests/TheCleaner.App.Tests/UnitTest1.cs
dotnet sln add tests/TheCleaner.App.Tests/TheCleaner.App.Tests.csproj
dotnet add tests/TheCleaner.App.Tests reference src/TheCleaner/TheCleaner.csproj
```

Pin the test packages in `tests/TheCleaner.App.Tests/TheCleaner.App.Tests.csproj` to `Microsoft.NET.Test.Sdk` `17.12.0`, `xunit` `2.9.2`, `xunit.runner.visualstudio` `2.8.2`, and strip the template TFM/Nullable/ImplicitUsings lines.

- [ ] **Step 2: Write the csproj with platform selection**

Replace `src/TheCleaner/TheCleaner.csproj` entirely:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AssemblyName>thecleaner</AssemblyName>
    <RootNamespace>TheCleaner</RootNamespace>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <!-- A genuine single file: the Skia/ANGLE natives go inside the exe. -->
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>

  <!-- Compile-time backend selection: publish RID first, host OS when building
       without one (plain `dotnet build` / `dotnet test`). -->
  <PropertyGroup>
    <CleanerPlatform Condition="'$(RuntimeIdentifier)' != '' And $(RuntimeIdentifier.StartsWith('win'))">windows</CleanerPlatform>
    <CleanerPlatform Condition="'$(RuntimeIdentifier)' != '' And $(RuntimeIdentifier.StartsWith('linux'))">linux</CleanerPlatform>
    <CleanerPlatform Condition="'$(RuntimeIdentifier)' != '' And $(RuntimeIdentifier.StartsWith('osx'))">mac</CleanerPlatform>
    <CleanerPlatform Condition="'$(CleanerPlatform)' == '' And $([MSBuild]::IsOSPlatform('Windows'))">windows</CleanerPlatform>
    <CleanerPlatform Condition="'$(CleanerPlatform)' == '' And $([MSBuild]::IsOSPlatform('Linux'))">linux</CleanerPlatform>
    <CleanerPlatform Condition="'$(CleanerPlatform)' == '' And $([MSBuild]::IsOSPlatform('OSX'))">mac</CleanerPlatform>
  </PropertyGroup>

  <PropertyGroup Condition="'$(CleanerPlatform)' == 'windows'">
    <DefineConstants>$(DefineConstants);PLATFORM_WINDOWS</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition="'$(CleanerPlatform)' == 'linux'">
    <DefineConstants>$(DefineConstants);PLATFORM_LINUX</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition="'$(CleanerPlatform)' == 'mac'">
    <DefineConstants>$(DefineConstants);PLATFORM_MAC</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TheCleaner.Core\TheCleaner.Core.csproj" />
    <ProjectReference Include="..\TheCleaner.Windows\TheCleaner.Windows.csproj"
                      Condition="'$(CleanerPlatform)' == 'windows'" />
    <ProjectReference Include="..\TheCleaner.Linux\TheCleaner.Linux.csproj"
                      Condition="'$(CleanerPlatform)' == 'linux'" />
    <ProjectReference Include="..\TheCleaner.Mac\TheCleaner.Mac.csproj"
                      Condition="'$(CleanerPlatform)' == 'mac'" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.3" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.3" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.3" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="TheCleaner.App.Tests" />
  </ItemGroup>

</Project>
```

Delete any `ViewModels/`, `Assets/`, or `ViewLocator.cs` the template generated — this app has no MVVM layer.

- [ ] **Step 3: Write the manifest**

`src/TheCleaner/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="thecleaner" />

  <!-- Start unelevated. Elevation happens only via an explicit UAC relaunch. -->
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 4: Write the failing test**

`tests/TheCleaner.App.Tests/CommandLineTests.cs`:

```csharp
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
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/TheCleaner.App.Tests`
Expected: FAIL — `CommandLineArgs` does not exist.

- [ ] **Step 6: Write CommandLine, PlatformBackend, Program, and App**

`src/TheCleaner/CommandLine.cs`:

```csharp
namespace TheCleaner;

/// <param name="Paths">Targets from drag-drop-onto-exe or an elevated relaunch.</param>
/// <param name="Elevated">True when this instance was started by an elevated relaunch.</param>
/// <param name="DeleteAfterUnlock">The action the user already chose, when a relaunch
/// is carrying it forward; null when the user has not chosen yet.</param>
public sealed record CommandLineArgs(
    IReadOnlyList<string> Paths, bool Elevated, bool? DeleteAfterUnlock)
{
    public bool HasPaths => Paths.Count > 0;

    public static CommandLineArgs Parse(string[] args)
    {
        var paths = new List<string>();
        var elevated = false;
        bool? delete = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--")
            {
                // Everything after the separator is a literal path.
                for (var j = i + 1; j < args.Length; j++) paths.Add(args[j]);
                break;
            }

            if (arg == "--elevated")
            {
                elevated = true;
            }
            else if (arg == "--action")
            {
                if (i + 1 >= args.Length) break;
                delete = args[++i] switch
                {
                    "unlock-delete" => true,
                    "unlock" => false,
                    _ => null
                };
            }
            else
            {
                paths.Add(arg);
            }
        }

        return new CommandLineArgs(paths, elevated, delete);
    }
}
```

`src/TheCleaner/PlatformBackend.cs`:

```csharp
using TheCleaner.Core;

namespace TheCleaner;

/// <summary>Compile-time backend selection. The csproj references exactly one platform
/// project based on the publish RID, and defines the matching constant.</summary>
internal static class PlatformBackend
{
    public static ILockKiller Create() =>
#if PLATFORM_WINDOWS
        new TheCleaner.Windows.WindowsLockKiller();
#elif PLATFORM_LINUX
        new TheCleaner.Linux.LinuxLockKiller();
#elif PLATFORM_MAC
        new TheCleaner.Mac.MacLockKiller();
#else
        throw new PlatformNotSupportedException(
            "No platform backend was compiled into this build of thecleaner.");
#endif

    /// <summary>True when this build can ask the OS to re-run it elevated.</summary>
    public static bool SupportsElevation =>
#if PLATFORM_WINDOWS
        true;
#else
        false;
#endif

    public static bool IsElevated =>
#if PLATFORM_WINDOWS
        TheCleaner.Windows.Elevation.IsElevated;
#else
        false;
#endif

    public static bool Relaunch(IReadOnlyList<string> paths, bool deleteAfterUnlock, out string? error)
    {
#if PLATFORM_WINDOWS
        return TheCleaner.Windows.Elevation.Relaunch(paths, deleteAfterUnlock, out error);
#else
        error = "Elevation is not supported in this build.";
        return false;
#endif
    }
}
```

`src/TheCleaner/Program.cs`:

```csharp
using Avalonia;

namespace TheCleaner;

internal static class Program
{
    /// <summary>Targets and action handed to us by Explorer drag-drop or an elevated
    /// relaunch. Read by <see cref="App.OnFrameworkInitializationCompleted"/>.</summary>
    public static CommandLineArgs Startup { get; private set; } = CommandLineArgs.Parse([]);

    [STAThread]
    public static void Main(string[] args)
    {
        Startup = CommandLineArgs.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

`src/TheCleaner/App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TheCleaner.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

`src/TheCleaner/App.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TheCleaner;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Program.Startup);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

`MainWindow` arrives in Task 13; this task does not build on its own until then. Write a temporary placeholder so the build stays green and Task 13 replaces it — `src/TheCleaner/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="TheCleaner.MainWindow"
        Title="thecleaner" Width="640" Height="420">
  <TextBlock Text="thecleaner" HorizontalAlignment="Center" VerticalAlignment="Center" />
</Window>
```

`src/TheCleaner/MainWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TheCleaner;

public partial class MainWindow : Window
{
    public MainWindow(CommandLineArgs startup)
    {
        AvaloniaXamlLoader.Load(this);
        _ = startup;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build && dotnet test`
Expected: PASS — 8 new CommandLine tests plus everything before.

- [ ] **Step 8: Verify the platform selection actually switches**

Run: `dotnet build src/TheCleaner -r linux-x64 --self-contained false`
Expected: build succeeds. Confirm it linked the Linux backend, not the Windows one:

Run: `ls src/TheCleaner/bin/Debug/net8.0/linux-x64/`
Expected: `TheCleaner.Linux.dll` present, `TheCleaner.Windows.dll` absent.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add Avalonia shell, argv contract, and compile-time backend selection"
```

---

### Task 13: Confirm UI, results, publish, and docs

**Files:**
- Modify: `src/TheCleaner/MainWindow.axaml` (replace the placeholder)
- Modify: `src/TheCleaner/MainWindow.axaml.cs` (replace the placeholder)
- Create: `README.md`
- Create: `docs/superpowers/MANUAL-TESTS.md`

**Interfaces:**
- Consumes: `CleanerService`, `ScanResult` (Task 4); `RunLogger` (Task 5); `PathSafety`, `PathExpander` (Tasks 2–3); `KillResult`, `PathResult`, `PathOutcome`, `UnlockOptions`, `LockHolder` (Task 1); `CommandLineArgs`, `PlatformBackend` (Task 12).
- Produces: the finished application. No further task depends on these names.

- [ ] **Step 1: Write the window XAML**

Replace `src/TheCleaner/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="TheCleaner.MainWindow"
        Title="thecleaner"
        Width="720" Height="520"
        MinWidth="520" MinHeight="400"
        WindowStartupLocation="CenterScreen">

  <Grid RowDefinitions="Auto,*,Auto" Margin="20">

    <!-- Header -->
    <StackPanel Grid.Row="0" Spacing="4" Margin="0,0,0,16">
      <TextBlock Text="thecleaner" FontSize="22" FontWeight="SemiBold" />
      <TextBlock x:Name="SubtitleText"
                 Text="Drop a file or folder to see what is holding it."
                 Opacity="0.7" TextWrapping="Wrap" />
    </StackPanel>

    <!-- Idle: drop zone -->
    <Border x:Name="IdlePanel" Grid.Row="1"
            BorderThickness="2" BorderBrush="#55FFFFFF" CornerRadius="8"
            Background="#11FFFFFF">
      <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <TextBlock Text="Drop files or folders here"
                   FontSize="16" HorizontalAlignment="Center" Opacity="0.8" />
        <TextBlock Text="or drag them onto thecleaner.exe"
                   HorizontalAlignment="Center" Opacity="0.5" />
      </StackPanel>
    </Border>

    <!-- Busy -->
    <StackPanel x:Name="BusyPanel" Grid.Row="1" IsVisible="False"
                HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
      <ProgressBar IsIndeterminate="True" Width="240" />
      <TextBlock x:Name="BusyText" Text="Scanning…" HorizontalAlignment="Center" Opacity="0.8" />
    </StackPanel>

    <!-- Confirm -->
    <Grid x:Name="ConfirmPanel" Grid.Row="1" IsVisible="False"
          RowDefinitions="Auto,Auto,Auto,*">
      <TextBlock Grid.Row="0" x:Name="TargetText"
                 FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,8" />
      <TextBlock Grid.Row="1" x:Name="CountText" Opacity="0.7" Margin="0,0,0,8" />
      <Border Grid.Row="2" Background="#33FF6B4A" CornerRadius="6"
              Padding="12,8" Margin="0,0,0,12">
        <TextBlock x:Name="WarningText" TextWrapping="Wrap"
                   Text="Open handles will be closed and every locking process listed below will be terminated. Unsaved work in those processes will be lost." />
      </Border>
      <Border Grid.Row="3" BorderThickness="1" BorderBrush="#33FFFFFF" CornerRadius="6">
        <ListBox x:Name="HolderList" Background="Transparent" BorderThickness="0">
          <ListBox.ItemTemplate>
            <DataTemplate>
              <StackPanel Margin="4,2">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                <TextBlock Text="{Binding Detail}" Opacity="0.6" FontSize="12"
                           TextTrimming="CharacterEllipsis" />
              </StackPanel>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </Border>
    </Grid>

    <!-- Results -->
    <Grid x:Name="ResultsPanel" Grid.Row="1" IsVisible="False" RowDefinitions="Auto,*,Auto">
      <TextBlock Grid.Row="0" x:Name="SummaryText"
                 FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,12" />
      <Border Grid.Row="1" BorderThickness="1" BorderBrush="#33FFFFFF" CornerRadius="6">
        <ListBox x:Name="ResultList" Background="Transparent" BorderThickness="0">
          <ListBox.ItemTemplate>
            <DataTemplate>
              <StackPanel Margin="4,2">
                <TextBlock Text="{Binding Title}" />
                <TextBlock Text="{Binding Detail}" Opacity="0.6" FontSize="12"
                           TextWrapping="Wrap" />
              </StackPanel>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </Border>
      <TextBlock Grid.Row="2" x:Name="LogPathText" Opacity="0.5" FontSize="12"
                 Margin="0,8,0,0" TextTrimming="CharacterEllipsis" />
    </Grid>

    <!-- Buttons -->
    <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right"
                Spacing="8" Margin="0,16,0,0">
      <Button x:Name="CancelButton" Content="Cancel" IsVisible="False" Click="OnCancel" />
      <Button x:Name="UnlockButton" Content="Unlock" IsVisible="False" Click="OnUnlock" />
      <Button x:Name="UnlockDeleteButton" Content="Unlock &amp; Delete" IsVisible="False"
              Classes="accent" IsDefault="True" Click="OnUnlockAndDelete" />
      <Button x:Name="DoneButton" Content="Done" IsVisible="False"
              Classes="accent" Click="OnDone" />
    </StackPanel>

  </Grid>
</Window>
```

- [ ] **Step 2: Write the window code-behind**

Replace `src/TheCleaner/MainWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TheCleaner.Core;

namespace TheCleaner;

/// <summary>Row shown in the holder and result lists.</summary>
public sealed record ListRow(string Title, string Detail);

public partial class MainWindow : Window
{
    private readonly CleanerService _service;
    private readonly RunLogger _logger = new(RunLogger.DefaultLogPath);
    private readonly CommandLineArgs _startup;

    private ScanResult _scan = ScanResult.Empty;

    public MainWindow() : this(CommandLineArgs.Parse([])) { }

    public MainWindow(CommandLineArgs startup)
    {
        AvaloniaXamlLoader.Load(this);
        _startup = startup;
        _service = new CleanerService(PlatformBackend.Create());

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        Opened += OnOpened;
    }

    // ---- startup ----------------------------------------------------------

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (_startup.HasPaths) await ScanAndShowAsync(_startup.Paths);
    }

    // ---- drag and drop ----------------------------------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!ConfirmPanel.IsVisible && !BusyPanel.IsVisible)
        {
            var paths = ExtractPaths(e);
            if (paths.Count > 0) await ScanAndShowAsync(paths);
        }
    }

    private static List<string> ExtractPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        var files = e.Data.GetFiles();
        if (files is null) return paths;

        foreach (var item in files)
        {
            var local = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local)) paths.Add(local);
        }
        return paths;
    }

    // ---- scan -------------------------------------------------------------

    private async Task ScanAndShowAsync(IReadOnlyList<string> paths)
    {
        ShowBusy("Scanning for locking processes…");

        ScanResult scan;
        try
        {
            scan = await _service.ScanAsync(paths, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.WriteError(ex.Message);
            ShowFailure($"Scan failed: {ex.Message}");
            return;
        }

        _scan = scan;

        if (!scan.HasWork)
        {
            var reasons = scan.Refused.Select(r => r.Message ?? "Refused.")
                .Concat(scan.Errors)
                .ToList();
            _logger.WriteError(string.Join(" ", reasons));
            ShowFailure(reasons.Count > 0
                ? string.Join(Environment.NewLine, reasons)
                : "Nothing to do.");
            return;
        }

        ShowConfirm(scan);
    }

    // ---- run --------------------------------------------------------------

    private void OnUnlock(object? sender, RoutedEventArgs e) => _ = RunAsync(deleteAfter: false);

    private void OnUnlockAndDelete(object? sender, RoutedEventArgs e) => _ = RunAsync(deleteAfter: true);

    private void OnCancel(object? sender, RoutedEventArgs e) => ShowIdle();

    private void OnDone(object? sender, RoutedEventArgs e) => ShowIdle();

    private async Task RunAsync(bool deleteAfter)
    {
        ShowBusy(deleteAfter ? "Unlocking and deleting…" : "Unlocking…");

        var options = new UnlockOptions(DeleteAfterUnlock: deleteAfter);
        KillResult result;
        try
        {
            result = await _service.RunAsync(_scan, options, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.WriteError(ex.Message);
            ShowFailure($"Unlock failed: {ex.Message}");
            return;
        }

        _logger.Write(_scan, result, options);

        if (result.ElevationRequired && PlatformBackend.SupportsElevation && !PlatformBackend.IsElevated)
        {
            if (PlatformBackend.Relaunch(_scan.Roots, deleteAfter, out var error))
            {
                // The elevated instance takes over from here.
                Close();
                return;
            }

            _logger.WriteError(error ?? "Elevation required / cancelled.");
            ShowResults(result, extraNote: error);
            return;
        }

        ShowResults(result, extraNote: null);
    }

    // ---- view states ------------------------------------------------------

    private void ShowIdle()
    {
        SubtitleText.Text = "Drop a file or folder to see what is holding it.";
        SetPanels(idle: true, busy: false, confirm: false, results: false);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: false);
    }

    private void ShowBusy(string message)
    {
        BusyText.Text = message;
        SetPanels(idle: false, busy: true, confirm: false, results: false);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: false);
    }

    private void ShowConfirm(ScanResult scan)
    {
        SubtitleText.Text = "Review what will happen, then choose an action.";
        TargetText.Text = DescribeTargets(scan.Roots);
        CountText.Text = $"{scan.FileCount} file(s), {scan.DirectoryCount} folder(s) will be affected.";

        WarningText.Text = scan.Holders.Count == 0
            ? "Nothing is holding these targets right now. Unlock & Delete will remove them."
            : "Open handles will be closed and every locking process listed below will be terminated. "
              + "Unsaved work in those processes will be lost.";

        var rows = new List<ListRow>();
        foreach (var h in scan.Holders)
        {
            rows.Add(new ListRow(
                $"{h.ProcessName}  (PID {h.Pid})",
                string.IsNullOrEmpty(h.Path) ? h.Source.ToString() : h.Path));
        }
        if (rows.Count == 0) rows.Add(new ListRow("No locking processes found.", string.Empty));

        foreach (var refused in scan.Refused)
            rows.Add(new ListRow($"Refused: {refused.Path}", refused.Message ?? string.Empty));
        foreach (var error in scan.Errors)
            rows.Add(new ListRow("Scan warning", error));

        HolderList.ItemsSource = rows;

        SetPanels(idle: false, busy: false, confirm: true, results: false);
        SetButtons(cancel: true, unlock: true, unlockDelete: true, done: false);
        UnlockDeleteButton.Focus();
    }

    private void ShowResults(KillResult result, string? extraNote)
    {
        SubtitleText.Text = "Run complete.";

        var summary = $"{result.DeletedCount} deleted, {result.UnlockedCount} unlocked, "
                      + $"{result.FailedCount} failed, {result.RefusedCount} refused. "
                      + $"{result.HandlesClosed} handle(s) closed, "
                      + $"{result.Terminated.Count} process(es) terminated.";
        SummaryText.Text = extraNote is null ? summary : summary + Environment.NewLine + extraNote;

        var rows = new List<ListRow>();
        foreach (var t in result.Terminated)
            rows.Add(new ListRow($"Terminated {t.ProcessName} (PID {t.Pid})", string.Empty));
        foreach (var s in result.Skipped)
            rows.Add(new ListRow($"Skipped {s.ProcessName} (PID {s.Pid})", s.Reason));
        foreach (var p in result.Paths)
            rows.Add(new ListRow($"{p.Outcome}: {p.Path}", p.Message ?? string.Empty));

        ResultList.ItemsSource = rows;
        LogPathText.Text = $"Log: {RunLogger.DefaultLogPath}";

        SetPanels(idle: false, busy: false, confirm: false, results: true);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: true);
        DoneButton.Focus();
    }

    private void ShowFailure(string message)
    {
        SubtitleText.Text = "Nothing was changed.";
        SummaryText.Text = message;
        ResultList.ItemsSource = Array.Empty<ListRow>();
        LogPathText.Text = $"Log: {RunLogger.DefaultLogPath}";

        SetPanels(idle: false, busy: false, confirm: false, results: true);
        SetButtons(cancel: false, unlock: false, unlockDelete: false, done: true);
        DoneButton.Focus();
    }

    private void SetPanels(bool idle, bool busy, bool confirm, bool results)
    {
        IdlePanel.IsVisible = idle;
        BusyPanel.IsVisible = busy;
        ConfirmPanel.IsVisible = confirm;
        ResultsPanel.IsVisible = results;
    }

    private void SetButtons(bool cancel, bool unlock, bool unlockDelete, bool done)
    {
        CancelButton.IsVisible = cancel;
        UnlockButton.IsVisible = unlock;
        UnlockDeleteButton.IsVisible = unlockDelete;
        DoneButton.IsVisible = done;
    }

    /// <summary>One line for one target, a count for several; long paths trim in the middle.</summary>
    private static string DescribeTargets(IReadOnlyList<string> roots) => roots.Count switch
    {
        0 => "No targets.",
        1 => Middle(roots[0], 80),
        _ => $"{roots.Count} targets — {Middle(roots[0], 60)} and {roots.Count - 1} more"
    };

    private static string Middle(string text, int max)
    {
        if (text.Length <= max) return text;
        var keep = (max - 3) / 2;
        return text[..keep] + "..." + text[^keep..];
    }
}
```

- [ ] **Step 3: Build and run the whole suite**

Run: `dotnet build && dotnet test`
Expected: build clean, all tests PASS.

- [ ] **Step 4: Launch the app and verify the idle window**

Run: `dotnet run --project src/TheCleaner`
Expected: a window titled "thecleaner" showing the drop zone. It must **not** exit on its own. Close it.

- [ ] **Step 5: Verify the release publish**

Run:

```bash
dotnet publish src/TheCleaner -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

Expected: `artifacts/win-x64/thecleaner.exe` exists. Confirm:

Run: `ls artifacts/win-x64`
Expected: `thecleaner.exe` (and a `.pdb`), with no loose `.dll` files beside it.

- [ ] **Step 6: End-to-end smoke test against the published exe**

```bash
mkdir -p /c/temp/cleaner-smoke
echo hello > /c/temp/cleaner-smoke/locked.txt
```

Open `C:\temp\cleaner-smoke\locked.txt` in Notepad and leave it open, then run:

```bash
./artifacts/win-x64/thecleaner.exe "C:\temp\cleaner-smoke\locked.txt"
```

Expected: the confirm view lists Notepad with its PID. Click **Unlock & Delete**. Expected: Notepad closes, the results view reports `1 deleted`, and:

Run: `ls /c/temp/cleaner-smoke/`
Expected: `locked.txt` is gone.

Run: `cat "$LOCALAPPDATA/thecleaner/last-run.log"`
Expected: the log names the target, Notepad's PID, and `[Deleted]`.

- [ ] **Step 7: Verify the safety rail on the published exe**

Run: `./artifacts/win-x64/thecleaner.exe "C:\"`
Expected: the results view says nothing was changed and gives the volume-root refusal. `C:\` is untouched.

- [ ] **Step 8: Write the README**

`README.md`:

```markdown
# thecleaner

Drag a locked file or folder onto `thecleaner.exe`. It shows which processes are
holding it, and on confirm closes their handles, terminates them, and optionally
deletes the target — recursively for folders.

## Build

```
dotnet build
dotnet test
```

## Run

```
dotnet run --project src/TheCleaner
```

Or pass targets directly:

```
dotnet run --project src/TheCleaner -- "C:\path\to\locked.txt"
```

## Publish (Windows v1)

```
dotnet publish src/TheCleaner -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o artifacts/win-x64
```

Produces a single `artifacts/win-x64/thecleaner.exe`.

## What it does

1. Refuses volume roots, your user profile root, and the Windows directory outright.
2. Expands folders depth-first and counts what will be affected.
3. Finds lockers via the Restart Manager, falling back to a system handle-table scan.
4. On confirm: closes the remote handles, terminates the remaining lockers, re-scans,
   then deletes if you chose **Unlock & Delete**.
5. Writes `%LOCALAPPDATA%\thecleaner\last-run.log`.

Critical system processes (`csrss`, `smss`, `wininit`, `services`, `lsass`, `System`,
`winlogon`) are never terminated — they are reported as skipped.

The app starts unelevated and relaunches itself through UAC only after a real
access-denied.

## Platforms

Windows is the v1 backend. Linux and macOS compile as stubs that report
"not implemented in this release"; their real backends ship in later releases.
The backend is chosen at compile time from the publish RID.
```

- [ ] **Step 9: Write the manual test checklist**

`docs/superpowers/MANUAL-TESTS.md`:

```markdown
# thecleaner — manual test checklist (v1, Windows)

Run against a published `artifacts/win-x64/thecleaner.exe`.

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| 1 | Locked file, delete | Open a temp .txt in Notepad. Drag it onto thecleaner.exe. Click **Unlock & Delete**. | Notepad is listed with its PID; Notepad closes; file is gone; summary says 1 deleted. |
| 2 | Locked file, keep | Same, but click **Unlock**. | Notepad closes; file still exists; summary says 1 unlocked. |
| 3 | Cancel | Same, but click **Cancel**. | Nothing is terminated, nothing deleted, window returns to the drop zone. |
| 4 | Locked folder, recursive | Create a folder with nested files; open one in Notepad. Drag the folder in. | File count shown before confirm; after Unlock & Delete the whole tree is gone. |
| 5 | Nothing locked | Drag an unlocked file in. | Confirm says nothing is holding it; Unlock & Delete removes it. |
| 6 | In-app drop | Launch with no arguments; drag a file into the window. | Window stays open on launch; the drop starts a scan. |
| 7 | Multi-path drop | Select two files, drag both in. | One confirm covering both. |
| 8 | Volume root refused | Run `thecleaner.exe "C:\"`. | Hard refusal, nothing changed, `C:\` untouched. |
| 9 | Profile root refused | Run `thecleaner.exe "%USERPROFILE%"`. | Hard refusal, nothing changed. |
| 10 | Windows dir refused | Run `thecleaner.exe "C:\Windows"`. | Hard refusal, nothing changed. |
| 11 | Elevation | Lock a file with an elevated process (elevated cmd holding it open). Unlock & Delete. | UAC prompt appears; accepting completes the job. |
| 12 | Elevation cancelled | Same, but click **No** at UAC. | Message says elevation required / cancelled; no crash. |
| 13 | Protected process | Not directly testable safely — verify by inspection that `ProcessTerminator.ProtectedNames` covers csrss, smss, wininit, services, lsass, System, winlogon, and that unit tests cover the skip path. | Skipped, never terminated. |
| 14 | Log | After any run, open `%LOCALAPPDATA%\thecleaner\last-run.log`. | Targets, holders, terminations, per-path outcomes, summary. |
```

- [ ] **Step 10: Final full verification**

Run: `dotnet build && dotnet test`
Expected: build clean, every test PASS. Record the actual counts before claiming completion.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: add confirm and results UI, publish config, and docs"
```

---

## Spec coverage check

| Spec requirement | Task |
|---|---|
| Drag-drop or argv → confirm UI | 12, 13 |
| Recursive folder support | 3, 9, 13 |
| Elevate only when needed | 10, 13 |
| Always terminate lockers after handle close | 8, 10 |
| Cross-platform architecture, per-OS binaries | 11, 12 |
| Windows-first, Linux/mac stubbed | 11 |
| `ILockKiller` / `LockHolder` / `UnlockOptions` shapes | 1 |
| Restart Manager primary finder | 6 |
| NtQuerySystemInformation fallback | 7 |
| Remote handle close | 8 |
| Never terminate critical processes | 8 |
| Re-scan once; leftover locks → per-path failure | 10 |
| Files then directories bottom-up; clear read-only | 9 |
| UAC relaunch with same paths + action | 10, 12 |
| Truncate long paths in the middle | 13 |
| Warning that processes will be terminated | 13 |
| Buttons Cancel / Unlock / Unlock & Delete (default) | 13 |
| No paths on launch → idle drop zone, do not exit | 13 |
| Multi-path drop → one confirm | 13 |
| Refuse volume roots / profile root / Windows dir | 2 |
| Folder file count before confirm | 4, 13 |
| `%LOCALAPPDATA%\thecleaner\last-run.log` | 5 |
| Per-path results; one failure does not abort | 4, 9 |
| Elevation cancel → clear message | 10, 13 |
| Unexpected exceptions → log + user-visible failure | 4, 13 |
| Unit tests: safety, expansion, aggregation | 2, 3, 4 |
| Manual test checklist | 13 |
| Single-file self-contained win-x64 publish | 13 |
