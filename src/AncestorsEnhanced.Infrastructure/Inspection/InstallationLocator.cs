using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class InstallationLocator
{
    private readonly SteamInstallationLocator _steam;
    private readonly EpicInstallationLocator _epic;
    private readonly GogInstallationLocator _gog;
    private readonly HeroicInstallationLocator _heroic;

    public InstallationLocator(IReadOnlyFileSystem fileSystem, IHostEnvironment environment)
    {
        var factory = new GameInstallationFactory(fileSystem);
        _steam = new SteamInstallationLocator(fileSystem, environment);
        _epic = new EpicInstallationLocator(fileSystem, environment, factory);
        _gog = new GogInstallationLocator(environment, factory);
        _heroic = new HeroicInstallationLocator(fileSystem, environment, factory);
    }

    public GameInstallationSnapshot? Find(List<InspectionNotice> notices)
    {
        // Deterministic preference instead of silently picking the first random hit:
        // Steam, then Epic, then GOG, then Heroic.
        List<GameInstallationSnapshot> installations = [];
        installations.AddRange(_steam.Find(notices));
        installations.AddRange(_epic.Find(notices));
        installations.AddRange(_gog.Find());
        installations.AddRange(_heroic.Find(notices));

        // Deduplicate identical installations reported by more than one store locator
        // or through different filesystem symlink aliases before choosing one.
        // The explicit store preference above wins for duplicate paths.
        List<GameInstallationSnapshot> unique = [];
        var seen = new HashSet<string>(PathComparer);
        foreach (GameInstallationSnapshot candidate in installations)
        {
            string canonicalInstall = ConfigurationFileOperations.ResolvePhysicalPath(candidate.InstallDirectory);
            if (seen.Add(canonicalInstall))
            {
                unique.Add(candidate with
                {
                    InstallDirectory = canonicalInstall,
                    LibraryRoot = ConfigurationFileOperations.ResolvePhysicalPath(candidate.LibraryRoot)
                });
            }
        }

        if (unique.Count == 0)
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "game.not-found",
                "Ancestors was not found. Steam, Epic, GOG and Heroic were checked."));
            return null;
        }

        if (unique.Count > 1)
        {
            // Multiple distinct installations are ambiguous: never pick and write to one
            // of them automatically. Fail closed so the user resolves the conflict
            // instead of guessing.
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "game.multiple-installations",
                "Multiple distinct Ancestors installations were detected. Resolve the duplicates before making changes."));
            return null;
        }

        return unique[0];
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
