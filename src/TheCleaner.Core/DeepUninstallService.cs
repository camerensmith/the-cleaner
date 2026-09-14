namespace TheCleaner.Core;

/// <summary>
/// Orchestrates a complete program removal: unlocks and deletes the install tree and
/// data paths via <see cref="ILockKiller"/>, then removes services, scheduled tasks, and
/// registry keys via <see cref="IDeepUninstallBackend"/>.
/// </summary>
public sealed class DeepUninstallService
{
    private readonly ILockKiller _locker;
    private readonly IDeepUninstallBackend _backend;
    private readonly PathSafety _safety;

    public DeepUninstallService(
        ILockKiller locker,
        IDeepUninstallBackend backend,
        PathSafety? safety = null)
    {
        _locker = locker;
        _backend = backend;
        _safety = safety ?? new PathSafety();
    }

    public async Task<DeepUninstallResult> UninstallAsync(
        InstallFootprint footprint,
        InstallCleanOptions options,
        CancellationToken ct)
    {
        var errors = new List<string>();

        // -- 1. Stop and delete services --------------------------------------
        var svcResult = BackendActionResult.Empty;
        if (options.IncludeServices && footprint.Services.Count > 0)
        {
            try
            {
                svcResult = await _backend.DeleteServicesAsync(footprint.Services, ct)
                    .ConfigureAwait(false);
                errors.AddRange(svcResult.Errors);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                svcResult = BackendActionResult.AllFailed(footprint.Services, e.Message);
                errors.AddRange(svcResult.Errors);
            }
        }

        // -- 2. Delete scheduled tasks ----------------------------------------
        var taskResult = BackendActionResult.Empty;
        if (options.IncludeTasks && footprint.ScheduledTasks.Count > 0)
        {
            try
            {
                taskResult = await _backend.DeleteScheduledTasksAsync(footprint.ScheduledTasks, ct)
                    .ConfigureAwait(false);
                errors.AddRange(taskResult.Errors);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                taskResult = BackendActionResult.AllFailed(footprint.ScheduledTasks, e.Message);
                errors.AddRange(taskResult.Errors);
            }
        }

        // -- 3. Delete registry keys ------------------------------------------
        var regResult = BackendActionResult.Empty;
        if (options.IncludeRegistry && footprint.RegistryKeys.Count > 0)
        {
            try
            {
                regResult = await _backend.DeleteRegistryKeysAsync(footprint.RegistryKeys, ct)
                    .ConfigureAwait(false);
                errors.AddRange(regResult.Errors);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                regResult = BackendActionResult.AllFailed(footprint.RegistryKeys, e.Message);
                errors.AddRange(regResult.Errors);
            }
        }

        // -- 4. Delete files and directories ----------------------------------
        var filePaths = BuildFilePaths(footprint, options);
        var filesDeleted = 0;
        var filesFailed = 0;

        if (filePaths.Count > 0)
        {
            var accepted = new List<string>();
            foreach (var p in filePaths)
            {
                var verdict = _safety.Check(p);
                if (verdict.Allowed)
                    accepted.Add(p);
                else
                    errors.Add($"Refused: {p} — {verdict.Reason}");
            }

            if (accepted.Count > 0)
            {
                try
                {
                    var kill = await _locker.UnlockAsync(
                        accepted,
                        new UnlockOptions(DeleteAfterUnlock: true),
                        ct).ConfigureAwait(false);

                    filesDeleted = kill.DeletedCount;
                    filesFailed = kill.FailedCount + kill.RefusedCount;
                    foreach (var p in kill.Paths.Where(p => p.Outcome == PathOutcome.Failed))
                        errors.Add($"{p.Path}: {p.Message}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    filesFailed = accepted.Count;
                    errors.Add($"File deletion failed: {e.Message}");
                }
            }
        }

        return new DeepUninstallResult(
            FilesDeleted: filesDeleted,
            FilesFailed: filesFailed,
            RegistryKeysDeleted: regResult.Deleted,
            RegistryKeysFailed: regResult.Failed,
            ServicesDeleted: svcResult.Deleted,
            ServicesFailed: svcResult.Failed,
            TasksDeleted: taskResult.Deleted,
            TasksFailed: taskResult.Failed,
            Errors: errors);
    }

    private static IReadOnlyList<string> BuildFilePaths(
        InstallFootprint footprint, InstallCleanOptions options)
    {
        var paths = new List<string>();

        if (!string.IsNullOrEmpty(footprint.InstallRoot))
            paths.Add(footprint.InstallRoot);

        if (options.IncludeUserData)
            paths.AddRange(footprint.DataPaths);

        if (options.IncludeShortcuts)
        {
            paths.AddRange(footprint.StartMenuShortcuts);
            paths.AddRange(footprint.DesktopShortcuts);
        }

        return paths;
    }
}
