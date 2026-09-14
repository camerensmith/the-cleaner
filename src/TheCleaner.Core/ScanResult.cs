namespace TheCleaner.Core;

/// <summary>Everything the confirm UI needs to render before the user commits.</summary>
public sealed record ScanResult(
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> AcceptedPaths,
    IReadOnlyList<PathResult> Refused,
    IReadOnlyList<LockHolder> Holders,
    int FileCount,
    int DirectoryCount,
    IReadOnlyList<string> Errors)
{
    public bool HasWork => AcceptedPaths.Count > 0;
    public bool AnyRefusals => Refused.Count > 0;

    public static ScanResult Empty { get; } = new([], [], [], [], 0, 0, []);
}
