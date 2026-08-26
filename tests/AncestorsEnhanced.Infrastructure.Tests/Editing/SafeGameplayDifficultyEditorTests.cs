using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.Paks;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

public sealed class SafeGameplayDifficultyEditorTests
{
    [Fact]
    public void ApplyUpdateDetectAndResetUseTheReviewedTransactionLifecycle()
    {
        using var fixture = new GameplayFixture();
        SafeGameplayDifficultyEditor editor = fixture.CreateEditor();
        GameplayDifficultySettings survival = new(130, 130, 130, 130, 130, 130, 130, 130, 130, 130, 90, 130, 120, 80, 70);

        SettingsChangePlan install = editor.CreatePlan(fixture.Snapshot, survival);

        Assert.Equal(15, install.Changes.Count);
        Assert.Equal(2, install.Files.Count);
        Assert.True(editor.Apply(install).Succeeded);
        GameplayDifficultyState active = editor.Inspect(fixture.Snapshot);
        Assert.Equal(GameplayDifficultyStateKind.Active, active.Kind);
        Assert.Equal(survival, active.Settings);

        GameplayDifficultySettings custom = new(70, 90, 110, 150, 120, 80, 130, 140, 60, 110);
        SettingsChangePlan update = editor.CreatePlan(fixture.Snapshot, custom);
        Assert.True(editor.Apply(update).Succeeded);
        Assert.Equal(custom, editor.Inspect(fixture.Snapshot).Settings);

        SettingsChangePlan reset = editor.CreatePlan(fixture.Snapshot, GameplayDifficultySettings.GameDefault);
        Assert.All(reset.Files, file => Assert.False(file.ResultExists));
        Assert.True(editor.Apply(reset).Succeeded);
        Assert.Equal(GameplayDifficultyStateKind.GameDefault, editor.Inspect(fixture.Snapshot).Kind);
        Assert.False(File.Exists(fixture.PakPath));
        Assert.False(File.Exists(fixture.MarkerPath));
    }

    [Fact]
    public void AChangedOwnershipMarkerAbortsBeforeThePakIsUpdated()
    {
        using var fixture = new GameplayFixture();
        SafeGameplayDifficultyEditor editor = fixture.CreateEditor();
        Assert.True(editor.Apply(editor.CreatePlan(fixture.Snapshot, new(130, 130, 130, 130))).Succeeded);
        byte[] installed = File.ReadAllBytes(fixture.PakPath);

        SettingsChangePlan update = editor.CreatePlan(fixture.Snapshot, new(140, 140, 140, 140));
        File.AppendAllText(fixture.MarkerPath, "changed");

        SettingsOperationResult result = editor.Apply(update);

        Assert.False(result.Succeeded);
        Assert.Equal(installed, File.ReadAllBytes(fixture.PakPath));
    }

    [Fact]
    public void AModifiedGameplayPakIsNeverOverwrittenOrRemoved()
    {
        using var fixture = new GameplayFixture();
        SafeGameplayDifficultyEditor editor = fixture.CreateEditor();
        Assert.True(editor.Apply(editor.CreatePlan(fixture.Snapshot, new(130, 130, 130, 130))).Succeeded);
        File.AppendAllText(fixture.PakPath, "foreign bytes");

        GameplayDifficultyState state = editor.Inspect(fixture.Snapshot);

        Assert.Equal(GameplayDifficultyStateKind.Unverified, state.Kind);
        Assert.Throws<InvalidOperationException>(() =>
            editor.CreatePlan(fixture.Snapshot, GameplayDifficultySettings.GameDefault));
        Assert.True(File.Exists(fixture.PakPath));
    }

