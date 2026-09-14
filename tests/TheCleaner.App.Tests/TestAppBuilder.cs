using Avalonia;
using Avalonia.Headless;
using TheCleaner.AppTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace TheCleaner.AppTests;

/// <summary>Boots the real App in Avalonia's headless platform so window tests can
/// construct and drive MainWindow without a display.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TheCleaner.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
