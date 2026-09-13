namespace TheCleaner.Core;

/// <param name="Files">Every concrete file under the roots.</param>
/// <param name="Directories">Every directory, deepest-first, roots last.</param>
/// <param name="Errors">Human-readable problems; expansion never throws for these.</param>
public sealed record ExpandedTarget(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Directories,
    IReadOnlyList<string> Errors)
{
    public int FileCount => Files.Count;

    /// <summary>Files first, then directories deepest-first — the order a deleter wants.</summary>
    public IReadOnlyList<string> AllPaths => [.. Files, .. Directories];

    public static ExpandedTarget Empty { get; } = new([], [], []);
}

/// <summary>Walks roots depth-first into concrete paths. Symlinked directories are
/// recorded but not followed, so a link loop cannot hang the walk.</summary>
public sealed class PathExpander
{
    public ExpandedTarget Expand(IEnumerable<string> roots)
    {
        var files = new List<string>();
        var dirs = new List<string>();
        var errors = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            string full;
            try
            {
                full = Path.GetFullPath(root);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"{root}: {e.Message}");
                continue;
            }

            if (File.Exists(full))
            {
                if (seenFiles.Add(full)) files.Add(full);
            }
            else if (Directory.Exists(full))
            {
                Walk(full, files, dirs, errors, seenFiles, seenDirs);
            }
            else
            {
                errors.Add($"{full}: path not found.");
            }
        }

        dirs.Sort(static (a, b) => Depth(b).CompareTo(Depth(a)));
        return new ExpandedTarget(files, dirs, errors);
    }

    private static void Walk(
        string dir,
        List<string> files,
        List<string> dirs,
        List<string> errors,
        HashSet<string> seenFiles,
        HashSet<string> seenDirs)
    {
        if (!seenDirs.Add(dir)) return;
        dirs.Add(dir);

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{dir}: {e.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(entry);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{entry}: {e.Message}");
                continue;
            }

            var isDir = attrs.HasFlag(FileAttributes.Directory);
            var isLink = attrs.HasFlag(FileAttributes.ReparsePoint);

            if (isDir && !isLink)
            {
                Walk(entry, files, dirs, errors, seenFiles, seenDirs);
            }
            else if (isDir)
            {
                // A directory symlink/junction: delete the link, never its contents.
                if (seenDirs.Add(entry)) dirs.Add(entry);
            }
            else
            {
                if (seenFiles.Add(entry)) files.Add(entry);
            }
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
