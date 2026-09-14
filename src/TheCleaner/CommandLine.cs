namespace TheCleaner;

/// <param name="Paths">Targets from drag-drop-onto-exe or an elevated relaunch.</param>
/// <param name="Elevated">True when this instance was started by an elevated relaunch.</param>
/// <param name="DeleteAfterUnlock">The action the user already chose, when a relaunch
/// is carrying it forward; null when the user has not chosen yet.</param>
public sealed record CommandLineArgs(
    IReadOnlyList<string> Paths, bool Elevated, bool? DeleteAfterUnlock)
{
    public bool HasPaths => Paths.Count > 0;

    public static CommandLineArgs Parse(string[] args)
    {
        var paths = new List<string>();
        var elevated = false;
        bool? delete = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--")
            {
                // Everything after the separator is a literal path.
                for (var j = i + 1; j < args.Length; j++) paths.Add(args[j]);
                break;
            }

            if (arg == "--elevated")
            {
                elevated = true;
            }
            else if (arg == "--action")
            {
                if (i + 1 >= args.Length) break;
                delete = args[++i] switch
                {
                    "unlock-delete" => true,
                    "unlock" => false,
                    _ => null
                };
            }
            else
            {
                paths.Add(arg);
            }
        }

        return new CommandLineArgs(paths, elevated, delete);
    }
}
