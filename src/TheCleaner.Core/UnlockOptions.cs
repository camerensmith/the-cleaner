namespace TheCleaner.Core;

/// <param name="DeleteAfterUnlock">Delete the targets once the lockers are gone.</param>
/// <param name="TerminateLockers">Always true in v1; kept explicit for v2 opt-out.</param>
public sealed record UnlockOptions(bool DeleteAfterUnlock, bool TerminateLockers = true);
