using System.Text;

namespace TheCleaner.Core;

/// <summary>Writes a plain-text record of the most recent run. Logging is best-effort:
/// it must never be the reason a run fails.</summary>
public sealed class RunLogger
{
    private readonly string _path;

    public RunLogger(string logPath) => _path = logPath;

    public static string DefaultLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "thecleaner",
        "last-run.log");

    public void Write(ScanResult scan, KillResult result, UnlockOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("thecleaner run ").AppendLine(DateTimeOffset.Now.ToString("u"));
        sb.Append("Action: ").AppendLine(options.DeleteAfterUnlock ? "Unlock & Delete" : "Unlock");
        sb.AppendLine();

        sb.AppendLine("Targets:");
        foreach (var root in scan.Roots) sb.Append("  ").AppendLine(root);
        sb.Append("  (").Append(scan.FileCount).Append(" files, ")
          .Append(scan.DirectoryCount).AppendLine(" directories)");
        sb.AppendLine();

        sb.AppendLine("Lockers found:");
        if (scan.Holders.Count == 0) sb.AppendLine("  (none)");
        foreach (var h in scan.Holders)
        {
            sb.Append("  ").Append(h.ProcessName).Append(" (pid ").Append(h.Pid).Append(") [")
              .Append(h.Source).Append("] ").AppendLine(h.Path);
        }
        sb.AppendLine();

        sb.Append("Handles closed: ").AppendLine(result.HandlesClosed.ToString());

        sb.AppendLine("Terminated:");
        if (result.Terminated.Count == 0) sb.AppendLine("  (none)");
        foreach (var t in result.Terminated)
            sb.Append("  ").Append(t.ProcessName).Append(" (pid ").Append(t.Pid).AppendLine(")");

        sb.AppendLine("Skipped:");
        if (result.Skipped.Count == 0) sb.AppendLine("  (none)");
        foreach (var s in result.Skipped)
        {
            sb.Append("  ").Append(s.ProcessName).Append(" (pid ").Append(s.Pid).Append("): ")
              .AppendLine(s.Reason);
        }
        sb.AppendLine();

        sb.AppendLine("Results:");
        foreach (var p in result.Paths)
        {
            sb.Append("  [").Append(p.Outcome).Append("] ").Append(p.Path);
            if (p.Message is not null) sb.Append(" — ").Append(p.Message);
            sb.AppendLine();
        }

        if (scan.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Scan errors:");
            foreach (var e in scan.Errors) sb.Append("  ").AppendLine(e);
        }

        sb.AppendLine();
        sb.Append("Summary: ").Append(result.DeletedCount).Append(" deleted, ")
          .Append(result.UnlockedCount).Append(" unlocked, ")
          .Append(result.FailedCount).Append(" failed, ")
          .Append(result.RefusedCount).AppendLine(" refused");
        if (result.ElevationRequired) sb.AppendLine("Elevation was required.");

        Save(sb.ToString());
    }

    public void WriteError(string message)
    {
        Save($"thecleaner run {DateTimeOffset.Now:u}{Environment.NewLine}ERROR: {message}{Environment.NewLine}");
    }

    private void Save(string text)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Best-effort only.
        }
    }
}
