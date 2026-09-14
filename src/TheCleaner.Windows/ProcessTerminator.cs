using System.ComponentModel;
using System.Diagnostics;
using TheCleaner.Core;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>Terminates locking processes, refusing the ones that would take the
/// session or the machine down with them.</summary>
public sealed class ProcessTerminator
{
    public static IReadOnlySet<string> ProtectedNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "smss", "wininit", "services", "lsass", "System", "winlogon"
        };

    public TerminationReport Terminate(IEnumerable<LockHolder> holders)
    {
        var terminated = new List<TerminatedProcess>();
        var skipped = new List<SkippedProcess>();
        var seen = new HashSet<int>();
        var accessDenied = false;
        var currentPid = Environment.ProcessId;

        foreach (var holder in holders)
        {
            if (!seen.Add(holder.Pid)) continue;

            if (holder.Pid == currentPid)
            {
                skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, "our own process"));
                continue;
            }

            if (holder.Pid is 0 or 4 || ProtectedNames.Contains(holder.ProcessName))
            {
                skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, "protected process"));
                continue;
            }

            var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_TERMINATE, false, holder.Pid);
            if (process == IntPtr.Zero)
            {
                var err = new Win32Exception();
                if (err.NativeErrorCode == NativeMethods.ERROR_ACCESS_DENIED) accessDenied = true;
                skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, err.Message));
                continue;
            }

            try
            {
                if (NativeMethods.TerminateProcess(process, 1))
                {
                    WaitForExit(holder.Pid);
                    terminated.Add(new TerminatedProcess(holder.Pid, holder.ProcessName));
                }
                else
                {
                    var err = new Win32Exception();
                    if (err.NativeErrorCode == NativeMethods.ERROR_ACCESS_DENIED) accessDenied = true;
                    skipped.Add(new SkippedProcess(holder.Pid, holder.ProcessName, err.Message));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }
        }

        return new TerminationReport(terminated, skipped, accessDenied);
    }

    /// <summary>Termination is asynchronous; deleting before the process is gone
    /// would fail for no good reason.</summary>
    private static void WaitForExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit(5_000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Already gone.
        }
    }
}
