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
        Task.Run(() => FindCore(paths, ct), ct);

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
