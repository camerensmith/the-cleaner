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
