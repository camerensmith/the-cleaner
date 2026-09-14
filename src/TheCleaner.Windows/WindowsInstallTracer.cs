using System.Diagnostics;
using Microsoft.Win32;
using TheCleaner.Core;

namespace TheCleaner.Windows;

/// <summary>
/// Discovers the complete install footprint of a program on Windows by examining the
/// file system, registry uninstall keys, service database, task scheduler, and shortcut
/// locations.
/// </summary>
public sealed class WindowsInstallTracer : IInstallTracer
{
    private static readonly string[] InstallLocationRoots = BuildInstallLocationRoots();

    private static string[] BuildInstallLocationRoots()
    {
        var roots = new List<string>();
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            Add(Path.Combine(local, "Programs"));

        return [.. roots];

        void Add(string p)
        {
            if (!string.IsNullOrEmpty(p) && !roots.Contains(p, StringComparer.OrdinalIgnoreCase))
                roots.Add(p);
        }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public Task<InstallFootprint> TraceAsync(string droppedPath, CancellationToken ct) =>
        Task.Run(() => TraceCore(droppedPath, ct), ct);

    // -------------------------------------------------------------------------
    // Core tracing
    // -------------------------------------------------------------------------

    private InstallFootprint TraceCore(string droppedPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var (exePath, installRoot) = ResolveToInstallRoot(droppedPath);
        if (string.IsNullOrEmpty(installRoot))
            return InstallFootprint.Empty;

        ct.ThrowIfCancellationRequested();

        var exePaths = FindExecutables(installRoot);
        var (registryKeys, appName) = FindRegistryInfo(installRoot, exePath);

        if (string.IsNullOrEmpty(appName))
            appName = DeriveAppName(installRoot, exePath);

        ct.ThrowIfCancellationRequested();

        var services = FindServices(installRoot);
        var scheduledTasks = FindScheduledTasks(installRoot);

        ct.ThrowIfCancellationRequested();

        var dataPaths = FindDataPaths(appName);
        var (startMenuShortcuts, desktopShortcuts) = FindShortcuts(installRoot);

        return new InstallFootprint(
            InstallRoot: installRoot,
            AppName: appName,
            ExePaths: exePaths,
            DataPaths: dataPaths,
            RegistryKeys: registryKeys,
            Services: services,
            ScheduledTasks: scheduledTasks,
            StartMenuShortcuts: startMenuShortcuts,
            DesktopShortcuts: desktopShortcuts);
    }

    // -------------------------------------------------------------------------
    // Step 1 – resolve dropped path → (exePath, installRoot)
    // -------------------------------------------------------------------------

    private static (string ExePath, string InstallRoot) ResolveToInstallRoot(string droppedPath)
    {
        var ext = Path.GetExtension(droppedPath).ToLowerInvariant();
        string? exePath = null;

        if (ext == ".lnk")
        {
            if (ShortcutResolver.TryResolve(droppedPath, out var target) && File.Exists(target))
                exePath = target;
        }
        else if (ext == ".exe" && File.Exists(droppedPath))
        {
            exePath = droppedPath;
        }
        else
        {
            // Any other file: try to find the associated exe nearby.
            exePath = FindNearestExe(droppedPath);
        }

        var searchPath = exePath ?? droppedPath;
        var installRoot = FindInstallRoot(searchPath) ?? string.Empty;

        return (exePath ?? string.Empty, installRoot);
    }

    /// <summary>Finds the first .exe file in the same directory or immediate parent.</summary>
    private static string? FindNearestExe(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrEmpty(dir))
        {
            try
            {
                var exe = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (exe is not null) return exe;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            // Stop as soon as we leave a known install location.
            if (IsDirectlyInsideInstallLocation(dir)) break;

            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>Walks up the directory tree and returns the deepest ancestor that is a
    /// direct child of one of the known install location roots.</summary>
    internal static string? FindInstallRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = File.Exists(full) ? Path.GetDirectoryName(full) : full;
        if (string.IsNullOrEmpty(dir)) return null;

        // If already at an install location root, nothing useful to trace.
        foreach (var root in InstallLocationRoots)
        {
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Walk up to find the child-of-install-location directory.
        var candidate = dir;
        while (!string.IsNullOrEmpty(candidate))
        {
            var parent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrEmpty(parent)) break;

            foreach (var root in InstallLocationRoots)
            {
                if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            candidate = parent;
        }

        // Not under a standard install location — use the directory containing the file
        // (or the directory itself) as a best-effort root, as long as it's not a
        // well-known Windows system or user folder.
        if (!IsSystemOrUserRoot(dir))
            return dir;

        return null;
    }

    private static bool IsDirectlyInsideInstallLocation(string dir)
    {
        var parent = Path.GetDirectoryName(dir);
        return parent is not null
            && InstallLocationRoots.Contains(parent, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSystemOrUserRoot(string dir)
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return string.Equals(dir, systemRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dir, userProfile, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dir, programData, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Step 2 – find executables in the install root
    // -------------------------------------------------------------------------

    private static IReadOnlyList<string> FindExecutables(string installRoot)
    {
        try
        {
            return [.. Directory.EnumerateFiles(installRoot, "*.exe", SearchOption.AllDirectories)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // -------------------------------------------------------------------------
    // Step 3 – registry: uninstall keys and vendor software keys
    // -------------------------------------------------------------------------

    private static (IReadOnlyList<string> Keys, string AppName) FindRegistryInfo(
        string installRoot, string exePath)
    {
        var keys = new List<string>();
        var appName = string.Empty;

        // Scan both HKLM and HKCU uninstall hives.
        ScanUninstallHive(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            installRoot, exePath, keys, ref appName);

        ScanUninstallHive(
            Registry.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            installRoot, exePath, keys, ref appName);

        // Also look for vendor software keys (HKLM\SOFTWARE\<vendorOrApp>).
        if (!string.IsNullOrEmpty(appName))
        {
            TryAddVendorKey(Registry.LocalMachine, @"SOFTWARE", appName, installRoot, keys);
            TryAddVendorKey(Registry.CurrentUser, @"SOFTWARE", appName, installRoot, keys);
        }

        return (keys, appName);
    }

    private static void ScanUninstallHive(
        RegistryKey hive,
        string uninstallPath,
        string installRoot,
        string exePath,
        List<string> keys,
        ref string appName)
    {
        try
        {
            using var uninstallKey = hive.OpenSubKey(uninstallPath);
            if (uninstallKey is null) return;

            foreach (var subName in uninstallKey.GetSubKeyNames())
            {
                try
                {
                    using var sub = uninstallKey.OpenSubKey(subName);
                    if (sub is null) continue;

                    if (MatchesInstall(sub, installRoot, exePath))
                    {
                        if (string.IsNullOrEmpty(appName))
                            appName = sub.GetValue("DisplayName") as string ?? string.Empty;

                        // Record the full registry path.
                        keys.Add($@"{HiveName(hive)}\{uninstallPath}\{subName}");
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool MatchesInstall(RegistryKey sub, string installRoot, string exePath)
    {
        foreach (var valueName in new[] { "InstallLocation", "UninstallString", "DisplayIcon" })
        {
            var val = sub.GetValue(valueName) as string;
            if (string.IsNullOrEmpty(val)) continue;

            // Strip any arguments from UninstallString / DisplayIcon.
            var pathPart = ExtractPath(val);
            if (string.IsNullOrEmpty(pathPart)) continue;

            if (IsUnderOrEqual(pathPart, installRoot))
                return true;

            if (!string.IsNullOrEmpty(exePath)
                && string.Equals(
                    Path.GetFullPath(pathPart),
                    Path.GetFullPath(exePath),
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void TryAddVendorKey(
        RegistryKey hive, string softwarePath, string appName,
        string installRoot, List<string> keys)
    {
        try
        {
            // Check direct match and first-word match (e.g. "Acme" for "Acme MyApp").
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                appName,
                appName.Split(' ')[0],
                Path.GetFileNameWithoutExtension(installRoot),
            };

            using var sw = hive.OpenSubKey(softwarePath);
            if (sw is null) return;

            foreach (var name in sw.GetSubKeyNames())
            {
                if (!candidates.Contains(name)) continue;

                // Verify the sub-key actually references our install location.
                try
                {
                    using var sub = sw.OpenSubKey(name);
                    if (sub is null) continue;
                    if (VendorKeyReferencesInstall(sub, installRoot))
                        keys.Add($@"{HiveName(hive)}\{softwarePath}\{name}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool VendorKeyReferencesInstall(RegistryKey key, string installRoot)
    {
        foreach (var valueName in key.GetValueNames())
        {
            var val = key.GetValue(valueName) as string;
            if (!string.IsNullOrEmpty(val) && IsUnderOrEqual(val, installRoot))
                return true;
        }
        foreach (var subName in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(subName);
                if (sub is not null && VendorKeyReferencesInstall(sub, installRoot))
                    return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return false;
    }

    private static string HiveName(RegistryKey hive)
    {
        if (hive == Registry.LocalMachine) return "HKEY_LOCAL_MACHINE";
        if (hive == Registry.CurrentUser) return "HKEY_CURRENT_USER";
        if (hive == Registry.ClassesRoot) return "HKEY_CLASSES_ROOT";
        return hive.Name;
    }

    // -------------------------------------------------------------------------
    // Step 4 – services: query HKLM\SYSTEM\CurrentControlSet\Services
    // -------------------------------------------------------------------------

    private static IReadOnlyList<string> FindServices(string installRoot)
    {
        var found = new List<string>();
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services");
            if (services is null) return found;

            foreach (var name in services.GetSubKeyNames())
            {
                try
                {
                    using var svc = services.OpenSubKey(name);
                    if (svc is null) continue;

                    var imagePath = svc.GetValue("ImagePath") as string;
                    if (string.IsNullOrEmpty(imagePath)) continue;

                    var exe = ExtractPath(imagePath);
                    if (!string.IsNullOrEmpty(exe) && IsUnderOrEqual(exe, installRoot))
                        found.Add(name);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return found;
    }

    // -------------------------------------------------------------------------
    // Step 5 – scheduled tasks: scan %SystemRoot%\System32\Tasks
    // -------------------------------------------------------------------------

    private static IReadOnlyList<string> FindScheduledTasks(string installRoot)
    {
        var found = new List<string>();
        var tasksRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");

        if (!Directory.Exists(tasksRoot)) return found;

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                tasksRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (!TaskXmlReferencesInstall(content, installRoot)) continue;

                    // Task name is relative path from tasksRoot, with back-slashes.
                    var relPath = file[tasksRoot.Length..].Replace('/', '\\');
                    if (!relPath.StartsWith('\\')) relPath = '\\' + relPath;
                    found.Add(relPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return found;
    }

    private static bool TaskXmlReferencesInstall(string xml, string installRoot)
    {
        var norm = installRoot.TrimEnd('\\', '/');
        // Case-insensitive substring search for the install root path inside the XML.
        return xml.Contains(norm, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Step 6 – data paths: known per-user and machine data locations
    // -------------------------------------------------------------------------

    private static IReadOnlyList<string> FindDataPaths(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return [];

        var candidates = new List<string>();
        foreach (var (folder, subPath) in DataFolders(appName))
        {
            var full = Path.Combine(folder, subPath);
            if (Directory.Exists(full))
                candidates.Add(full);
        }
        return candidates;
    }

    private static IEnumerable<(string Folder, string Sub)> DataFolders(string appName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return (appData, appName);
        yield return (localAppData, appName);
        yield return (programData, appName);
        yield return (userProfile, appName);
    }

    // -------------------------------------------------------------------------
    // Step 7 – shortcuts: Start Menu and Desktop
    // -------------------------------------------------------------------------

    private static (IReadOnlyList<string> StartMenu, IReadOnlyList<string> Desktop)
        FindShortcuts(string installRoot)
    {
        var startMenu = new List<string>();
        var desktop = new List<string>();

        ScanForShortcuts(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            installRoot, startMenu);
        ScanForShortcuts(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            installRoot, startMenu);

        ScanForShortcuts(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            installRoot, desktop);
        ScanForShortcuts(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            installRoot, desktop);

        return (startMenu, desktop);
    }

    private static void ScanForShortcuts(string folder, string installRoot, List<string> results)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            foreach (var lnk in Directory.EnumerateFiles(
                folder, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    if (ShortcutResolver.TryResolve(lnk, out var target)
                        && IsUnderOrEqual(target, installRoot))
                    {
                        results.Add(lnk);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    internal static string DeriveAppName(string installRoot, string exePath)
    {
        // 1. Use the FileVersionInfo product name from the main exe.
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(fvi.ProductName)) return fvi.ProductName;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // 2. Fall back to the install root directory name.
        return Path.GetFileName(installRoot.TrimEnd('\\', '/')) ?? string.Empty;
    }

    /// <summary>True when <paramref name="path"/> is equal to or resides under
    /// <paramref name="root"/>.</summary>
    internal static bool IsUnderOrEqual(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;

        string fullPath, fullRoot;
        try
        {
            fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');
            fullRoot = Path.GetFullPath(root).TrimEnd('\\', '/');
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException
                                      or PathTooLongException)
        {
            return false;
        }

        if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        return fullPath.StartsWith(
            fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the first path token from strings like
    /// <c>"C:\path\to\uninstall.exe" /quiet</c> or
    /// <c>C:\path\icon.dll,0</c>.</summary>
    private static string ExtractPath(string raw)
    {
        raw = raw.Trim();

        // Quoted path.
        if (raw.StartsWith('"'))
        {
            var end = raw.IndexOf('"', 1);
            return end > 1 ? raw[1..end] : string.Empty;
        }

        // Unquoted: take up to the first space or comma that would start arguments.
        // Heuristic: if the text contains a drive letter we split on space; otherwise
        // we take the whole string as a key name and return it unchanged.
        var spaceIdx = raw.IndexOf(' ');
        var commaIdx = raw.IndexOf(',');

        var splitAt = (spaceIdx, commaIdx) switch
        {
            ( < 0, < 0) => -1,
            ( >= 0, < 0) => spaceIdx,
            ( < 0, >= 0) => commaIdx,
            _ => Math.Min(spaceIdx, commaIdx)
        };

        return splitAt >= 0 ? raw[..splitAt] : raw;
    }
}
