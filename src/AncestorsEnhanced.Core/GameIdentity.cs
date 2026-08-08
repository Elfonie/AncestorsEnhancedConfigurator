namespace AncestorsEnhanced.Core;

/// <summary>
/// Single authoritative identity rule for a supported Ancestors installation
/// (F061/F063). A recognised build ID and a recognised content signature are
/// independent evidence: when both are present they must both match, otherwise
/// contradictory evidence is fail-closed. When exactly one is legitimately
/// available for the platform, that single source of evidence is enough. A
/// CONTENT-SIGNATURE READ ERROR is never treated as "no signature on this
/// platform": it fails closed so a transient IO problem cannot silently widen
/// which installations are considered editable.
/// </summary>
public static class GameIdentity
{
    public static bool IsSupported(
        string? buildId,
        string? contentSignature,
        bool contentSignatureReadFailed)
    {
        if (contentSignatureReadFailed)
        {
            return false;
        }

        bool buildPending = !string.IsNullOrWhiteSpace(buildId);
        bool contentPending = !string.IsNullOrWhiteSpace(contentSignature);
        bool buildOk = string.Equals(
            buildId,
            AncestorsGameProfile.SupportedBuildId,
            StringComparison.Ordinal);
        bool contentOk = string.Equals(
            contentSignature,
            AncestorsGameProfile.SupportedContentSignature,
            StringComparison.Ordinal);

        if (buildPending && contentPending)
        {
            return buildOk && contentOk;
        }

        return buildOk || contentOk;
    }
}
