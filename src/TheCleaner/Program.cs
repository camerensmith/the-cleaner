using Avalonia;

namespace TheCleaner;

internal static class Program
{
    /// <summary>Targets and action handed to us by Explorer drag-drop or an elevated
    /// relaunch. Read by <see cref="App.OnFrameworkInitializationCompleted"/>.</summary>
    public static CommandLineArgs Startup { get; private set; } = CommandLineArgs.Parse([]);

    [STAThread]
    public static void Main(string[] args)
    {
        Startup = CommandLineArgs.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
