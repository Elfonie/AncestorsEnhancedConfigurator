namespace AncestorsEnhanced.Core;

/// <summary>
/// Store-aware identity rule for a supported Ancestors installation.
/// </summary>
public static class GameIdentity
{
    public static bool IsSupported(
        Inspection.StoreKind store,
        string? buildId,
        string? contentSignature,
        bool contentSignatureReadFailed)
    {
        if (contentSignatureReadFailed)
        {
            return false;
        }

        bool contentOk = string.Equals(
            contentSignature,
            AncestorsGameProfile.SupportedContentSignature,
            StringComparison.Ordinal);

        if (store is Inspection.StoreKind.EpicGames or Inspection.StoreKind.Gog)
        {
            return contentOk;
        }

        if (store != Inspection.StoreKind.Steam)
        {
            return false;
        }

        bool buildPending = !string.IsNullOrWhiteSpace(buildId);
        bool contentPending = !string.IsNullOrWhiteSpace(contentSignature);
        bool buildOk = string.Equals(
            buildId,
            AncestorsGameProfile.SupportedSteamBuildId,
            StringComparison.Ordinal);
        return buildPending && contentPending
            ? buildOk && contentOk
            : buildOk || contentOk;
    }
}
