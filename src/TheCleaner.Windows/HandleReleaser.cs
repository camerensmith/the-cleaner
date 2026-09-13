using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>
/// Best-effort remote handle close: duplicates each matching handle into this process
/// with DUPLICATE_CLOSE_SOURCE, which severs it in the owning process, then closes our
/// copy. Some holders survive this; termination is the backstop.
/// </summary>
public sealed class HandleReleaser
{
    public int CloseHandlesFor(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return 0;

        var targets = new HashSet<string>(paths.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            try { targets.Add(Path.GetFullPath(p)); }
            catch (ArgumentException) { /* unparseable target: nothing can match it */ }
        }

        var entries = HandleSnapshot.Take();
        if (entries.Length == 0) return 0;

        var fileTypeIndex = HandleSnapshot.FileObjectTypeIndex(entries);

        var self = NativeMethods.GetCurrentProcess();
        var currentPid = Environment.ProcessId;
        var closed = 0;
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

                // Inspect first with a non-destructive duplicate — closing the source is
                // irreversible, so it must only happen once the path is confirmed.
                if (!NativeMethods.DuplicateHandle(
                        process, entry.HandleValue, self, out var probe,
                        0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                {
                    continue;
                }

                bool matches;
                try
                {
                    matches = NativeMethods.GetFileType(probe) == NativeMethods.FILE_TYPE_DISK
                              && HandleSnapshot.FinalPath(probe) is { } name
                              && targets.Contains(name);
                }
                finally
                {
                    NativeMethods.CloseHandle(probe);
                }

                if (!matches) continue;

                if (NativeMethods.DuplicateHandle(
                        process, entry.HandleValue, self, out var stolen,
                        0, false, NativeMethods.DUPLICATE_CLOSE_SOURCE | NativeMethods.DUPLICATE_SAME_ACCESS))
                {
                    NativeMethods.CloseHandle(stolen);
                    closed++;
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

        return closed;
    }
}
