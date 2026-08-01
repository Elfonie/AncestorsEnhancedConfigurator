namespace AncestorsEnhanced.Infrastructure.Environment;

using AncestorsEnhanced.Core.Inspection;

internal interface IHostEnvironment
{
    HostKind Host { get; }

    string? LocalApplicationDataPath { get; }

    DateTimeOffset UtcNow { get; }

    IReadOnlyList<string> GetSteamRootCandidates();

    IReadOnlyList<string> GetEpicManifestDirectories();

    IReadOnlyList<string> GetGogInstallCandidates();
}
