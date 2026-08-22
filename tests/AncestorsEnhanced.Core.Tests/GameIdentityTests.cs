using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Inspection;
using Xunit;

namespace AncestorsEnhanced.Core.Tests;

/// <summary>Identity rules for a supported installation.</summary>
public sealed class GameIdentityTests
{
    private const string OkBuild = AncestorsGameProfile.SupportedSteamBuildId;
    private const string OkSignature = AncestorsGameProfile.SupportedContentSignature;

    [Fact]
    public void MatchingBuildIdWithoutSignatureIsSupported()
    {
        Assert.True(GameIdentity.IsSupported(StoreKind.Steam, OkBuild, null, contentSignatureReadFailed: false));
    }

    [Fact]
    public void CorrectBuildIdWithWrongSignatureIsRejected()
    {
        Assert.False(GameIdentity.IsSupported(StoreKind.Steam, OkBuild, "PAK5:WRONG:WRONG", contentSignatureReadFailed: false));
    }

    [Fact]
    public void WrongBuildIdWithCorrectSignatureIsRejected()
    {
        Assert.False(GameIdentity.IsSupported(StoreKind.Steam, "0000000", OkSignature, contentSignatureReadFailed: false));
    }

    [Fact]
    public void BothWrongIsRejected()
    {
        Assert.False(GameIdentity.IsSupported(StoreKind.Steam, "0000000", "PAK5:WRONG:WRONG", contentSignatureReadFailed: false));
    }

    [Fact]
    public void SupportedSignatureWithoutBuildIdIsAccepted()
    {
        Assert.True(GameIdentity.IsSupported(StoreKind.Steam, null, OkSignature, contentSignatureReadFailed: false));
    }

    [Fact]
    public void EpicUsesContentSignatureInsteadOfSteamBuildId()
    {
        Assert.True(GameIdentity.IsSupported(
            StoreKind.EpicGames, "epic-build-version", OkSignature, contentSignatureReadFailed: false));
        Assert.False(GameIdentity.IsSupported(
            StoreKind.EpicGames, OkBuild, "PAK5:WRONG:WRONG", contentSignatureReadFailed: false));
    }

    [Fact]
    public void GogUsesVerifiedContentSignature()
    {
        Assert.True(GameIdentity.IsSupported(
            StoreKind.Gog, null, OkSignature, contentSignatureReadFailed: false));
        Assert.False(GameIdentity.IsSupported(
            StoreKind.Gog, OkBuild, null, contentSignatureReadFailed: false));
    }

    [Fact]
    public void ContentSignatureReadErrorIsAlwaysFailClosed()
    {
        // A content-signature read error is never equivalent to "this platform has no
        // signature", even when the build ID alone would otherwise match.
        Assert.False(GameIdentity.IsSupported(StoreKind.Steam, OkBuild, null, contentSignatureReadFailed: true));
        Assert.False(GameIdentity.IsSupported(StoreKind.EpicGames, "epic", OkSignature, contentSignatureReadFailed: true));
    }
}