    [Fact]
    public void CatalogMapsTheVerifiedGameplayValuesToExactOffsetsAndScaledFloats()
    {
        IReadOnlyList<GameplayAssetPatch> patches = GameplayDifficultyPatchCatalog.Create(130, 70, 110, 150, 120, 80, 140, 130, 60, 110, 90, 130, 120, 80, 70);

        Assert.Equal(23, patches.Count);
        Assert.Collection(
            patches,
            patch => AssertFloatPatch(patch, "food", 1795, 24f, 31.2f),
            patch => AssertFloatPatch(patch, "water", 1968, 30f, 21f),
            patch => AssertFloatPatch(patch, "sleep", 2170, 16f, 17.6f),
            patch => AssertFloatPatch(patch, "minor-fall", 1621, .025f, .0375f),
            patch => AssertFloatPatch(patch, "major-fall", 1699, .05f, .075f),
            patch => AssertFloatPatch(patch, "minor-bleed", 2466, .01f, .012f),
            patch => AssertFloatPatch(patch, "major-bleed", 2610, .02f, .024f),
            patch => AssertFloatPatch(patch, "minor-poison", 2447, .01f, .008f),
            patch => AssertFloatPatch(patch, "major-poison", 2678, .02f, .016f),
            patch => AssertFloatPatch(patch, "energy-recovery", 1887, 1f, 1.4f),
            patch => AssertFloatPatch(patch, "minor-wound-sleep-healing", 2404, 10f, 13f),
            patch => AssertFloatPatch(patch, "major-wound-sleep-healing", 2606, 10f, 13f),
            patch => AssertFloatPatch(patch, "minor-wound-stamina-penalty", 2375, .15f, .09f),
            patch => AssertFloatPatch(patch, "major-wound-stamina-penalty", 2577, .30f, .18f),
            patch => AssertFloatPatch(patch, "minor-poison-sleep-healing", 2389, 10f, 11f),
            patch => AssertFloatPatch(patch, "major-poison-sleep-healing", 2591, 10f, 11f),
            patch => AssertFloatPatch(patch, "minor-poison-liquid-healing", 2418, 15f, 16.5f),
            patch => AssertFloatPatch(patch, "major-poison-liquid-healing", 2620, 15f, 16.5f),
            patch => AssertFloatPatch(patch, "rest-delay", 1945, 1.5f, 1.35f),
            patch => AssertFloatPatch(patch, "exhaustion-threshold", 1916, .5f, .65f),
            patch => AssertFloatPatch(patch, "exhaustion-penalty", 1974, .15f, .18f),
            patch => AssertFloatPatch(patch, "wound-recovery-duration", 2519, 480f, 384f),
            patch => AssertFloatPatch(patch, "poison-stamina-penalty", 2649, .25f, .175f));
    }

    [Fact]
    public void CatalogSupportsTheExperimentalDifficultyBounds()
    {
        IReadOnlyList<GameplayAssetPatch> patches = GameplayDifficultyPatchCatalog.Create(10, 1000, 10, 1000, 10, 1000, 10, 1000, 10, 10, 10, 1000, 10, 1000, 10);

        Assert.Collection(
            patches,
            patch => AssertFloatPatch(patch, "food", 1795, 24f, 2.4f),
            patch => AssertFloatPatch(patch, "water", 1968, 30f, 300f),
            patch => AssertFloatPatch(patch, "sleep", 2170, 16f, 1.6f),
            patch => AssertFloatPatch(patch, "minor-fall", 1621, .025f, .25f),
            patch => AssertFloatPatch(patch, "major-fall", 1699, .05f, .5f),
            patch => AssertFloatPatch(patch, "minor-bleed", 2466, .01f, .001f),
            patch => AssertFloatPatch(patch, "major-bleed", 2610, .02f, .002f),
            patch => AssertFloatPatch(patch, "minor-poison", 2447, .01f, .1f),
            patch => AssertFloatPatch(patch, "major-poison", 2678, .02f, .2f),
            patch => AssertFloatPatch(patch, "energy-recovery", 1887, 1f, .1f),
            patch => AssertFloatPatch(patch, "minor-wound-sleep-healing", 2404, 10f, 100f),
            patch => AssertFloatPatch(patch, "major-wound-sleep-healing", 2606, 10f, 100f),
            patch => AssertFloatPatch(patch, "minor-wound-stamina-penalty", 2375, .15f, .015f),
            patch => AssertFloatPatch(patch, "major-wound-stamina-penalty", 2577, .30f, .03f),
            patch => AssertFloatPatch(patch, "minor-poison-sleep-healing", 2389, 10f, 1f),
            patch => AssertFloatPatch(patch, "major-poison-sleep-healing", 2591, 10f, 1f),
            patch => AssertFloatPatch(patch, "minor-poison-liquid-healing", 2418, 15f, 1.5f),
            patch => AssertFloatPatch(patch, "major-poison-liquid-healing", 2620, 15f, 1.5f),
            patch => AssertFloatPatch(patch, "rest-delay", 1945, 1.5f, .15f),
            patch => AssertFloatPatch(patch, "exhaustion-threshold", 1916, .5f, 5f),
            patch => AssertFloatPatch(patch, "exhaustion-penalty", 1974, .15f, .015f),
            patch => AssertFloatPatch(patch, "wound-recovery-duration", 2519, 480f, 4800f),
            patch => AssertFloatPatch(patch, "poison-stamina-penalty", 2649, .25f, .025f));
    }

