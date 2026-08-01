namespace AncestorsEnhanced.Core.Inspection;

public sealed record GameInstallationSnapshot(
    StoreKind Store,
    HostKind Host,
    CompatibilityLayerKind CompatibilityLayer,
    string StoreRoot,
    string LibraryRoot,
    string InstallDirectory,
    string ExecutablePath,
    string? BuildId,
    bool ExecutableExists,
    string? ContentSignature = null);
