using System.Diagnostics;
using TheCleaner.Core;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>
/// Fallback locker discovery: walks the system handle table and matches file handles
/// against the target paths. Slower than Restart Manager but attributes each holder
/// to an exact path and catches holders RM does not report.
/// </summary>
public sealed class NtHandleLockFinder
{
    public IReadOnlyList<LockHolder> Find(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return [];

        var targets = new HashSet<string>(paths.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            try { targets.Add(Path.GetFullPath(p)); }
            catch (ArgumentException) { /* unparseable target: nothing can match it */ }
        }

        var entries = HandleSnapshot.Take();
        if (entries.Length == 0) return [];

        var fileTypeIndex = HandleSnapshot.FileObjectTypeIndex(entries);

        var self = NativeMethods.GetCurrentProcess();
        var currentPid = Environment.ProcessId;
        var found = new Dictionary<(int Pid, string Path), LockHolder>();
        var processHandles = new Dictionary<int, IntPtr>();

        try
        {
            foreach (var entry in entries)
            {
                if (fileTypeIndex.HasValue && entry.ObjectTypeIndex != fileTypeIndex.Value) continue;

                var pid = (int)entry.UniqueProcessId;
                if (pid is 0 or 4) continue;

                if (!processHandles.TryGetValue(pid, out var process))
                {
                    process = pid == currentPid
                        ? self
                        : NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, pid);
                    processHandles[pid] = process;
                }

                if (process == IntPtr.Zero) continue;

                if (!NativeMethods.DuplicateHandle(
                        process, entry.HandleValue, self, out var dup,
                        0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                {
                    continue;
                }

                try
                {
                    // Naming a pipe or socket handle can block forever; disk files cannot.
                    if (NativeMethods.GetFileType(dup) != NativeMethods.FILE_TYPE_DISK) continue;

                    var name = HandleSnapshot.FinalPath(dup);
                    if (name is null || !targets.Contains(name)) continue;

                    var key = (pid, name);
                    if (found.ContainsKey(key)) continue;

                    found[key] = new LockHolder(
                        pid, ProcessNameOf(pid), name, LockHolderSource.HandleScan);
                }
                finally
                {
                    NativeMethods.CloseHandle(dup);
                }
            }
        }
        finally
        {
            foreach (var (pid, handle) in processHandles)
            {
                if (handle != IntPtr.Zero && pid != currentPid) NativeMethods.CloseHandle(handle);
            }
        }

        return [.. found.Values];
    }

    private static string ProcessNameOf(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return $"pid {pid}";
        }
    }
}
