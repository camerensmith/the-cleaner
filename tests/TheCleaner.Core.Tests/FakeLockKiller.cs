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
