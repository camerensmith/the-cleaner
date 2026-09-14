using TheCleaner.Windows;

namespace TheCleaner.Windows.Tests;

public class ShortcutResolverTests
{
    [Fact]
    public void Returns_false_for_empty_byte_array()
    {
        Assert.False(ShortcutResolver.TryParseBytes([], out _));
    }

    [Fact]
    public void Returns_false_for_too_short_data()
    {
        Assert.False(ShortcutResolver.TryParseBytes(new byte[10], out _));
    }

    [Fact]
    public void Returns_false_when_header_magic_is_wrong()
    {
        var bytes = new byte[0x4C];
        // Header size field at offset 0 defaults to 0 — wrong magic.
        Assert.False(ShortcutResolver.TryParseBytes(bytes, out _));
    }

    [Fact]
    public void Returns_false_for_link_without_LinkInfo_flag()
    {
        var bytes = BuildMinimalHeader(hasLinkInfo: false, hasIdList: false, isUnicode: false);
        Assert.False(ShortcutResolver.TryParseBytes(bytes, out _));
    }

    [WindowsOnlyFact]
    public void TryResolve_returns_false_for_nonexistent_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".lnk");
        Assert.False(ShortcutResolver.TryResolve(path, out var target));
        Assert.Empty(target);
    }

    /// <summary>Builds the minimum 0x4C-byte shell link header with the specified flags,
    /// without any trailing LinkInfo or IDList data (so the test is purely header-level).</summary>
    private static byte[] BuildMinimalHeader(
        bool hasLinkInfo, bool hasIdList, bool isUnicode)
    {
        var bytes = new byte[0x4C];
        // Header size field at offset 0.
        BitConverter.TryWriteBytes(bytes.AsSpan(0), (uint)0x4C);

        uint flags = 0;
        if (hasIdList) flags |= 0x01;
        if (hasLinkInfo) flags |= 0x02;
        if (isUnicode) flags |= 0x80000;
        BitConverter.TryWriteBytes(bytes.AsSpan(0x14), flags);

        return bytes;
    }
}
