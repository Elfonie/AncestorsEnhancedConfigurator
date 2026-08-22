using AncestorsEnhanced.Core.Inspection;
using Xunit;

namespace AncestorsEnhanced.Core.Tests;

/// <summary>VerifiedGameContext fingerprint stability, distinctness and matching semantics.</summary>
public sealed class VerifiedGameContextTests
{
    [Fact]
    public void AsciiPathsProduceDistinctFingerprints()
    {
        VerifiedGameContext first = Context("C:\\game\\one", "C:\\user\\one");
        VerifiedGameContext second = Context("C:\\game\\two", "C:\\user\\one");

        Assert.NotEqual(first.ContextFingerprint, second.ContextFingerprint);
    }

    [Fact]
    public void UnicodePathsProduceDistinctFingerprintsFromAsciiAndFromEachOther()
    {
        VerifiedGameContext ascii = Context("C:\\game", "C:\\user");
        VerifiedGameContext umlaut = Context("C:\\spiele\\gr\x00f6\x00dfe\\\x00e4", "C:\\ben\x00fctzer");
        VerifiedGameContext autre = Context("C:\\spiele\\gr\x00f6\x00dfe\\\x00e4x", "C:\\ben\x00fctzer");

        // The byte length of the UTF-8 encoding (not the character count) is hashed, so
        // multi-byte characters must still produce distinct hashes.
        Assert.NotEqual(ascii.ContextFingerprint, umlaut.ContextFingerprint);
        Assert.NotEqual(umlaut.ContextFingerprint, autre.ContextFingerprint);
    }

    [Fact]
    public void TwoUnicodePathsDifferingOnlyAtTheEndAreDistinct()
    {
        VerifiedGameContext a = Context("C:\\spiele\\ordner\\name", "C:\\user");
        VerifiedGameContext b = Context("C:\\spiele\\ordner\\name2", "C:\\user");

        Assert.NotEqual(a.ContextFingerprint, b.ContextFingerprint);
    }

    [Fact]
    public void SameContextYieldsTheSameFingerprint()
    {
        VerifiedGameContext a = Context("C:\\game", "C:\\user");
        VerifiedGameContext b = Context("C:\\game", "C:\\user");

        Assert.Equal(a.ContextFingerprint, b.ContextFingerprint);
    }

    [Fact]
    public void MatchesRejectsABuildIdDrift()
    {
        (VerifiedGameContext context, GameInspectionSnapshot live) = SnapshotPair();
        VerifiedGameContext captured = context;

        // The live re-inspection reports a different build ID than the one captured.
        GameInspectionSnapshot drifted = live with
        {
            Installation = live.Installation! with { BuildId = "9999999" },
        };

        Assert.True(captured.Matches(live));
        Assert.False(captured.Matches(drifted));
    }

    private static (VerifiedGameContext Context, GameInspectionSnapshot Snapshot) SnapshotPair()
    {
        GameInstallationSnapshot installation = new(
            StoreKind.Steam,
            HostKind.Windows,
            CompatibilityLayerKind.None,
            "C:\\library",
            "C:\\install",
            AncestorsEnhanced.Core.AncestorsGameProfile.SupportedSteamBuildId,
            ExecutableExists: true,
            null);
        GameInspectionSnapshot snapshot = new(
            DateTimeOffset.UnixEpoch,
            installation,
            "C:\\user",
            [],
            null,
            [],
            []);
        return (VerifiedGameContext.TryCreateFromSnapshot(snapshot)!, snapshot);
    }

    private static VerifiedGameContext Context(string install, string user) =>
        new(
            install,
            user,
            StoreKind.Steam,
            HostKind.Windows,
            CompatibilityLayerKind.None,
            "C:\\library",
            AncestorsEnhanced.Core.AncestorsGameProfile.SupportedSteamBuildId,
            null,
            false);
}
