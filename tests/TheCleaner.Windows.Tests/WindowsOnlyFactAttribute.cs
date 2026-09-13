using System.Runtime.InteropServices;

namespace TheCleaner.Windows.Tests;

/// <summary>A fact that only runs on Windows — the backend is Win32-only, but the
/// solution must still build and test green on other hosts.</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "Windows-only test.";
    }
}
