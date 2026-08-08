namespace AncestorsEnhanced.Core.Inspection;

public sealed record GameInstallationSnapshot(
    StoreKind Store,
    HostKind Host,
    CompatibilityLayerKind CompatibilityLayer,
    string LibraryRoot,
    string InstallDirectory,
    string? BuildId,
    bool ExecutableExists,
    string? ContentSignature = null,
    bool ContentSignatureReadFailed = false);
