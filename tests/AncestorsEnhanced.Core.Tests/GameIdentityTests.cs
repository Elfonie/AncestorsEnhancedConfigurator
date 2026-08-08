using AncestorsEnhanced.Core;
using Xunit;

namespace AncestorsEnhanced.Core.Tests;

/// <summary>Identity rule for a supported install (F061/F063).</summary>
public sealed class GameIdentityTests
{
    private const string OkBuild = AncestorsGameProfile.SupportedBuildId;
    private const string OkSignature = AncestorsGameProfile.SupportedContentSignature;

    [Fact]
    public void MatchingBuildIdWithoutSignatureIsSupported()
    {
        Assert.True(GameIdentity.IsSupported(OkBuild, null, contentSignatureReadFailed: false));
    }

    [Fact]
    public void CorrectBuildIdWithWrongSignatureIsRejected()
    {
        Assert.False(GameIdentity.IsSupported(OkBuild, "PAK5:WRONG:WRONG", contentSignatureReadFailed: false));
    }

    [Fact]
    public void WrongBuildIdWithCorrectSignatureIsRejected()
    {
        Assert.False(GameIdentity.IsSupported("0000000", OkSignature, contentSignatureReadFailed: false));
    }

    [Fact]
    public void BothWrongIsRejected()
    {
        Assert.False(GameIdentity.IsSupported("0000000", "PAK5:WRONG:WRONG", contentSignatureReadFailed: false));
    }

    [Fact]
    public void SupportedSignatureWithoutBuildIdIsAccepted()
    {
        Assert.True(GameIdentity.IsSupported(null, OkSignature, contentSignatureReadFailed: false));
    }

    [Fact]
    public void ContentSignatureReadErrorIsAlwaysFailClosed()
    {
        // A content-signature read error is never equivalent to "this platform has no
        // signature" (F063), even when the build ID alone would otherwise match.
        Assert.False(GameIdentity.IsSupported(OkBuild, null, contentSignatureReadFailed: true));
        Assert.False(GameIdentity.IsSupported(OkBuild, OkSignature, contentSignatureReadFailed: true));
    }
}
