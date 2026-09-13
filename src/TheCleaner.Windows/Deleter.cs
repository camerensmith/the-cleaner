using TheCleaner.Core;

namespace TheCleaner.Windows;

/// <param name="AccessDenied">True when a delete failed with access denied — the
/// signal to offer an elevated retry.</param>
public sealed record DeleteReport(IReadOnlyList<PathResult> Results, bool AccessDenied);

/// <summary>Removes targets deepest-first so a directory is only attempted once its
/// contents are gone. A path that is already absent counts as deleted.</summary>
public sealed class Deleter
{
    public DeleteReport Delete(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return new DeleteReport([], false);

        var ordered = paths.ToList();
        ordered.Sort(static (a, b) => Depth(b).CompareTo(Depth(a)));

        var results = new List<PathResult>(ordered.Count);
        var accessDenied = false;

        foreach (var path in ordered)
        {
            try
            {
                if (File.Exists(path))
                {
                    ClearReadOnly(path);
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    ClearReadOnly(path);
                    Directory.Delete(path, recursive: false);
                }
                // Absent already: nothing to do.

                results.Add(new PathResult(path, PathOutcome.Deleted));
            }
            catch (UnauthorizedAccessException e)
            {
                accessDenied = true;
                results.Add(new PathResult(path, PathOutcome.Failed, e.Message));
            }
            catch (IOException e)
            {
                results.Add(new PathResult(path, PathOutcome.Failed, e.Message));
            }
        }

        return new DeleteReport(results, accessDenied);
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Let the delete itself report the failure.
        }
    }

    private static int Depth(string path)
    {
        var n = 0;
        foreach (var c in path)
        {
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) n++;
        }
        return n;
    }
}
