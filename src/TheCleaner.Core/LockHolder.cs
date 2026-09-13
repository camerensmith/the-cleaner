namespace TheCleaner.Core;

/// <summary>How a <see cref="LockHolder"/> was discovered.</summary>
public enum LockHolderSource
{
    /// <summary>Windows Restart Manager. Reports processes for the whole registered
    /// batch, so it cannot attribute a holder to a single file.</summary>
    RestartManager,

    /// <summary>System handle-table scan. Attributes each holder to an exact path.</summary>
    HandleScan
}

/// <param name="Pid">Process id holding the lock.</param>
/// <param name="ProcessName">Process name without extension, e.g. "notepad".</param>
/// <param name="Path">
/// The locked path when the source can attribute one (<see cref="LockHolderSource.HandleScan"/>),
/// otherwise the scanned root that the batch was registered under
/// (<see cref="LockHolderSource.RestartManager"/>). Never null; may be empty.
/// </param>
/// <param name="Source">Which discovery mechanism found this holder.</param>
public sealed record LockHolder(int Pid, string ProcessName, string Path, LockHolderSource Source);
