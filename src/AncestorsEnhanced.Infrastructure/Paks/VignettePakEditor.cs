using System.Globalization;
using System.Security.Cryptography;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Paks;

internal static class VignettePakEditor
{
    public const string OwnPatchName = "AncestorsEnhanced-Vignette_P.pak";
    public const string LegacyPatchName = "pakchunk99-WindowsNoEditor_P.pak";
    public const string AssetPath =
        "Ancestors/Content/Prod/Data/TimeOfDay/Curves/VL01E01/VL01E01_Vignette_Intensity.uasset";

    private const string SourcePakName = "VL01E01.pak";
    private const int MaximumAssetSize = 1024 * 1024;
    private const int MaximumManagedPatchSize = 2 * 1024 * 1024;
    private const string OriginalAssetSha256 =
        "7F7455754E37BED619F6A1C24D6F3849D999B71DCFA8FD56DAD52B3BCF307C73";
    private const string LegacyPatchSha256 =
        "06F74C5E4BF70D2748614D8C74405B4C96FB4E50F103A66827C4E2041B2801A0";
    private static readonly int[] ScaleOffsets = [0x41A, 0x435, 0x439, 0x441, 0x450];

    public static VignetteModSnapshot Inspect(string installDirectory)
    {
        try
        {
            string pakDirectory = GetPakDirectory(installDirectory);
            string sourcePath = Path.Combine(pakDirectory, SourcePakName);
            byte[] original = ReadOriginalAsset(sourcePath);
            List<(string Path, decimal Percent, bool Managed)> overrides = [];
            foreach (string path in Directory.EnumerateFiles(pakDirectory, "*_P.pak"))
            {
                try
                {
                    if (!PakV5Archive.ContainsFile(path, AssetPath))
                    {
                        continue;
                    }

                    byte[] candidate = PakV5Archive.ReadFile(path, AssetPath, MaximumAssetSize);
                    if (!TryReadPercent(original, candidate, out decimal percent))
                    {
                        return new VignetteModSnapshot(null, false, "Conflicting vignette patch");
                    }

                    string name = Path.GetFileName(path);
                    bool managed = IsManagedPatch(path, name, candidate);
                    overrides.Add((path, percent, managed));
                }
                catch (Exception exception) when (IsExpectedPakException(exception))
                {
                    return new VignetteModSnapshot(null, false, "Unverified patch package");
                }
            }

            if (overrides.Count == 0)
            {
                return new VignetteModSnapshot(null, true, "Game asset verified");
            }

            if (overrides.Count != 1 || !overrides[0].Managed)
            {
                return new VignetteModSnapshot(null, false, "Conflicting vignette patch");
            }

            return new VignetteModSnapshot(
                overrides[0].Percent,
                true,
                "Managed graphics patch",
                overrides[0].Path);
        }
        catch (Exception exception) when (IsExpectedPakException(exception))
        {
            return new VignetteModSnapshot(null, false, "Unsupported game asset");
        }
    }

