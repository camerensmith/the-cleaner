using System.Text;

namespace TheCleaner.Windows;

/// <summary>Reads the local file-system target from a Windows Shell Link (.lnk) file
/// without requiring COM activation, by parsing the binary format directly.</summary>
internal static class ShortcutResolver
{
    private const uint HeaderSize = 0x4C;
    private const uint FlagHasLinkTargetIDList = 0x01;
    private const uint FlagHasLinkInfo = 0x02;
    private const uint FlagIsUnicode = 0x80000;
    private const uint LinkInfoFlagVolumeIdAndLocalBasePath = 0x01;
    private const uint LinkInfoUnicodeOffsetField = 0x20; // offset to LocalBasePathOffsetUnicode
    private const uint LinkInfoHdrSizeForUnicode = 0x24;

    /// <summary>Attempts to read the local target path from a .lnk file.
    /// Returns <c>false</c> (with <paramref name="target"/> set to empty) on any error.</summary>
    internal static bool TryResolve(string lnkPath, out string target)
    {
        target = string.Empty;
        try
        {
            var bytes = File.ReadAllBytes(lnkPath);
            return TryParseBytes(bytes, out target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool TryParseBytes(byte[] bytes, out string target)
    {
        target = string.Empty;

        if (bytes.Length < (int)HeaderSize) return false;
        // Header size field must be 0x4C for a valid shell link.
        if (BitConverter.ToUInt32(bytes, 0) != HeaderSize) return false;

        var linkFlags = BitConverter.ToUInt32(bytes, 0x14);
        var isUnicode = (linkFlags & FlagIsUnicode) != 0;

        var pos = (int)HeaderSize;

        // Skip the optional IDList.
        if ((linkFlags & FlagHasLinkTargetIDList) != 0)
        {
            if (pos + 2 > bytes.Length) return false;
            var idListSize = BitConverter.ToUInt16(bytes, pos);
            pos += 2 + idListSize;
        }

        // Parse the LinkInfo structure to extract the local path.
        if ((linkFlags & FlagHasLinkInfo) == 0 || pos + 28 > bytes.Length)
            return false;

        var linkInfoStart = pos;
        var linkInfoHdrSize = BitConverter.ToUInt32(bytes, pos + 4);
        var linkInfoFlags = BitConverter.ToUInt32(bytes, pos + 8);

        if ((linkInfoFlags & LinkInfoFlagVolumeIdAndLocalBasePath) == 0)
            return false;

        // Prefer the Unicode local path when available.
        if (isUnicode
            && linkInfoHdrSize >= LinkInfoHdrSizeForUnicode
            && pos + (int)LinkInfoHdrSizeForUnicode <= bytes.Length)
        {
            var uOffset = BitConverter.ToInt32(bytes, pos + (int)LinkInfoUnicodeOffsetField);
            if (uOffset > 0)
            {
                target = ReadNullTerminatedUnicode(bytes, linkInfoStart + uOffset);
                if (!string.IsNullOrEmpty(target)) return true;
            }
        }

        // Fall back to ANSI local path.
        var ansiOffset = BitConverter.ToInt32(bytes, pos + 0x10);
        if (ansiOffset > 0)
        {
            target = ReadNullTerminatedAnsi(bytes, linkInfoStart + ansiOffset);
            return !string.IsNullOrEmpty(target);
        }

        return false;
    }

    private static string ReadNullTerminatedUnicode(byte[] bytes, int offset)
    {
        if (offset < 0 || offset >= bytes.Length) return string.Empty;
        var end = offset;
        while (end + 1 < bytes.Length && (bytes[end] != 0 || bytes[end + 1] != 0))
            end += 2;
        return Encoding.Unicode.GetString(bytes, offset, end - offset);
    }

    private static string ReadNullTerminatedAnsi(byte[] bytes, int offset)
    {
        if (offset < 0 || offset >= bytes.Length) return string.Empty;
        var end = offset;
        while (end < bytes.Length && bytes[end] != 0) end++;
        return Encoding.Default.GetString(bytes, offset, end - offset);
    }
}
