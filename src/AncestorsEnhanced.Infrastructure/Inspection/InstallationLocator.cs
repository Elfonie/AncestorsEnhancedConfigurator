using AncestorsEnhanced.Core.Inspection;
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

        if (installations.Count == 0)
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "game.not-found",
                "Ancestors was not found. Steam, Epic, GOG and Heroic were checked."));
            return null;
        }

        if (installations.Count > 1)
        {
            GameInstallationSnapshot selected = installations[0];
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "game.multiple-installations",
                $"Multiple Ancestors installations were detected. Using {selected.Store} at {selected.InstallDirectory}."));
        }

        return installations[0];
    }
}
