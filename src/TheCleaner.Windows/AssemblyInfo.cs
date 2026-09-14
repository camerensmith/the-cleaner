using System.Runtime.Versioning;

// This assembly is Win32 top to bottom. The target framework stays platform-neutral
// (net8.0) so the solution builds and tests on any host, and this attribute tells the
// platform-compatibility analyzer the truth: nothing in here is meant to run elsewhere.
// The app only links this project when the publish RID is a Windows one.
[assembly: SupportedOSPlatform("windows")]
