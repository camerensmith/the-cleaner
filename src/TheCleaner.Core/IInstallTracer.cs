namespace TheCleaner.Core;

/// <summary>Platform-specific tracer: given a dropped path (exe, shortcut, or data file)
/// it discovers the program's complete on-system footprint.</summary>
public interface IInstallTracer
{
    /// <summary>
    /// Traces the install footprint for the program associated with
    /// <paramref name="droppedPath"/>. Never throws for ordinary failures — an
    /// <see cref="InstallFootprint"/> with an empty
    /// <see cref="InstallFootprint.InstallRoot"/> signals nothing was found.
    /// </summary>
    Task<InstallFootprint> TraceAsync(string droppedPath, CancellationToken ct);
}
