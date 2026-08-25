using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Environment;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class GogInstallationLocator(
    IHostEnvironment environment,
    GameInstallationFactory factory)
{
    public IReadOnlyList<GameInstallationSnapshot> Find() =>
        environment.GetGogInstallCandidates()
            .Select(path => environment.Host == HostKind.Linux
                ? factory.CreateLinux(StoreKind.Gog, path, null, CompatibilityLayerKind.Proton)
                : factory.CreateWindows(StoreKind.Gog, path, null))
            .OfType<GameInstallationSnapshot>()
            .ToArray();
}
