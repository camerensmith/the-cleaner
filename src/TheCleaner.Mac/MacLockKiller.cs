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
