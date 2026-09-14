using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace TheCleaner.Windows;

/// <summary>UAC re-launch. The app always starts unelevated; this runs only after a
/// real access-denied, and only with the user's consent at the UAC prompt.</summary>
public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Starts an elevated copy of this executable with the same targets and
    /// action. Returns false with a reason when the user declines the UAC prompt.</summary>
    public static bool Relaunch(IReadOnlyList<string> paths, bool deleteAfterUnlock, out string? error)
    {
        error = null;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            error = "Could not determine the executable path to relaunch.";
            return false;
        }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        psi.ArgumentList.Add("--elevated");
        psi.ArgumentList.Add("--action");
        psi.ArgumentList.Add(deleteAfterUnlock ? "unlock-delete" : "unlock");
        psi.ArgumentList.Add("--");
        foreach (var p in paths) psi.ArgumentList.Add(p);

        try
        {
            var started = Process.Start(psi);
            if (started is null)
            {
                error = "Elevated relaunch did not start.";
                return false;
            }
            return true;
        }
        catch (Win32Exception e)
        {
            // ERROR_CANCELLED (1223) is the user clicking No.
            error = e.NativeErrorCode == 1223
                ? "Elevation required / cancelled."
                : $"Elevation failed: {e.Message}";
            return false;
        }
    }
}
