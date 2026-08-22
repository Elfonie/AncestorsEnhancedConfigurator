using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

/// <summary>Ready-only game detection replay used by <see cref="GameContextVerifier"/> tests.</summary>
internal sealed class StubInspector(Func<GameInspectionSnapshot> factory) : IReadOnlyGameInspector
{
    private readonly Func<GameInspectionSnapshot> _factory = factory;
    public GameInspectionSnapshot Inspect() => _factory();
}

/// <summary>VerifiedGameContext and GameContextVerifier revalidation.</summary>
public sealed class GameContextVerifierTests
{
    [Fact]
    public void VerifyPassesWhenLiveStateStillMatchesTheVerifiedContext()
    {
        (GameInspectionSnapshot snapshot, GameInspectionSnapshot live) = Snapshots();
        VerifiedGameContext context = MustCreate(snapshot);
        var verifier = new GameContextVerifier(new StubInspector(() => live));

        Assert.True(verifier.Verify(context));
    }

    [Fact]
    public void VerifyFailsWhenInstallDirectoryChangedAfterPreview()
    {
        (GameInspectionSnapshot snapshot, GameInspectionSnapshot live) = Snapshots();
        live = live with
        {
            Installation = live.Installation! with { InstallDirectory = Path.Combine(Path.GetTempPath(), "elsewhere") },
        };
        VerifiedGameContext context = MustCreate(snapshot);

        Assert.False(new GameContextVerifier(new StubInspector(() => live)).Verify(context));
    }

    [Fact]
    public void VerifyFailsWhenUserDataDirectoryChangedAfterPreview()
    {
        (GameInspectionSnapshot snapshot, GameInspectionSnapshot live) = Snapshots();
        live = live with { UserDataDirectory = Path.Combine(Path.GetTempPath(), "different-userdata") };
        VerifiedGameContext context = MustCreate(snapshot);

        Assert.False(new GameContextVerifier(new StubInspector(() => live)).Verify(context));
    }

    [Fact]
    public void VerifyFailsWhenProtonLibraryRootChanged()
    {
        (GameInspectionSnapshot snapshot, GameInspectionSnapshot live) = Snapshots();
        live = live with
        {
            Installation = live.Installation! with
            {
                Host = HostKind.Linux,
                CompatibilityLayer = CompatibilityLayerKind.Proton,
                LibraryRoot = Path.Combine(Path.GetTempPath(), "other-library"),
            },
        };
        // The captured context came from a Windows/None snapshot; a Proton layout no
        // longer matches.
        VerifiedGameContext context = MustCreate(snapshot);

        Assert.False(new GameContextVerifier(new StubInspector(() => live)).Verify(context));
    }

    [Fact]
    public void RevalidateReturnsNullForANewContentSignatureReadError()
    {
        (GameInspectionSnapshot snapshot, GameInspectionSnapshot live) = Snapshots();
        live = live with { Installation = live.Installation! with { ContentSignatureReadFailed = true } };
        var verifier = new GameContextVerifier(new StubInspector(() => live));

        Assert.Null(verifier.Revalidate());
    }

    [Fact]
    public void ContextCannotBeCreatedFromAnUnsupportedSnapshot()
    {
        GameInspectionSnapshot snapshot = Snapshot("5495393", AncestorsEnhanced.Core.AncestorsGameProfile.SupportedContentSignature);
        VerifiedGameContext? context = VerifiedGameContext.TryCreateFromSnapshot(snapshot with
        {
            Installation = snapshot.Installation! with { ContentSignatureReadFailed = true },
        });

        Assert.Null(context);
    }

    private static VerifiedGameContext MustCreate(GameInspectionSnapshot snapshot) =>
        VerifiedGameContext.TryCreateFromSnapshot(snapshot)
        ?? throw new InvalidOperationException("Expected a verifiable snapshot.");

    private static (GameInspectionSnapshot Snapshot, GameInspectionSnapshot Live) Snapshots()
    {
        GameInspectionSnapshot snapshot = Snapshot("5495393", AncestorsEnhanced.Core.AncestorsGameProfile.SupportedContentSignature);
        return (snapshot, snapshot);
    }

    private static GameInspectionSnapshot Snapshot(string buildId, string? contentSignature)
    {
        string userData = Path.Combine(Path.GetTempPath(), "ae-test-userdata");
        return new GameInspectionSnapshot(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(
                StoreKind.Steam,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "C:\\library",
                "C:\\install",
                buildId,
                ExecutableExists: true,
                contentSignature),
            userData,
            [],
            null,
            [],
            []);
    }
}