    [Fact]
    public void GameplayLifecycleUsesTheDetectedProtonLibraryPathsOnLinux()
    {
        using var fixture = new GameplayFixture(proton: true);
        SafeGameplayDifficultyEditor editor = fixture.CreateEditor();
        GameplayDifficultySettings explorer = new(70, 70, 70, 70);

        Assert.True(editor.Apply(editor.CreatePlan(fixture.Snapshot, explorer)).Succeeded);
        Assert.Equal(explorer, editor.Inspect(fixture.Snapshot).Settings);
        Assert.True(editor.Apply(editor.CreatePlan(fixture.Snapshot, GameplayDifficultySettings.GameDefault)).Succeeded);
        Assert.Equal(GameplayDifficultyStateKind.GameDefault, editor.Inspect(fixture.Snapshot).Kind);
    }

    [Fact]
    public void LegacyGameplayPakMarkerRemainsRecognizedForAOneStepUpgrade()
    {
        using var fixture = new GameplayFixture();
        SafeGameplayDifficultyEditor editor = fixture.CreateEditor();
        GameplayDifficultySettings legacySettings = new(130, 130, 130, 130);
        byte[] legacyPak = Encoding.UTF8.GetBytes("AEC gameplay fixture|130|130|130|130");
        File.WriteAllBytes(fixture.PakPath, legacyPak);
        string hash = Convert.ToHexString(SHA256.HashData(legacyPak));
        File.WriteAllText(fixture.MarkerPath, JsonSerializer.Serialize(new
        {
            Version = 1,
            Component = "gameplay",
            PakSha256 = hash,
            Settings = new
            {
                FoodPercent = 130,
                WaterPercent = 130,
                SleepPercent = 130,
                FallDamagePercent = 130,
            },
        }));

        GameplayDifficultyState state = editor.Inspect(fixture.Snapshot);

        Assert.Equal(GameplayDifficultyStateKind.Active, state.Kind);
        Assert.Equal(legacySettings, state.Settings);
        Assert.Equal(100, state.Settings.BleedingPercent);
        Assert.Equal(100, state.Settings.PoisonPercent);
        Assert.Equal(100, state.Settings.EnergyRecoveryPercent);
        Assert.Equal(100, state.Settings.WoundSleepHealingPercent);
        Assert.Equal(100, state.Settings.WoundStaminaPenaltyPercent);
        Assert.Equal(100, state.Settings.PoisonRecoveryPercent);
        Assert.Equal(100, state.Settings.RestDelayPercent);
        Assert.Equal(100, state.Settings.ExhaustionThresholdPercent);
        Assert.Equal(100, state.Settings.ExhaustionPenaltyPercent);
        Assert.Equal(100, state.Settings.WoundRecoveryDurationPercent);
        Assert.Equal(100, state.Settings.PoisonStaminaPenaltyPercent);
    }

    [Fact]
    public void PreviousGameplayPakMarkerRemainsRecognizedForAOneStepUpgrade()
    {
        using var fixture = new GameplayFixture();
        SafeGameplayDifficultyEditor editor = fixture.CreateEditor();
        GameplayDifficultySettings previousSettings = new(130, 130, 130, 130, 130, 130);
        byte[] previousPak = Encoding.UTF8.GetBytes("AEC gameplay fixture|130|130|130|130|130|130");
        File.WriteAllBytes(fixture.PakPath, previousPak);
        string hash = Convert.ToHexString(SHA256.HashData(previousPak));
        File.WriteAllText(fixture.MarkerPath, JsonSerializer.Serialize(new
        {
            Version = 2,
            Component = "gameplay",
            PakSha256 = hash,
            Settings = previousSettings,
        }));

        GameplayDifficultyState state = editor.Inspect(fixture.Snapshot);

        Assert.Equal(GameplayDifficultyStateKind.Active, state.Kind);
        Assert.Equal(previousSettings, state.Settings);
    }

