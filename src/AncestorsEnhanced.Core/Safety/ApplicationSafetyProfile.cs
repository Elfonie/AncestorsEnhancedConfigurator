namespace AncestorsEnhanced.Core.Safety;

public sealed record ApplicationSafetyProfile(
    bool GameFileWritesEnabled,
    bool NetworkAccessEnabled,
    bool TelemetryEnabled)
{
    public static ApplicationSafetyProfile Foundation { get; } = new(
        GameFileWritesEnabled: false,
        NetworkAccessEnabled: false,
        TelemetryEnabled: false);

    public bool IsReadOnly => !GameFileWritesEnabled;
}
