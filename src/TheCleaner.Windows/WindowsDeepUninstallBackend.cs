using System.Diagnostics;
using Microsoft.Win32;
using TheCleaner.Core;

namespace TheCleaner.Windows;

/// <summary>
/// Windows implementation of <see cref="IDeepUninstallBackend"/>: removes services via
/// the SCM, deletes scheduled tasks via <c>schtasks.exe</c>, and removes registry keys
/// via the .NET Registry API.
/// </summary>
public sealed class WindowsDeepUninstallBackend : IDeepUninstallBackend
{
    private readonly InstallSafety _safety;

    public WindowsDeepUninstallBackend(InstallSafety? safety = null)
    {
        _safety = safety ?? new InstallSafety();
    }

    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------

    public Task<BackendActionResult> DeleteServicesAsync(
        IReadOnlyList<string> serviceNames, CancellationToken ct) =>
        Task.Run(() => DeleteServicesCore(serviceNames, ct), ct);

    private BackendActionResult DeleteServicesCore(
        IReadOnlyList<string> serviceNames, CancellationToken ct)
    {
        var deleted = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var name in serviceNames)
        {
            ct.ThrowIfCancellationRequested();

            var safety = _safety.CheckService(name);
            if (!safety.Allowed)
            {
                errors.Add(safety.Reason!);
                failed++;
                continue;
            }

            // Stop then delete via sc.exe (avoids adding System.ServiceProcess dependency).
            if (RunSc($"stop {name}") || true) // ignore stop failures — service may already be stopped
            {
                if (RunSc($"delete {name}"))
                    deleted++;
                else
                {
                    failed++;
                    errors.Add($"sc delete {name}: failed.");
                }
            }
        }

        return new BackendActionResult(deleted, failed, errors);
    }

    private static bool RunSc(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Scheduled tasks
    // -------------------------------------------------------------------------

    public Task<BackendActionResult> DeleteScheduledTasksAsync(
        IReadOnlyList<string> taskNames, CancellationToken ct) =>
        Task.Run(() => DeleteTasksCore(taskNames, ct), ct);

    private BackendActionResult DeleteTasksCore(
        IReadOnlyList<string> taskNames, CancellationToken ct)
    {
        var deleted = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var name in taskNames)
        {
            ct.ThrowIfCancellationRequested();

            var safety = _safety.CheckScheduledTask(name);
            if (!safety.Allowed)
            {
                errors.Add(safety.Reason!);
                failed++;
                continue;
            }

            if (RunSchtasks($"/delete /tn \"{name}\" /f"))
                deleted++;
            else
            {
                failed++;
                errors.Add($"schtasks /delete /tn \"{name}\": failed.");
            }
        }

        return new BackendActionResult(deleted, failed, errors);
    }

    private static bool RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Registry keys
    // -------------------------------------------------------------------------

    public Task<BackendActionResult> DeleteRegistryKeysAsync(
        IReadOnlyList<string> keyPaths, CancellationToken ct) =>
        Task.Run(() => DeleteRegistryKeysCore(keyPaths, ct), ct);

    private BackendActionResult DeleteRegistryKeysCore(
        IReadOnlyList<string> keyPaths, CancellationToken ct)
    {
        var deleted = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var keyPath in keyPaths)
        {
            ct.ThrowIfCancellationRequested();

            var safety = _safety.CheckRegistryKey(keyPath);
            if (!safety.Allowed)
            {
                errors.Add(safety.Reason!);
                failed++;
                continue;
            }

            if (!TrySplitRegistryPath(keyPath, out var hive, out var subPath))
            {
                errors.Add($"Cannot parse registry path: {keyPath}");
                failed++;
                continue;
            }

            try
            {
                hive.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or ArgumentException)
            {
                failed++;
                errors.Add($"{keyPath}: {e.Message}");
            }
        }

        return new BackendActionResult(deleted, failed, errors);
    }

    private static bool TrySplitRegistryPath(
        string keyPath, out RegistryKey hive, out string subPath)
    {
        hive = Registry.LocalMachine; // default
        subPath = string.Empty;

        var sep = keyPath.IndexOf('\\');
        if (sep < 0) return false;

        var hiveName = keyPath[..sep].ToUpperInvariant();
        subPath = keyPath[(sep + 1)..];

        hive = hiveName switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            "HKEY_USERS" or "HKU" => Registry.Users,
            _ => Registry.LocalMachine
        };

        return !string.IsNullOrEmpty(subPath);
    }
}
