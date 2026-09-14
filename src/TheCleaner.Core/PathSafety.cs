namespace TheCleaner.Core;

public sealed record PathSafetyVerdict(bool Allowed, string? Reason)
{
    public static PathSafetyVerdict Ok { get; } = new(true, null);
    public static PathSafetyVerdict Refuse(string reason) => new(false, reason);
}

/// <summary>
/// Hard refusals. A refused path produces an error and <b>no action at all</b> —
/// it is never unlocked, never deleted, never passed to a platform backend.
/// </summary>
public sealed class PathSafety
{
    private readonly string[] _protected;

    /// <param name="protectedPaths">
    /// Paths that may not be targeted, nor have an ancestor targeted.
    /// Defaults to <see cref="DefaultProtectedPaths"/>.
    /// </param>
    public PathSafety(IEnumerable<string>? protectedPaths = null)
    {
        var source = protectedPaths ?? DefaultProtectedPaths();
        var normalized = new List<string>();
        foreach (var p in source)
        {
            if (TryNormalize(p, out var n)) normalized.Add(n);
        }
        _protected = normalized.ToArray();
    }

    public static IReadOnlyList<string> DefaultProtectedPaths() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows)
    ];

    public PathSafetyVerdict Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathSafetyVerdict.Refuse("Path is empty.");

        if (!TryNormalize(path, out var full))
            return PathSafetyVerdict.Refuse($"Path could not be parsed: {path}");

        var root = Path.GetPathRoot(full);
        if (root is not null && Same(TrimSeparators(root), full))
            return PathSafetyVerdict.Refuse($"Refusing to touch the volume root {full}.");

        foreach (var prot in _protected)
        {
            if (Same(full, prot))
                return PathSafetyVerdict.Refuse($"Refusing to touch the protected path {full}.");

            if (IsAncestorOf(full, prot))
            {
                return PathSafetyVerdict.Refuse(
                    $"Refusing to touch {full} because it contains the protected path {prot}.");
            }
        }

        return PathSafetyVerdict.Ok;
    }

    /// <summary>Full path, separators normalized, trailing separators trimmed
    /// (except on a bare volume root, which keeps none either).</summary>
    private static bool TryNormalize(string path, out string normalized)
    {
        try
        {
            // "C:" means "the current directory on drive C" to Path.GetFullPath. For a
            // tool that deletes things, the safe reading of a bare drive letter is the
            // drive itself, so anchor it before resolving.
            var input = IsBareDriveLetter(path) ? path + Path.DirectorySeparatorChar : path;
            normalized = TrimSeparators(Path.GetFullPath(input));
            return normalized.Length > 0;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool IsBareDriveLetter(string path) =>
        path.Length == 2 && path[1] == ':' && char.IsLetter(path[0]);

    private static string TrimSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // "C:" trims to "C:" — keep it, it compares equal to the root form below.
        return trimmed.Length == 0 ? path : trimmed;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>candidate</c> is a strict parent directory of <c>descendant</c>.</summary>
    private static bool IsAncestorOf(string candidate, string descendant)
    {
        if (descendant.Length <= candidate.Length) return false;
        if (!descendant.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return false;
        var next = descendant[candidate.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }
}
