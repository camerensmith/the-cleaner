using System.Runtime.InteropServices;
using System.Text;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>Shared plumbing for reading the system handle table and naming the file
/// handles in it. Every operation degrades quietly rather than throwing.</summary>
internal static class HandleSnapshot
{
    /// <summary>Copies the system handle table into managed memory. Returns an empty
    /// array rather than throwing when the query fails.</summary>
    public static NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[] Take()
    {
        var size = 1 << 20;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NativeMethods.NtQuerySystemInformation(
                    NativeMethods.SystemExtendedHandleInformation, buffer, size, out _);

                if (status == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
                {
                    size *= 2;
                    continue;
                }
                if (status != 0) return [];

                var count = Marshal.ReadIntPtr(buffer).ToInt64();
                var entrySize = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
                var start = buffer + (IntPtr.Size * 2); // NumberOfHandles + Reserved

                // PtrToStructure copies into managed memory, so freeing the buffer in the
                // finally below leaves the returned entries valid.
                var entries = new NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[count];
                for (long i = 0; i < count; i++)
                {
                    entries[i] = Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(
                        start + (int)(i * entrySize));
                }
                return entries;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return [];
    }

    /// <summary>Opens a throwaway file and looks its handle up in the snapshot to learn
    /// which object-type index means "File" on this machine. Lets callers skip the
    /// expensive duplicate-and-name work on the tens of thousands of non-file handles.</summary>
    public static ushort? FileObjectTypeIndex(
        NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX[] entries)
    {
        var probePath = Path.Combine(Path.GetTempPath(), $"thecleaner-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var probe = new FileStream(
                probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);

            var handle = probe.SafeFileHandle.DangerousGetHandle();
            var pid = Environment.ProcessId;

            foreach (var entry in entries)
            {
                if ((int)entry.UniqueProcessId == pid && entry.HandleValue == handle)
                    return entry.ObjectTypeIndex;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No probe: the caller falls back to inspecting every handle.
        }

        return null;
    }

    /// <summary>The DOS path behind a handle, or null. Only call this on a handle that
    /// <see cref="NativeMethods.GetFileType"/> reported as FILE_TYPE_DISK — naming a
    /// pipe or socket handle can block forever.</summary>
    public static string? FinalPath(IntPtr handle)
    {
        var sb = new StringBuilder(1024);
        var len = NativeMethods.GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
        if (len == 0) return null;

        if (len > sb.Capacity)
        {
            sb = new StringBuilder((int)len + 1);
            len = NativeMethods.GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
            if (len == 0) return null;
        }

        var path = sb.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path[4..];
        return path;
    }
}