    public static IReadOnlyList<ConfigurationFileChangePlan> CreatePlans(
        GameInspectionSnapshot snapshot,
        string? value,
        out decimal currentPercent)
    {
        GameInstallationSnapshot installation = snapshot.Installation
            ?? throw new InvalidOperationException("The game installation was not detected.");
        _ = snapshot.Vignette
            ?? throw new InvalidOperationException("The vignette asset was not inspected.");
        VignetteModSnapshot state = Inspect(installation.InstallDirectory);
        if (!state.IsEditable)
        {
            throw new InvalidOperationException(state.Status);
        }
        currentPercent = state.Percent ?? 100m;

        decimal? requested = value is null
            ? null
            : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (requested == 100)
        {
            requested = null;
        }

        string pakDirectory = GetPakDirectory(installation.InstallDirectory);
        bool legacyActive = state.ActivePatchPath is not null &&
            string.Equals(Path.GetFileName(state.ActivePatchPath), LegacyPatchName, StringComparison.OrdinalIgnoreCase);
        var plans = new List<ConfigurationFileChangePlan>();
        if (legacyActive)
        {
            string legacyPath = state.ActivePatchPath!;
            byte[] legacy = ReadStableBounded(legacyPath, MaximumManagedPatchSize);
            plans.Add(new ConfigurationFileChangePlan(
                LegacyPatchName,
                legacyPath,
                Existed: true,
                Sha256(legacy),
                legacy,
                [],
                SettingFileTarget.Pak,
                ResultExists: false));
        }

        string targetPath = legacyActive || state.ActivePatchPath is null
            ? Path.Combine(pakDirectory, OwnPatchName)
            : state.ActivePatchPath;
        string fileName = Path.GetFileName(targetPath);
        ValidatePakFileName(fileName);
        bool existed = File.Exists(targetPath);
        byte[] originalFile = existed ? ReadStableBounded(targetPath, MaximumManagedPatchSize) : [];

        if (requested is null)
        {
            if (existed)
            {
                plans.Add(new ConfigurationFileChangePlan(
                    fileName,
                    targetPath,
                    existed,
                    Sha256(originalFile),
                    originalFile,
                    [],
                    SettingFileTarget.Pak,
                    ResultExists: false));
            }
            return plans;
        }

        byte[] originalAsset = ReadOriginalAsset(Path.Combine(pakDirectory, SourcePakName));
        byte[] updatedAsset = Scale(originalAsset, requested.Value);
        byte[] updatedPak = PakV5Archive.BuildSingleFile(AssetPath, updatedAsset);
        byte[] verifiedAsset = PakV5Archive.ReadFile(updatedPak, AssetPath);
        if (!verifiedAsset.AsSpan().SequenceEqual(updatedAsset))
        {
            throw new InvalidDataException("The generated vignette patch failed validation.");
        }

        plans.Add(new ConfigurationFileChangePlan(
            fileName,
            targetPath,
            existed,
            Sha256(originalFile),
            originalFile,
            updatedPak,
            SettingFileTarget.Pak));
        return plans;
    }

    private static byte[] ReadOriginalAsset(string sourcePath)
    {
        byte[] original = PakV5Archive.ReadFile(sourcePath, AssetPath, MaximumAssetSize);
        if (!string.Equals(Sha256(original), OriginalAssetSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The vignette source asset is not supported.");
        }

        return original;
    }

    internal static bool IsManagedPatch(string path, string name, byte[] asset)
    {
        byte[] package = ReadStableBounded(path, MaximumManagedPatchSize);
        if (string.Equals(name, OwnPatchName, StringComparison.OrdinalIgnoreCase))
        {
            return package.AsSpan().SequenceEqual(PakV5Archive.BuildSingleFile(AssetPath, asset));
        }

        return string.Equals(name, LegacyPatchName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Sha256(package), LegacyPatchSha256, StringComparison.Ordinal);
    }

    internal static byte[] Scale(byte[] original, decimal percent)
    {
        byte[] updated = [.. original];
        float multiplier = (float)(percent / 100);
        foreach (int offset in ScaleOffsets)
        {
            float value = BitConverter.ToSingle(original, offset) * multiplier;
            BitConverter.GetBytes(value).CopyTo(updated, offset);
        }

        return updated;
    }

    internal static bool TryReadPercent(byte[] original, byte[] candidate, out decimal percent)
    {
        percent = 0;
        if (candidate.Length != original.Length)
        {
            return false;
        }

        bool[] ignored = new bool[original.Length];
        foreach (int offset in ScaleOffsets)
        {
            for (int index = offset; index < offset + sizeof(float); index++)
            {
                ignored[index] = true;
            }
        }

        for (int index = 0; index < original.Length; index++)
        {
            if (!ignored[index] && original[index] != candidate[index])
            {
                return false;
            }
        }

        float reference = BitConverter.ToSingle(original, ScaleOffsets[0]);
        float candidateValue = BitConverter.ToSingle(candidate, ScaleOffsets[0]);
        decimal detected = decimal.Round((decimal)(candidateValue / reference * 100), 2);
        if (detected is < 0 or > 100)
        {
            return false;
        }

        foreach (int offset in ScaleOffsets)
        {
            float expected = BitConverter.ToSingle(original, offset) * (float)(detected / 100);
            if (Math.Abs(expected - BitConverter.ToSingle(candidate, offset)) > 0.000001f)
            {
                return false;
            }
        }

        percent = detected;
        return true;
    }

    private static bool IsExpectedPakException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or NotSupportedException or OverflowException;
}
