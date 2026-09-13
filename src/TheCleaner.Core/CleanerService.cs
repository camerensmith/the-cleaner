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
