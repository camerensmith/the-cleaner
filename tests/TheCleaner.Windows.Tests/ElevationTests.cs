namespace TheCleaner.Windows.Tests;

public class ElevationTests
{
    [WindowsOnlyFact]
    public void IsElevated_answers_without_throwing()
    {
        var ex = Record.Exception(() => _ = Elevation.IsElevated);
        Assert.Null(ex);
    }
}
