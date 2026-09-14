using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public sealed class FakeDeepUninstallBackend : IDeepUninstallBackend
{
    public List<string> ServicesDeleted { get; } = [];
    public List<string> TasksDeleted { get; } = [];
    public List<string> RegistryKeysDeleted { get; } = [];

    public BackendActionResult ServicesResult { get; set; } = BackendActionResult.Empty;
    public BackendActionResult TasksResult { get; set; } = BackendActionResult.Empty;
    public BackendActionResult RegistryResult { get; set; } = BackendActionResult.Empty;

    public Task<BackendActionResult> DeleteServicesAsync(
        IReadOnlyList<string> serviceNames, CancellationToken ct)
    {
        ServicesDeleted.AddRange(serviceNames);
        return Task.FromResult(ServicesResult.Deleted == 0 && ServicesResult.Failed == 0
            ? new BackendActionResult(serviceNames.Count, 0, [])
            : ServicesResult);
    }

    public Task<BackendActionResult> DeleteScheduledTasksAsync(
        IReadOnlyList<string> taskNames, CancellationToken ct)
    {
        TasksDeleted.AddRange(taskNames);
        return Task.FromResult(TasksResult.Deleted == 0 && TasksResult.Failed == 0
            ? new BackendActionResult(taskNames.Count, 0, [])
            : TasksResult);
    }

    public Task<BackendActionResult> DeleteRegistryKeysAsync(
        IReadOnlyList<string> keyPaths, CancellationToken ct)
    {
        RegistryKeysDeleted.AddRange(keyPaths);
        return Task.FromResult(RegistryResult.Deleted == 0 && RegistryResult.Failed == 0
            ? new BackendActionResult(keyPaths.Count, 0, [])
            : RegistryResult);
    }
}
