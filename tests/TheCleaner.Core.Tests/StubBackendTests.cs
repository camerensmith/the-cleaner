using TheCleaner.Core;
using TheCleaner.Linux;
using TheCleaner.Mac;

namespace TheCleaner.Core.Tests;

public class StubBackendTests
{
    public static TheoryData<ILockKiller, string> Stubs => new()
    {
        { new LinuxLockKiller(), "Linux" },
        { new MacLockKiller(), "macOS" }
    };

    [Theory]
    [MemberData(nameof(Stubs))]
    public async Task Find_returns_no_holders(ILockKiller killer, string _)
    {
        Assert.Empty(await killer.FindLockersAsync([@"/tmp/x"], CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Stubs))]
    public async Task Unlock_fails_every_path_with_a_not_implemented_message(
        ILockKiller killer, string platform)
    {
        var result = await killer.UnlockAsync(
            ["/tmp/a", "/tmp/b"], new UnlockOptions(DeleteAfterUnlock: true), CancellationToken.None);

        Assert.Equal(2, result.FailedCount);
        Assert.All(result.Paths, p => Assert.Contains("not implemented", p.Message!, StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Paths, p => Assert.Contains(platform, p.Message!, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Stubs))]
    public async Task Unlock_does_not_throw_for_an_empty_list(ILockKiller killer, string _)
    {
        var result = await killer.UnlockAsync([], new UnlockOptions(false), CancellationToken.None);
        Assert.Empty(result.Paths);
    }
}
