using System.Security.Cryptography;
using AncestorsEnhanced.Infrastructure.Paks;

namespace AncestorsEnhanced.Infrastructure.Tests.Paks;

public sealed class GameplayPakBuilderTests
{
    [Fact]
    public void BuildCreatesVerifiedMultiAssetPakFromExactStockAssets()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aec-gameplay-{Guid.NewGuid():N}");
        string installDirectory = Path.Combine(root, "Game");
        string pakDirectory = Path.Combine(installDirectory, "Ancestors", "Content", "Paks");
        Directory.CreateDirectory(pakDirectory);
        try
        {
            byte[] damage = [0, 0, 0, 0, 0, 0, 0, 0];
            byte[] weapon = [10, 11, 12, 13, 14, 15, 16, 17];
            File.WriteAllBytes(
                Path.Combine(pakDirectory, "Ancestors-WindowsNoEditor.pak"),
                PakV5Archive.BuildFiles(
                [
                    ("Ancestors/Content/Damage.uasset", damage),
                    ("Ancestors/Content/Weapon.uasset", weapon),
                ]));

            byte[] result = GameplayPakBuilder.Build(
                installDirectory,
                [
                    CreatePatch("damage", "Ancestors/Content/Damage.uasset", damage, 2, [0, 0], [1, 2]),
                    CreatePatch("weapon", "Ancestors/Content/Weapon.uasset", weapon, 4, [14, 15], [20, 21]),
                ]);

            Assert.Equal(new byte[] { 0, 0, 1, 2, 0, 0, 0, 0 }, PakV5Archive.ReadFile(result, "Ancestors/Content/Damage.uasset"));
            Assert.Equal(new byte[] { 10, 11, 12, 13, 20, 21, 16, 17 }, PakV5Archive.ReadFile(result, "Ancestors/Content/Weapon.uasset"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildFailsClosedWhenTheStockAssetHashChanged()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aec-gameplay-{Guid.NewGuid():N}");
        string installDirectory = Path.Combine(root, "Game");
        string pakDirectory = Path.Combine(installDirectory, "Ancestors", "Content", "Paks");
        Directory.CreateDirectory(pakDirectory);
        try
        {
            byte[] actual = [1, 2, 3, 4];
            byte[] expected = [1, 2, 3, 5];
            File.WriteAllBytes(
                Path.Combine(pakDirectory, "Ancestors-WindowsNoEditor.pak"),
                PakV5Archive.BuildSingleFile("Ancestors/Content/Test.uasset", actual));

            Assert.Throws<InvalidDataException>(() => GameplayPakBuilder.Build(
                installDirectory,
                [CreatePatch("test", "Ancestors/Content/Test.uasset", expected, 0, [1], [2])]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyMutationsFailsClosedWhenExpectedBytesDoNotMatch()
    {
        byte[] source = [1, 2, 3, 4];
        GameplayAssetPatch patch = CreatePatch("test", "Ancestors/Content/Test.uasset", source, 1, [9], [5]);

        Assert.Throws<InvalidDataException>(() => GameplayPakBuilder.ApplyMutations(source, patch));
    }

    private static GameplayAssetPatch CreatePatch(
        string settingId,
        string assetPath,
        byte[] source,
        int offset,
        byte[] expected,
        byte[] replacement) => new(
        settingId,
        "Ancestors-WindowsNoEditor.pak",
        assetPath,
        Convert.ToHexString(SHA256.HashData(source)),
        1024,
        [new GameplayByteMutation(offset, expected, replacement)]);
}
