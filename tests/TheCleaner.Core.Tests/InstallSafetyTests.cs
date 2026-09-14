using TheCleaner.Core;

namespace TheCleaner.Core.Tests;

public class InstallSafetyTests
{
    private static InstallSafety Make() => new();

    // ---- registry keys --------------------------------------------------

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager")]
    [InlineData(@"HKEY_CLASSES_ROOT\.exe")]
    public void Refuses_system_registry_keys(string keyPath)
    {
        var verdict = Make().CheckRegistryKey(keyPath);
        Assert.False(verdict.Allowed);
        Assert.Contains("system registry key", verdict.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\MyApp")]
    [InlineData(@"HKEY_CURRENT_USER\SOFTWARE\AcmeCorp\MyApp")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MyApp")]
    public void Allows_third_party_registry_keys(string keyPath)
    {
        Assert.True(Make().CheckRegistryKey(keyPath).Allowed);
    }

    // ---- services -------------------------------------------------------

    [Theory]
    [InlineData("wuauserv")]
    [InlineData("WINMGMT")]   // case-insensitive
    [InlineData("eventlog")]
    [InlineData("windefend")]
    public void Refuses_system_services(string serviceName)
    {
        var verdict = Make().CheckService(serviceName);
        Assert.False(verdict.Allowed);
        Assert.Contains("system service", verdict.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("MyAppService")]
    [InlineData("AcmeCorp.Updater")]
    public void Allows_third_party_services(string serviceName)
    {
        Assert.True(Make().CheckService(serviceName).Allowed);
    }

    // ---- scheduled tasks ------------------------------------------------

    [Theory]
    [InlineData(@"\Microsoft\Windows\WindowsUpdate\Scheduled Start")]
    [InlineData(@"\Microsoft\Windows\Defrag\ScheduledDefrag")]
    public void Refuses_system_scheduled_tasks(string taskName)
    {
        var verdict = Make().CheckScheduledTask(taskName);
        Assert.False(verdict.Allowed);
        Assert.Contains("system scheduled task", verdict.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"\MyApp\DailyUpdate")]
    [InlineData(@"\AcmeCorp\Cleanup")]
    public void Allows_third_party_scheduled_tasks(string taskName)
    {
        Assert.True(Make().CheckScheduledTask(taskName).Allowed);
    }
}
