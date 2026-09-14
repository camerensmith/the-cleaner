namespace TheCleaner.Core;

/// <summary>Platform-specific operations needed by <see cref="DeepUninstallService"/>
/// beyond what <see cref="ILockKiller"/> already provides.</summary>
public interface IDeepUninstallBackend
{
    /// <summary>Stops and deletes each named service.</summary>
    Task<BackendActionResult> DeleteServicesAsync(
        IReadOnlyList<string> serviceNames, CancellationToken ct);

    /// <summary>Disables and deletes each named scheduled task.</summary>
    Task<BackendActionResult> DeleteScheduledTasksAsync(
        IReadOnlyList<string> taskNames, CancellationToken ct);

    /// <summary>Recursively deletes each registry key path
    /// (format: "HKEY_LOCAL_MACHINE\SOFTWARE\..."). Absent keys are silently skipped.</summary>
    Task<BackendActionResult> DeleteRegistryKeysAsync(
        IReadOnlyList<string> keyPaths, CancellationToken ct);
}

/// <summary>Outcome counts for one category of deep-uninstall actions.</summary>
public sealed record BackendActionResult(
    int Deleted,
    int Failed,
    IReadOnlyList<string> Errors)
{
    public static BackendActionResult Empty { get; } = new(0, 0, []);

    public static BackendActionResult AllFailed(IReadOnlyList<string> names, string reason)
    {
        var errors = names.Select(n => $"{n}: {reason}").ToList();
        return new BackendActionResult(0, names.Count, errors);
    }
}