    private static void AssertFloatPatch(
        GameplayAssetPatch patch,
        string id,
        int offset,
        float expected,
        float replacement)
    {
        Assert.Equal(id, patch.SettingId);
        GameplayByteMutation mutation = Assert.Single(patch.Mutations);
        Assert.Equal(offset, mutation.Offset);
        Assert.Equal(expected, BitConverter.ToSingle(mutation.ExpectedBytes), 5);
        Assert.Equal(replacement, BitConverter.ToSingle(mutation.ReplacementBytes), 5);
    }

    private sealed class GameplayFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"aec-gameplay-lifecycle-{Guid.NewGuid():N}");

        public GameplayFixture(bool proton = false)
        {
            string libraryRoot = proton ? Path.Combine(_root, "SteamLibrary") : _root;
            InstallDirectory = proton
                ? Path.Combine(libraryRoot, "steamapps", "common", "Ancestors The Humankind Odyssey")
                : Path.Combine(_root, "Game");
            UserDataDirectory = proton
                ? Path.Combine(
                    libraryRoot,
                    "steamapps",
                    "compatdata",
                    AncestorsGameProfile.SteamAppId,
                    "pfx",
                    "drive_c",
                    "users",
                    "steamuser",
                    "AppData",
                    "Local",
                    "Ancestors",
                    "Saved")
                : Path.Combine(_root, "UserData");
            Directory.CreateDirectory(Path.Combine(InstallDirectory, "Ancestors", "Content", "Paks"));
            Directory.CreateDirectory(UserDataDirectory);
            Snapshot = new GameInspectionSnapshot(
                DateTimeOffset.UnixEpoch,
                new GameInstallationSnapshot(
                    StoreKind.Steam,
                    proton ? HostKind.Linux : HostKind.Windows,
                    proton ? CompatibilityLayerKind.Proton : CompatibilityLayerKind.None,
                    libraryRoot,
                    InstallDirectory,
                    AncestorsGameProfile.SupportedSteamBuildId,
                    ExecutableExists: true,
                    ContentSignature: AncestorsGameProfile.SupportedContentSignature),
                UserDataDirectory,
                [],
                null,
                [],
                []);
        }

        public string InstallDirectory { get; }

        public string UserDataDirectory { get; }

        public GameInspectionSnapshot Snapshot { get; }

        public string PakPath => Path.Combine(
            InstallDirectory,
            "Ancestors",
            "Content",
            "Paks",
            GameplayPakBuilder.OwnPatchName);

        public string MarkerPath => Path.Combine(
            InstallDirectory,
            "Ancestors",
            "Content",
            "Paks",
            SafeGameplayDifficultyEditor.OwnershipMarkerName);

        public SafeGameplayDifficultyEditor CreateEditor() => new(
            () => DateTimeOffset.UtcNow,
            () => false,
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(UserDataDirectory), StringComparison.OrdinalIgnoreCase),
            buildPak: (_, settings) => Encoding.UTF8.GetBytes(
                $"AEC gameplay fixture|{settings.FoodPercent}|{settings.WaterPercent}|{settings.SleepPercent}|{settings.FallDamagePercent}|{settings.BleedingPercent}|{settings.PoisonPercent}|{settings.EnergyRecoveryPercent}|{settings.WoundSleepHealingPercent}|{settings.WoundStaminaPenaltyPercent}|{settings.PoisonRecoveryPercent}"),
            buildLegacyPak: (_, settings) => Encoding.UTF8.GetBytes(
                $"AEC gameplay fixture|{settings.FoodPercent}|{settings.WaterPercent}|{settings.SleepPercent}|{settings.FallDamagePercent}"),
            buildVersion2Pak: (_, settings) => Encoding.UTF8.GetBytes(
                $"AEC gameplay fixture|{settings.FoodPercent}|{settings.WaterPercent}|{settings.SleepPercent}|{settings.FallDamagePercent}|{settings.BleedingPercent}|{settings.PoisonPercent}"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
