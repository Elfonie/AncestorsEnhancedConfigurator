using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.Paks;

namespace AncestorsEnhanced.Infrastructure.Inspection;

public sealed class ReadOnlyAncestorsInspector : IReadOnlyGameInspector
{
    private readonly IHostEnvironment _environment;
    private readonly InstallationLocator _installations;
    private readonly UserDataLocator _userData;
    private readonly IniFileInspector _iniFiles;
    private readonly SystemSaveInspector _systemSave;
    private readonly PakFileInspector _pakFiles;

    internal ReadOnlyAncestorsInspector(
        IReadOnlyFileSystem fileSystem,
        IHostEnvironment environment)
    {
        _environment = environment;
        _installations = new InstallationLocator(fileSystem, environment);
        _userData = new UserDataLocator(fileSystem, environment);
        _iniFiles = new IniFileInspector(fileSystem);
        _systemSave = new SystemSaveInspector(fileSystem);
        _pakFiles = new PakFileInspector(fileSystem);
    }

    public static ReadOnlyAncestorsInspector CreateDefault() =>
        new(new PhysicalReadOnlyFileSystem(), CreateHostEnvironment());

    public GameInspectionSnapshot Inspect()
    {
        List<InspectionNotice> notices = [];
        GameInstallationSnapshot? installation = _installations.Find(notices);
        string? userDataDirectory = _userData.Find(installation, notices);
        return new GameInspectionSnapshot(
            _environment.UtcNow,
            installation,
            userDataDirectory,
            _iniFiles.Read(userDataDirectory, notices),
            _systemSave.Read(userDataDirectory),
            _pakFiles.Read(installation, notices),
            notices,
            installation is null
                ? null
                : VignettePakEditor.Inspect(installation.InstallDirectory));
    }

    private static IHostEnvironment CreateHostEnvironment()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsHostEnvironment();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxHostEnvironment();
        }

        throw new PlatformNotSupportedException("Windows and Linux are supported.");
    }
}
