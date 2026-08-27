using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Inspection;
using AncestorsEnhanced.Infrastructure.Paks;
using AncestorsEnhanced.Infrastructure.Platform;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

public sealed class SafeGameplayDifficultyEditor : IGameplayDifficultyEditor
{
    private const int MaximumManagedPakSize = 2 * 1024 * 1024;
    private const int MaximumMarkerSize = 64 * 1024;
    public const string OwnershipMarkerName = "AncestorsEnhanced-Gameplay_P.pak.aec-owned.sha256";

    private readonly SettingsTransaction _transaction;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string, bool> _isExpectedUserDataDirectory;
    private readonly Func<string, GameplayDifficultySettings, byte[]> _buildPak;
    private readonly Func<string, GameplayDifficultySettings, byte[]> _buildLegacyPak;
    private readonly Func<string, GameplayDifficultySettings, byte[]> _buildVersion2Pak;

    public SafeGameplayDifficultyEditor(GameContextVerifier verifier)
        : this(
            () => DateTimeOffset.UtcNow,
            GameProcessProbe.IsAncestorsRunning,
            GameEditingGuard.IsExpectedNativeUserDataDirectory,
            verifier)
    {
    }

    internal SafeGameplayDifficultyEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning,
        Func<string, bool> isExpectedUserDataDirectory,
        GameContextVerifier? verifier = null,
        Func<string, GameplayDifficultySettings, byte[]>? buildPak = null,
        Func<string, GameplayDifficultySettings, byte[]>? buildLegacyPak = null,
        Func<string, GameplayDifficultySettings, byte[]>? buildVersion2Pak = null)
    {
        _utcNow = utcNow;
        _isExpectedUserDataDirectory = isExpectedUserDataDirectory;
        _buildPak = buildPak ?? BuildPak;
        _buildLegacyPak = buildLegacyPak ?? BuildLegacyPak;
        _buildVersion2Pak = buildVersion2Pak ?? BuildVersion2Pak;
        _transaction = new SettingsTransaction(
            utcNow,
            isGameRunning,
            isExpectedUserDataDirectory,
            verifier is null ? (_ => true) : plan => Revalidate(verifier, plan),
            verifier is null ? (_ => true) : snapshot => RevalidateSnapshot(verifier, snapshot));
    }

    public GameplayDifficultyState Inspect(GameInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Supports(snapshot) || snapshot.Installation is null)
        {
            return new(
                GameplayDifficultyStateKind.Unverified,
                GameplayDifficultySettings.GameDefault,
                "Exact Steam build 5495393 with matching stock PAK signatures is required");
        }

        try
        {
            string pakPath = GetTargetPath(
                snapshot.UserDataDirectory!,
                snapshot.Installation.InstallDirectory,
                GameplayPakBuilder.OwnPatchName,
                SettingFileTarget.Pak);
            string markerPath = GetTargetPath(
                snapshot.UserDataDirectory!,
                snapshot.Installation.InstallDirectory,
                OwnershipMarkerName,
                SettingFileTarget.Pak);
            bool pakExists = File.Exists(pakPath);
            bool markerExists = File.Exists(markerPath);
            if (!pakExists && !markerExists)
            {
                return GameplayDifficultyState.GameDefault;
            }
            if (!pakExists || !markerExists)
            {
                return Unverified("The gameplay PAK and its ownership record are incomplete");
            }

            ValidateWritableTarget(pakPath);
            ValidateWritableTarget(markerPath);
            byte[] pak = ReadStableBounded(pakPath, MaximumManagedPakSize);
            byte[] marker = ReadStableBounded(markerPath, MaximumMarkerSize);
            if (!AecPakOwnershipMarker.TryReadGameplay(marker, out string expectedSha, out GameplayDifficultySettings settings, out int markerVersion) ||
                !string.Equals(Sha256(pak), expectedSha, StringComparison.Ordinal))
            {
                return Unverified("The gameplay PAK ownership record does not match the installed file");
            }

            byte[] reconstructed = markerVersion switch
            {
                1 => _buildLegacyPak(snapshot.Installation.InstallDirectory, settings),
                2 => _buildVersion2Pak(snapshot.Installation.InstallDirectory, settings),
                _ => _buildPak(snapshot.Installation.InstallDirectory, settings),
            };
            bool byteExact = pak.AsSpan().SequenceEqual(reconstructed);
            if (!byteExact && markerVersion >= AecPakOwnershipMarker.CurrentVersion)
            {
                return Unverified("The installed gameplay PAK is not the exact package AEC would generate");
            }

            // Packages recorded by an older marker format were built by retired PAK builders whose
            // exact bytes can no longer be reproduced (the builder groups asset mutations since the
            // current format). Their integrity is still proven by the ownership record's SHA-256,
            // so they remain active and editable and are rebuilt in the current format on next change.
            string formatNote = byteExact
                ? string.Empty
                : $" (legacy format v{markerVersion} · rebuilt on next change)";
            return ActiveState(settings, formatNote);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Unverified($"The installed gameplay PAK could not be verified: {exception.Message}");
        }
    }

    public SettingsChangePlan CreatePlan(
        GameInspectionSnapshot snapshot,
        GameplayDifficultySettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        GameEditingGuard.ValidateSnapshot(snapshot, _isExpectedUserDataDirectory);
        if (!Supports(snapshot) || snapshot.Installation is null)
        {
            throw new InvalidOperationException("Gameplay editing requires the exact researched Steam build and stock PAK signatures.");
        }

        PakFileSnapshot? foreign = snapshot.PakFiles.FirstOrDefault(pak =>
            pak.Classification is not PakClassification.BaseGame and not PakClassification.AecOwned &&
            !string.Equals(pak.Name, GameplayPakBuilder.OwnPatchName, StringComparison.OrdinalIgnoreCase));
        if (foreign is not null)
        {
            throw new InvalidOperationException(
                $"{foreign.Name} is an external PAK whose gameplay assets cannot be ruled out. Remove it before creating a gameplay patch.");
        }

        GameplayDifficultyState current = Inspect(snapshot);
        if (current.Kind == GameplayDifficultyStateKind.Unverified)
        {
            throw new InvalidOperationException(current.Description);
        }
        if (current.Settings == settings)
        {
            throw new InvalidOperationException("The selected gameplay values already match the installed state.");
        }

        string pakPath = GetTargetPath(
            snapshot.UserDataDirectory!,
            snapshot.Installation.InstallDirectory,
            GameplayPakBuilder.OwnPatchName,
            SettingFileTarget.Pak);
        string markerPath = GetTargetPath(
            snapshot.UserDataDirectory!,
            snapshot.Installation.InstallDirectory,
            OwnershipMarkerName,
            SettingFileTarget.Pak);
        byte[] originalPak = File.Exists(pakPath) ? ReadStableBounded(pakPath, MaximumManagedPakSize) : [];
        byte[] originalMarker = File.Exists(markerPath) ? ReadStableBounded(markerPath, MaximumMarkerSize) : [];
        bool existed = current.Kind == GameplayDifficultyStateKind.Active;
        var files = new List<ConfigurationFileChangePlan>(2);
        if (settings.IsGameDefault)
        {
            files.Add(CreateFilePlan(GameplayPakBuilder.OwnPatchName, pakPath, originalPak, [], existed, false));
            files.Add(CreateFilePlan(OwnershipMarkerName, markerPath, originalMarker, [], existed, false));
        }
        else
        {
            byte[] updatedPak = _buildPak(snapshot.Installation.InstallDirectory, settings);
            byte[] updatedMarker = AecPakOwnershipMarker.CreateGameplay(updatedPak, settings);
            files.Add(CreateFilePlan(GameplayPakBuilder.OwnPatchName, pakPath, originalPak, updatedPak, existed, true));
            files.Add(CreateFilePlan(OwnershipMarkerName, markerPath, originalMarker, updatedMarker, existed, true));
        }

        SettingChangePreview[] changes = CreatePreviews(current.Settings, settings);
        DateTimeOffset createdAt = _utcNow();
        VerifiedGameContext context = VerifiedGameContext.TryCreateFromSnapshot(snapshot)
            ?? throw new InvalidOperationException("The game context cannot be verified.");
        return _transaction.Issue(new SettingsChangePlan(
            $"gameplay-{createdAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}",
            createdAt,
            context.BuildId ?? string.Empty,
            context.UserDataDirectory,
            changes,
            files,
            context.InstallDirectory,
            context.ContextFingerprint,
            context.ContentSignature,
            Store: context.Store));
    }

    public SettingsOperationResult Apply(SettingsChangePlan plan) => _transaction.Apply(plan);

    public void DiscardPlan(SettingsChangePlan plan) => _transaction.Discard(plan);

    private static ConfigurationFileChangePlan CreateFilePlan(
        string fileName,
        string path,
        byte[] original,
        byte[] updated,
        bool existed,
        bool resultExists) => new(
            fileName,
            path,
            existed,
            Sha256(original),
            original,
            updated,
            SettingFileTarget.Pak,
            resultExists);

    private static SettingChangePreview[] CreatePreviews(
        GameplayDifficultySettings before,
        GameplayDifficultySettings after)
    {
        var changes = new List<SettingChangePreview>(16);
        Add("Food need", "gameplay.food", before.FoodPercent, after.FoodPercent);
        Add("Water need", "gameplay.water", before.WaterPercent, after.WaterPercent);
        Add("Sleep need", "gameplay.sleep", before.SleepPercent, after.SleepPercent);
        Add("Fall damage", "gameplay.fall-damage", before.FallDamagePercent, after.FallDamagePercent);
        Add("Bleeding", "gameplay.bleeding", before.BleedingPercent, after.BleedingPercent);
        Add("Poison", "gameplay.poison", before.PoisonPercent, after.PoisonPercent);
        Add("Energy recovery", "gameplay.energy-recovery", before.EnergyRecoveryPercent, after.EnergyRecoveryPercent);
        Add("Wound healing from sleep", "gameplay.wound-sleep-healing", before.WoundSleepHealingPercent, after.WoundSleepHealingPercent);
        Add("Wound stamina penalty", "gameplay.wound-stamina-penalty", before.WoundStaminaPenaltyPercent, after.WoundStaminaPenaltyPercent);
        Add("Poison recovery", "gameplay.poison-recovery", before.PoisonRecoveryPercent, after.PoisonRecoveryPercent);
        Add("Rest delay", "gameplay.rest-delay", before.RestDelayPercent, after.RestDelayPercent);
        Add("Exhaustion threshold", "gameplay.exhaustion-threshold", before.ExhaustionThresholdPercent, after.ExhaustionThresholdPercent);
        Add("Exhaustion penalty", "gameplay.exhaustion-penalty", before.ExhaustionPenaltyPercent, after.ExhaustionPenaltyPercent);
        Add("Wound recovery time", "gameplay.wound-recovery-duration", before.WoundRecoveryDurationPercent, after.WoundRecoveryDurationPercent);
        Add("Poison stamina penalty", "gameplay.poison-stamina-penalty", before.PoisonStaminaPenaltyPercent, after.PoisonStaminaPenaltyPercent);
        return [.. changes];

        void Add(string name, string key, int oldValue, int newValue)
        {
            if (oldValue != newValue)
            {
                changes.Add(new SettingChangePreview(
                    name,
                    GameplayPakBuilder.OwnPatchName,
                    key,
                    $"{oldValue}%",
                    $"{newValue}%"));
            }
        }
    }

    private static bool Supports(GameInspectionSnapshot snapshot) =>
        snapshot.Installation is
        {
            Store: StoreKind.Steam,
            BuildId: AncestorsGameProfile.SupportedSteamBuildId,
            ContentSignature: AncestorsGameProfile.SupportedContentSignature,
            ContentSignatureReadFailed: false,
        } && !string.IsNullOrWhiteSpace(snapshot.UserDataDirectory);

    private static byte[] BuildPak(string installDirectory, GameplayDifficultySettings settings) =>
        GameplayPakBuilder.Build(
            installDirectory,
            GameplayDifficultyPatchCatalog.Create(
                settings.FoodPercent,
                settings.WaterPercent,
                settings.SleepPercent,
                settings.FallDamagePercent,
                settings.BleedingPercent,
                settings.PoisonPercent,
                settings.EnergyRecoveryPercent,
                settings.WoundSleepHealingPercent,
                settings.WoundStaminaPenaltyPercent,
                settings.PoisonRecoveryPercent,
                settings.RestDelayPercent,
                settings.ExhaustionThresholdPercent,
                settings.ExhaustionPenaltyPercent,
                settings.WoundRecoveryDurationPercent,
                settings.PoisonStaminaPenaltyPercent));

    private static byte[] BuildVersion2Pak(string installDirectory, GameplayDifficultySettings settings) =>
        GameplayPakBuilder.Build(
            installDirectory,
            GameplayDifficultyPatchCatalog.CreateVersion2(
                settings.FoodPercent,
                settings.WaterPercent,
                settings.SleepPercent,
                settings.FallDamagePercent,
                settings.BleedingPercent,
                settings.PoisonPercent));

    private static byte[] BuildLegacyPak(string installDirectory, GameplayDifficultySettings settings) =>
        GameplayPakBuilder.Build(
            installDirectory,
            GameplayDifficultyPatchCatalog.CreateLegacy(
                settings.FoodPercent,
                settings.WaterPercent,
                settings.SleepPercent,
                settings.FallDamagePercent));

    private static GameplayDifficultyState ActiveState(GameplayDifficultySettings settings, string formatNote = "") => new(
        GameplayDifficultyStateKind.Active,
        settings,
        $"AEC gameplay PAK active{formatNote} · Food {settings.FoodPercent}% · Water {settings.WaterPercent}% · Sleep {settings.SleepPercent}% · Fall damage {settings.FallDamagePercent}% · Bleeding {settings.BleedingPercent}% · Poison {settings.PoisonPercent}% · Energy recovery {settings.EnergyRecoveryPercent}% · Wound sleep healing {settings.WoundSleepHealingPercent}% · Wound stamina penalty {settings.WoundStaminaPenaltyPercent}% · Poison recovery {settings.PoisonRecoveryPercent}% · Rest delay {settings.RestDelayPercent}% · Exhaustion threshold {settings.ExhaustionThresholdPercent}% · Exhaustion penalty {settings.ExhaustionPenaltyPercent}% · Wound recovery time {settings.WoundRecoveryDurationPercent}% · Poison stamina penalty {settings.PoisonStaminaPenaltyPercent}%");

    private static GameplayDifficultyState Unverified(string description) => new(
        GameplayDifficultyStateKind.Unverified,
        GameplayDifficultySettings.GameDefault,
        description);

    private static bool Revalidate(GameContextVerifier verifier, SettingsChangePlan plan)
    {
        VerifiedGameContext? current = verifier.Revalidate();
        return current is not null &&
            string.Equals(plan.ContextFingerprint, current.ContextFingerprint, StringComparison.Ordinal);
    }

    private static bool RevalidateSnapshot(GameContextVerifier verifier, GameInspectionSnapshot snapshot)
    {
        VerifiedGameContext? captured = VerifiedGameContext.TryCreateFromSnapshot(snapshot);
        return captured is not null && verifier.Verify(captured);
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException or NotSupportedException or OverflowException;
}
