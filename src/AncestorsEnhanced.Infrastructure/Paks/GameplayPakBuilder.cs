using System.Security.Cryptography;
using AncestorsEnhanced.Infrastructure.Editing;

namespace AncestorsEnhanced.Infrastructure.Paks;

internal static class GameplayPakBuilder
{
    public const string OwnPatchName = "AncestorsEnhanced-Gameplay_P.pak";

    public static byte[] Build(
        string installDirectory,
        IReadOnlyList<GameplayAssetPatch> patches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentNullException.ThrowIfNull(patches);
        if (patches.Count == 0)
        {
            throw new InvalidOperationException("At least one verified gameplay patch is required.");
        }

        if (patches.Select(patch => patch.AssetPath).Distinct(StringComparer.Ordinal).Count() != patches.Count)
        {
            throw new InvalidOperationException("A gameplay PAK cannot contain the same asset twice.");
        }

        string pakDirectory = ConfigurationFileOperations.GetPakDirectory(installDirectory);
        var files = new List<(string FileName, byte[] Content)>(patches.Count);
        foreach (GameplayAssetPatch patch in patches.OrderBy(patch => patch.AssetPath, StringComparer.Ordinal))
        {
            ValidatePatch(patch);
            string sourcePath = Path.Combine(pakDirectory, patch.SourcePakName);
            byte[] original = PakV5Archive.ReadFile(sourcePath, patch.AssetPath, patch.MaximumAssetSize);
            if (!string.Equals(Sha256(original), patch.StockAssetSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The stock asset for {patch.SettingId} is not supported.");
            }

            byte[] updated = ApplyMutations(original, patch);
            files.Add((patch.AssetPath, updated));
        }

        byte[] pak = PakV5Archive.BuildFiles(files);
        foreach ((string fileName, byte[] content) in files)
        {
            byte[] verified = PakV5Archive.ReadFile(pak, fileName);
            if (!verified.AsSpan().SequenceEqual(content))
            {
                throw new InvalidDataException("The generated gameplay PAK failed its content verification.");
            }
        }

        return pak;
    }

    internal static byte[] ApplyMutations(byte[] original, GameplayAssetPatch patch)
    {
        ArgumentNullException.ThrowIfNull(original);
        ValidatePatch(patch);
        byte[] updated = [.. original];
        foreach (GameplayByteMutation mutation in patch.Mutations.OrderBy(mutation => mutation.Offset))
        {
            if (mutation.Offset < 0 || mutation.Offset > original.Length - mutation.ExpectedBytes.Length)
            {
                throw new InvalidDataException($"The verified offset for {patch.SettingId} is outside its source asset.");
            }

            if (!original.AsSpan(mutation.Offset, mutation.ExpectedBytes.Length).SequenceEqual(mutation.ExpectedBytes))
            {
                throw new InvalidDataException($"The expected stock bytes for {patch.SettingId} do not match.");
            }

            mutation.ReplacementBytes.CopyTo(updated, mutation.Offset);
        }

        return updated;
    }

    private static void ValidatePatch(GameplayAssetPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (string.IsNullOrWhiteSpace(patch.SettingId) ||
            string.IsNullOrWhiteSpace(patch.AssetPath) ||
            string.IsNullOrWhiteSpace(patch.SourcePakName) ||
            !string.Equals(Path.GetFileName(patch.SourcePakName), patch.SourcePakName, StringComparison.Ordinal) ||
            !patch.SourcePakName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(patch.StockAssetSha256) ||
            patch.MaximumAssetSize <= 0 ||
            patch.Mutations.Count == 0)
        {
            throw new InvalidDataException("The gameplay patch definition is incomplete.");
        }

        GameplayByteMutation[] mutations = [.. patch.Mutations.OrderBy(mutation => mutation.Offset)];
        for (int index = 0; index < mutations.Length; index++)
        {
            GameplayByteMutation mutation = mutations[index];
            if (mutation.ExpectedBytes is null || mutation.ReplacementBytes is null ||
                mutation.ExpectedBytes.Length == 0 ||
                mutation.ExpectedBytes.Length != mutation.ReplacementBytes.Length ||
                (index > 0 && mutation.Offset < mutations[index - 1].Offset + mutations[index - 1].ExpectedBytes.Length))
            {
                throw new InvalidDataException("The gameplay patch mutations are invalid or overlap.");
            }
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}

internal sealed record GameplayAssetPatch(
    string SettingId,
    string SourcePakName,
    string AssetPath,
    string StockAssetSha256,
    int MaximumAssetSize,
    IReadOnlyList<GameplayByteMutation> Mutations);

internal sealed record GameplayByteMutation(
    int Offset,
    byte[] ExpectedBytes,
    byte[] ReplacementBytes);
