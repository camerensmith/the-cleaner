using TheCleaner.Core;

namespace TheCleaner;

/// <summary>Compile-time backend selection. The csproj references exactly one platform
/// project based on the publish RID, and defines the matching constant.</summary>
internal static class PlatformBackend
{
    public static ILockKiller Create() =>
#if PLATFORM_WINDOWS
        new TheCleaner.Windows.WindowsLockKiller();
#elif PLATFORM_LINUX
        new TheCleaner.Linux.LinuxLockKiller();
#elif PLATFORM_MAC
        new TheCleaner.Mac.MacLockKiller();
#else
        throw new PlatformNotSupportedException(
            "No platform backend was compiled into this build of thecleaner.");
#endif

    /// <summary>Returns the install tracer for this platform, or <c>null</c> when
    /// the feature is not supported on the current OS.</summary>
    public static IInstallTracer? CreateInstallTracer() =>
#if PLATFORM_WINDOWS
        new TheCleaner.Windows.WindowsInstallTracer();
#else
        null;
#endif

    /// <summary>Returns the deep-uninstall backend for this platform, or <c>null</c>
    /// when the feature is not supported on the current OS.</summary>
    public static IDeepUninstallBackend? CreateDeepUninstallBackend() =>
#if PLATFORM_WINDOWS
        new TheCleaner.Windows.WindowsDeepUninstallBackend();
#else
        null;
#endif

    /// <summary>True when this build can ask the OS to re-run it elevated.</summary>
    public static bool SupportsElevation =>
#if PLATFORM_WINDOWS
        true;
#else
        false;
#endif

    public static bool IsElevated =>
#if PLATFORM_WINDOWS
        TheCleaner.Windows.Elevation.IsElevated;
#else
        false;
#endif

    public static bool Relaunch(IReadOnlyList<string> paths, bool deleteAfterUnlock, out string? error)
    {
#if PLATFORM_WINDOWS
        return TheCleaner.Windows.Elevation.Relaunch(paths, deleteAfterUnlock, out error);
#else
        error = "Elevation is not supported in this build.";
        return false;
#endif
    }
}
