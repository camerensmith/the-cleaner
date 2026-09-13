using System.Diagnostics;
using TheCleaner.Core;

namespace TheCleaner.Windows.Tests;

public class ProcessTerminatorTests
{
    private static LockHolder Holder(int pid, string name) =>
        new(pid, name, @"C:\irrelevant", LockHolderSource.HandleScan);

    [WindowsOnlyFact]
    public void Terminates_a_real_child_process()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c pause")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        })!;

        try
        {
            var report = new ProcessTerminator().Terminate([Holder(child.Id, "cmd")]);

            Assert.Contains(report.Terminated, t => t.Pid == child.Id);
            Assert.True(child.WaitForExit(10_000), "child did not exit");
            Assert.False(report.AccessDenied);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }

    [WindowsOnlyFact]
    public void Never_terminates_our_own_process()
    {
        var report = new ProcessTerminator().Terminate([Holder(Environment.ProcessId, "testhost")]);

        Assert.Empty(report.Terminated);
        Assert.Contains(report.Skipped, s => s.Pid == Environment.ProcessId);
        Assert.Contains("own process", report.Skipped[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsOnlyFact]
    public void Skips_protected_system_processes_by_name()
    {
        var report = new ProcessTerminator().Terminate([Holder(999_001, "lsass")]);

        Assert.Empty(report.Terminated);
        Assert.Contains(report.Skipped, s => s.ProcessName == "lsass");
        Assert.Contains("protected", report.Skipped[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsOnlyFact]
    public void Skips_the_system_pids()
    {
        var report = new ProcessTerminator().Terminate([Holder(4, "System"), Holder(0, "Idle")]);

        Assert.Empty(report.Terminated);
        Assert.Equal(2, report.Skipped.Count);
    }

    [WindowsOnlyFact]
    public void Deduplicates_repeated_pids()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c pause")
        {
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
        })!;

        try
        {
            var report = new ProcessTerminator().Terminate(
                [Holder(child.Id, "cmd"), Holder(child.Id, "cmd")]);

            Assert.Single(report.Terminated);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }

    [WindowsOnlyFact]
    public void An_already_dead_pid_is_skipped_not_thrown()
    {
        var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
        {
            UseShellExecute = false, CreateNoWindow = true
        })!;
        child.WaitForExit();
        var deadPid = child.Id;
        child.Dispose();

        var report = new ProcessTerminator().Terminate([Holder(deadPid, "cmd")]);

        Assert.Empty(report.Terminated);
        Assert.Single(report.Skipped);
    }

    [WindowsOnlyFact]
    public void Protected_names_cover_the_documented_set()
    {
        foreach (var name in new[] { "csrss", "smss", "wininit", "services", "lsass", "System", "winlogon" })
            Assert.Contains(name, ProcessTerminator.ProtectedNames);
    }
}
