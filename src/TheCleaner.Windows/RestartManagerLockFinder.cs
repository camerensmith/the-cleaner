using System.Text;
using TheCleaner.Core;
using TheCleaner.Windows.Interop;

namespace TheCleaner.Windows;

/// <summary>
/// Primary locker discovery. Restart Manager answers for the whole registered batch,
/// so every holder it reports carries the caller-supplied <c>displayPath</c> rather
/// than a per-file attribution — <see cref="NtHandleLockFinder"/> supplies exact paths.
/// </summary>
public sealed class RestartManagerLockFinder
{
    /// <summary>Registering a huge batch makes RmRegisterResources crawl. Past this
    /// many paths we register a prefix and let the handle scan cover the rest.</summary>
    public const int MaxRegisteredPaths = 2000;

    private const int RegisterChunkSize = 250;

    public IReadOnlyList<LockHolder> Find(IReadOnlyList<string> paths, string displayPath)
    {
        if (paths.Count == 0) return [];

        var key = new StringBuilder(NativeMethods.CCH_RM_SESSION_KEY + 1);
        if (NativeMethods.RmStartSession(out var session, 0, key) != NativeMethods.ERROR_SUCCESS)
            return [];

        try
        {
            var toRegister = paths.Count > MaxRegisteredPaths
                ? paths.Take(MaxRegisteredPaths).ToArray()
                : paths.ToArray();

            for (var i = 0; i < toRegister.Length; i += RegisterChunkSize)
            {
                var chunk = toRegister.Skip(i).Take(RegisterChunkSize).ToArray();

                // A bad path in one chunk must not sink the whole scan.
                NativeMethods.RmRegisterResources(
                    session, (uint)chunk.Length, chunk, 0, null, 0, null);
            }

            return GetList(session, displayPath);
        }
        finally
        {
            NativeMethods.RmEndSession(session);
        }
    }

    private static IReadOnlyList<LockHolder> GetList(uint session, string displayPath)
    {
        uint count = 0;
        var rc = NativeMethods.RmGetList(session, out var needed, ref count, null, out _);

        if (rc == NativeMethods.ERROR_SUCCESS || needed == 0) return [];
        if (rc != NativeMethods.ERROR_MORE_DATA) return [];

        var infos = new NativeMethods.RM_PROCESS_INFO[needed];
        count = needed;
        rc = NativeMethods.RmGetList(session, out needed, ref count, infos, out _);
        if (rc != NativeMethods.ERROR_SUCCESS) return [];

        var holders = new List<LockHolder>((int)count);
        for (var i = 0; i < count; i++)
        {
            var info = infos[i];
            var name = string.IsNullOrWhiteSpace(info.strAppName)
                ? (string.IsNullOrWhiteSpace(info.strServiceShortName)
                    ? $"pid {info.Process.dwProcessId}"
                    : info.strServiceShortName)
                : info.strAppName;

            holders.Add(new LockHolder(
                info.Process.dwProcessId,
                StripExtension(name),
                displayPath,
                LockHolderSource.RestartManager));
        }

        return holders;
    }

    private static string StripExtension(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
