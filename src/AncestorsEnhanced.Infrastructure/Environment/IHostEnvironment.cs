namespace AncestorsEnhanced.Infrastructure.Environment;

internal interface IHostEnvironment
{
    bool IsWindows { get; }

    string? LocalApplicationDataPath { get; }

    DateTimeOffset UtcNow { get; }

    IReadOnlyList<string> GetSteamRootCandidates();
}
