using System.Runtime.Versioning;

// Every test in here is a [WindowsOnlyFact] that skips off-Windows, so the assembly
// carries the same platform declaration as the backend it exercises.
[assembly: SupportedOSPlatform("windows")]
