using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class DeepUninstallServiceTests
{
    private static DeepUninstallService Make(
        FakeLockKiller? killer = null,
        FakeDeepUninstallBackend? backend = null,
        PathSafety? safety = null) =>
        new(
            killer ?? new FakeLockKiller(),
            backend ?? new FakeDeepUninstallBackend(),
            safety ?? new PathSafety([@"C:\Users\tester", @"C:\Windows"]));

    private static InstallFootprint MakeFootprint(string installRoot = @"C:\Program Files\MyApp") =>
        new(
            InstallRoot: installRoot,
            AppName: "MyApp",
            ExePaths: [$@"{installRoot}\myapp.exe"],
            DataPaths: [],
            RegistryKeys: [],
            Services: [],
            ScheduledTasks: [],
            StartMenuShortcuts: [],
            DesktopShortcuts: []);

    // ---- service delegation ---------------------------------------------

    [Fact]
    public async Task Passes_services_to_backend_when_IncludeServices_true()
    {
        var backend = new FakeDeepUninstallBackend();
        var footprint = MakeFootprint() with { Services = ["mysvc"] };

        await Make(backend: backend).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeServices: true), CancellationToken.None);

        Assert.Contains("mysvc", backend.ServicesDeleted);
    }

    [Fact]
    public async Task Skips_services_when_IncludeServices_false()
    {
        var backend = new FakeDeepUninstallBackend();
        var footprint = MakeFootprint() with { Services = ["mysvc"] };

        await Make(backend: backend).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeServices: false), CancellationToken.None);

        Assert.Empty(backend.ServicesDeleted);
    }

    // ---- task delegation ------------------------------------------------

    [Fact]
    public async Task Passes_tasks_to_backend_when_IncludeTasks_true()
    {
        var backend = new FakeDeepUninstallBackend();
        var footprint = MakeFootprint() with { ScheduledTasks = [@"\MyApp\Update"] };

        await Make(backend: backend).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeTasks: true), CancellationToken.None);

        Assert.Contains(@"\MyApp\Update", backend.TasksDeleted);
    }

    [Fact]
    public async Task Skips_tasks_when_IncludeTasks_false()
    {
        var backend = new FakeDeepUninstallBackend();
        var footprint = MakeFootprint() with { ScheduledTasks = [@"\MyApp\Update"] };

        await Make(backend: backend).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeTasks: false), CancellationToken.None);

        Assert.Empty(backend.TasksDeleted);
    }

    // ---- registry delegation --------------------------------------------

    [Fact]
    public async Task Passes_registry_keys_to_backend_when_IncludeRegistry_true()
    {
        var backend = new FakeDeepUninstallBackend();
        var footprint = MakeFootprint() with
        {
            RegistryKeys = [@"HKEY_LOCAL_MACHINE\SOFTWARE\MyApp"]
        };

        await Make(backend: backend).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeRegistry: true), CancellationToken.None);

        Assert.Contains(@"HKEY_LOCAL_MACHINE\SOFTWARE\MyApp", backend.RegistryKeysDeleted);
    }

    [Fact]
    public async Task Skips_registry_when_IncludeRegistry_false()
    {
        var backend = new FakeDeepUninstallBackend();
        var footprint = MakeFootprint() with
        {
            RegistryKeys = [@"HKEY_LOCAL_MACHINE\SOFTWARE\MyApp"]
        };

        await Make(backend: backend).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeRegistry: false), CancellationToken.None);

        Assert.Empty(backend.RegistryKeysDeleted);
    }

    // ---- file deletion --------------------------------------------------

    [Fact]
    public async Task Passes_install_root_to_locker_for_deletion()
    {
        using var temp = new TempDir();
        temp.File("myapp.exe");

        var killer = new FakeLockKiller();
        var footprint = MakeFootprint(temp.Path);

        await Make(killer: killer).UninstallAsync(
            footprint, InstallCleanOptions.All, CancellationToken.None);

        Assert.Contains(temp.Path, killer.UnlockCalledWith);
    }

    [Fact]
    public async Task Passes_data_paths_to_locker_when_IncludeUserData_true()
    {
        using var temp = new TempDir();
        using var dataDir = new TempDir();

        var killer = new FakeLockKiller();
        var footprint = MakeFootprint(temp.Path) with { DataPaths = [dataDir.Path] };

        await Make(killer: killer).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeUserData: true), CancellationToken.None);

        Assert.Contains(dataDir.Path, killer.UnlockCalledWith);
    }

    [Fact]
    public async Task Skips_data_paths_when_IncludeUserData_false()
    {
        using var temp = new TempDir();
        using var dataDir = new TempDir();

        var killer = new FakeLockKiller();
        var footprint = MakeFootprint(temp.Path) with { DataPaths = [dataDir.Path] };

        await Make(killer: killer).UninstallAsync(
            footprint, new InstallCleanOptions(IncludeUserData: false), CancellationToken.None);

        Assert.DoesNotContain(dataDir.Path, killer.UnlockCalledWith);
    }

    // ---- safety refusals ------------------------------------------------

    [Fact]
    public async Task Refused_paths_produce_errors_and_are_not_forwarded_to_locker()
    {
        var killer = new FakeLockKiller();
        // Use the real user-profile root — always a protected path on every OS.
        var protectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var footprint = new InstallFootprint(
            InstallRoot: protectedRoot,
            AppName: "test",
            ExePaths: [],
            DataPaths: [],
            RegistryKeys: [],
            Services: [],
            ScheduledTasks: [],
            StartMenuShortcuts: [],
            DesktopShortcuts: []);

        var result = await Make(killer: killer, safety: new PathSafety()).UninstallAsync(
            footprint, InstallCleanOptions.All, CancellationToken.None);

        Assert.Empty(killer.UnlockCalledWith);
        Assert.NotEmpty(result.Errors);
    }

    // ---- backend exceptions ---------------------------------------------

    [Fact]
    public async Task Backend_exception_for_services_is_captured_as_failures()
    {
        var backend = new FakeDeepUninstallBackend
        {
            ServicesResult = BackendActionResult.AllFailed(["svc1"], "access denied")
        };
        var footprint = MakeFootprint() with { Services = ["svc1"] };

        var result = await Make(backend: backend).UninstallAsync(
            footprint, InstallCleanOptions.All, CancellationToken.None);

        Assert.Equal(1, result.ServicesFailed);
        Assert.NotEmpty(result.Errors);
    }

    // ---- result aggregation ---------------------------------------------

    [Fact]
    public async Task AnyFailures_is_false_when_everything_succeeds()
    {
        using var temp = new TempDir();
        temp.File("myapp.exe");

        var backend = new FakeDeepUninstallBackend();
        var footprint = MakeFootprint(temp.Path) with
        {
            Services = ["mysvc"],
            ScheduledTasks = [@"\MyApp\Update"],
            RegistryKeys = [@"HKEY_LOCAL_MACHINE\SOFTWARE\MyApp"],
        };

        var result = await Make(backend: backend).UninstallAsync(
            footprint, InstallCleanOptions.All, CancellationToken.None);

        Assert.False(result.AnyFailures);
    }
}
