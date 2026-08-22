using System.Security.Cryptography;
using System.Text;

namespace AncestorsEnhanced.Core.Inspection;

/// <summary>
/// Immutable representation of a verified Ancestors game context. Instances are created
/// only from a snapshot that currently satisfies all editing guards. The
/// <see cref="ContextFingerprint"/> is a stable digest of the recognised evidence; it is
/// used to detect context drift before a mutation, but never replaces a live re-read of
/// the filesystem.
/// </summary>
public sealed record VerifiedGameContext(
    string InstallDirectory,
    string UserDataDirectory,
    StoreKind Store,
    HostKind Host,
    CompatibilityLayerKind CompatibilityLayer,
    string? LibraryRoot,
    string? BuildId,
    string? ContentSignature,
    bool ContentSignatureReadFailed)
{
    /// <summary>A stable digest over the canonical context fields.</summary>
    public string ContextFingerprint { get; } = ComputeFingerprint(
        InstallDirectory, UserDataDirectory, Store, Host, CompatibilityLayer,
        LibraryRoot, BuildId, ContentSignature, ContentSignatureReadFailed);

    private static string ComputeFingerprint(
        string installDirectory,
        string userDataDirectory,
        StoreKind store,
        HostKind host,
        CompatibilityLayerKind compatibilityLayer,
        string? libraryRoot,
        string? buildId,
        string? contentSignature,
        bool contentSignatureReadFailed)
    {
        // Hash the encoding of each field and nothing else: AppendData hashes the exact
        // UTF-8 bytes, avoiding the previous bug of hashing character count instead of
        // byte count. Fields are separated by a delimiter that cannot appear
        // inside a normal path, and path fields are canonicalised first.
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Write(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            sha.AppendData(bytes);
        }

        Write(Path.GetFullPath(installDirectory));
        Write("\u001f");
        Write(Path.GetFullPath(userDataDirectory));
        Write("\u001f");
        Write(store.ToString());
        Write("\u001f");
        Write(host.ToString());
        Write("\u001f");
        Write(compatibilityLayer.ToString());
        Write("\u001f");
        if (libraryRoot is not null)
        {
            Write(Path.GetFullPath(libraryRoot));
        }

        Write("\u001f");
        Write(buildId ?? string.Empty);
        Write("\u001f");
        Write(contentSignature ?? string.Empty);
        Write("\u001f");
        Write(contentSignatureReadFailed ? "failed" : "ok");
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>
    /// Creates a verified context only from a snapshot that currently satisfies all
    /// editing guards. Returns <c>null</c> (fail-closed) when the installation is
    /// missing, the identity is unsupported, a content-signature read failed, or the
    /// user-data directory is unknown. No hard-coded supported build
    /// ID is substituted for unrecognised real data.
    /// </summary>
    public static VerifiedGameContext? TryCreateFromSnapshot(GameInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Installation is not { } installation ||
            string.IsNullOrWhiteSpace(snapshot.UserDataDirectory) ||
            installation.ContentSignatureReadFailed ||
            !Core.Editing.EditableSettingsCatalog.IsVerifiedEditingTarget(snapshot))
        {
            return null;
        }

        return new VerifiedGameContext(
            installation.InstallDirectory,
            snapshot.UserDataDirectory!,
            installation.Store,
            installation.Host,
            installation.CompatibilityLayer,
            installation.LibraryRoot,
            installation.BuildId,
            installation.ContentSignature,
            installation.ContentSignatureReadFailed);
    }

    /// <summary>True when this context still matches the current snapshot.</summary>
    public bool Matches(GameInspectionSnapshot snapshot)
    {
        if (snapshot?.Installation is not { } installation ||
            string.IsNullOrWhiteSpace(snapshot.UserDataDirectory) ||
            installation.ContentSignatureReadFailed ||
            !Core.Editing.EditableSettingsCatalog.IsVerifiedEditingTarget(snapshot))
        {
            return false;
        }

        return string.Equals(Path.GetFullPath(InstallDirectory), Path.GetFullPath(installation.InstallDirectory), PathComparison)
            && string.Equals(Path.GetFullPath(UserDataDirectory), Path.GetFullPath(snapshot.UserDataDirectory), PathComparison)
            && Store == installation.Store
            && Host == installation.Host
            && CompatibilityLayer == installation.CompatibilityLayer
            // A build ID that was present must not silently disappear or change.
            && string.Equals(BuildId ?? string.Empty, installation.BuildId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(ContentSignature ?? string.Empty, installation.ContentSignature ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(
                Path.GetFullPath(LibraryRoot ?? string.Empty),
                Path.GetFullPath(installation.LibraryRoot ?? string.Empty),
                PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
