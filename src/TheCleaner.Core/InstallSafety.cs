namespace TheCleaner.Core;

/// <summary>Hard refusals for deep-uninstall targets that go beyond plain file paths:
/// Windows system registry keys, system-owned services, and system scheduled tasks.</summary>
public sealed class InstallSafety
{
    // Registry key prefixes that belong to Windows itself — never removed.
    private static readonly string[] SystemRegistryPrefixes =
    [
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions",
        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control",
        @"HKEY_CLASSES_ROOT",
    ];

    // Service names that are always system-owned.
    private static readonly HashSet<string> SystemServices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "wuauserv", "bits", "winmgmt", "eventlog", "rpcss",
            "lanmanworkstation", "lanmanserver", "cryptsvc", "dnscache",
            "dhcp", "w32tm", "spooler", "themes", "schedule",
            "windefend", "mpssvc", "wscsvc",
        };

    // Scheduled task path prefixes that belong to Windows.
    private static readonly string[] SystemTaskPrefixes =
    [
        @"\Microsoft\Windows\",
        @"\Microsoft\XblGameSave",
    ];

    public PathSafetyVerdict CheckRegistryKey(string keyPath)
    {
        foreach (var prefix in SystemRegistryPrefixes)
        {
            if (keyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return PathSafetyVerdict.Refuse(
                    $"Refusing to remove system registry key: {keyPath}");
        }
        return PathSafetyVerdict.Ok;
    }

    public PathSafetyVerdict CheckService(string serviceName)
    {
        if (SystemServices.Contains(serviceName))
            return PathSafetyVerdict.Refuse(
                $"Refusing to remove system service: {serviceName}");
        return PathSafetyVerdict.Ok;
    }

    public PathSafetyVerdict CheckScheduledTask(string taskName)
    {
        foreach (var prefix in SystemTaskPrefixes)
        {
            if (taskName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return PathSafetyVerdict.Refuse(
                    $"Refusing to remove system scheduled task: {taskName}");
        }
        return PathSafetyVerdict.Ok;
    }
}
