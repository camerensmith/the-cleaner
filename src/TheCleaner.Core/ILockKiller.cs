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
