namespace TheCleaner.Core;

/// <summary>Summary returned after a deep uninstall run.</summary>
public sealed record DeepUninstallResult(
    int FilesDeleted,
    int FilesFailed,
    int RegistryKeysDeleted,
    int RegistryKeysFailed,
    int ServicesDeleted,
    int ServicesFailed,
    int TasksDeleted,
    int TasksFailed,
    IReadOnlyList<string> Errors)
{
    public bool AnyFailures =>
        FilesFailed > 0 || RegistryKeysFailed > 0 || ServicesFailed > 0 || TasksFailed > 0;

    public static DeepUninstallResult Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, []);
}
