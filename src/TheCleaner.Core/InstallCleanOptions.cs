namespace TheCleaner.Core;

/// <summary>Controls which artifact categories are removed by
/// <see cref="DeepUninstallService"/>.</summary>
public sealed record InstallCleanOptions(
    bool IncludeUserData = true,
    bool IncludeRegistry = true,
    bool IncludeServices = true,
    bool IncludeTasks = true,
    bool IncludeShortcuts = true)
{
    public static InstallCleanOptions All { get; } = new();
}
