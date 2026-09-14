namespace TheCleaner.Core;

/// <summary>The complete on-disk, registry, and system footprint of an installed program,
/// as discovered by an <see cref="IInstallTracer"/>.</summary>
public sealed record InstallFootprint(
    /// <summary>The primary installation directory (e.g., C:\Program Files\MyApp).</summary>
    string InstallRoot,

    /// <summary>Human-readable name derived from registry DisplayName or directory name.</summary>
    string AppName,

    /// <summary>All executable (.exe) files found inside the install root.</summary>
    IReadOnlyList<string> ExePaths,

    /// <summary>AppData, ProgramData, and user-profile data directories for this program.</summary>
    IReadOnlyList<string> DataPaths,

    /// <summary>Registry key paths (e.g., HKEY_LOCAL_MACHINE\SOFTWARE\...) to remove.</summary>
    IReadOnlyList<string> RegistryKeys,

    /// <summary>Windows service names registered by this program.</summary>
    IReadOnlyList<string> Services,

    /// <summary>Scheduled task names registered by this program.</summary>
    IReadOnlyList<string> ScheduledTasks,

    /// <summary>Start Menu .lnk shortcut paths pointing into the install root.</summary>
    IReadOnlyList<string> StartMenuShortcuts,

    /// <summary>Desktop .lnk shortcut paths pointing into the install root.</summary>
    IReadOnlyList<string> DesktopShortcuts)
{
    public static InstallFootprint Empty { get; } =
        new(string.Empty, string.Empty, [], [], [], [], [], [], []);

    /// <summary>True when at least an install root was discovered.</summary>
    public bool HasWork => !string.IsNullOrEmpty(InstallRoot);
}
