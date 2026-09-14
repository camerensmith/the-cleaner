using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class ResultsTests
{
    [Fact]
    public void KillResult_counts_outcomes_by_category()
    {
        var result = new KillResult(
            Paths:
            [
                new PathResult(@"C:\a.txt", PathOutcome.Deleted),
                new PathResult(@"C:\b.txt", PathOutcome.Deleted),
                new PathResult(@"C:\c.txt", PathOutcome.Unlocked),
                new PathResult(@"C:\d.txt", PathOutcome.Failed, "still locked"),
                new PathResult(@"C:\", PathOutcome.Refused, "volume root")
            ],
            Terminated: [new TerminatedProcess(123, "notepad")],
            Skipped: [new SkippedProcess(4, "System", "protected process")],
            HandlesClosed: 2,
            ElevationRequired: false);

        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(1, result.UnlockedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RefusedCount);
        Assert.True(result.AnyFailures);
    }

    [Fact]
    public void KillResult_with_no_failures_reports_none()
    {
        var result = new KillResult(
            Paths: [new PathResult(@"C:\a.txt", PathOutcome.Deleted)],
            Terminated: [],
            Skipped: [],
            HandlesClosed: 0,
            ElevationRequired: false);

        Assert.False(result.AnyFailures);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public void UnlockOptions_terminates_lockers_by_default()
    {
        var options = new UnlockOptions(DeleteAfterUnlock: true);
        Assert.True(options.TerminateLockers);
    }
}
