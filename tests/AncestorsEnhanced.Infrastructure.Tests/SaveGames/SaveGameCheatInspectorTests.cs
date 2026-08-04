using System;
using System.IO;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SaveGameCheatInspectorTests
{
    private static string? RealSavePath() =>
        File.Exists(@"C:\Users\Firefly\AppData\Local\Ancestors\Saved\SaveGames\Savegame0.sav")
            ? @"C:\Users\Firefly\AppData\Local\Ancestors\Saved\SaveGames\Savegame0.sav"
            : null;

    [Fact]
    public void EncodeLiteralRoundTrips()
    {
        byte[] payload = new byte[256];
        new Random(1234).NextBytes(payload);
        byte[] encoded = SnappyBlockCodec.EncodeLiteral(payload);
        byte[] decoded = SnappyBlockCodec.Decode(encoded);
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void HealClanModifiesRealSaveInPlace()
    {
        string? path = RealSavePath();
        if (path is null)
        {
            // Skipped when the reference save is not present (e.g. CI).
            return;
        }

        byte[] decompressed = SnappyBlockCodec.Decode(File.ReadAllBytes(path));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.HealClan,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.ModifiedCount > 0);
        Assert.NotNull(modified);
        Assert.Equal(decompressed.Length, modified!.Length);
    }

    [Fact]
    public void MaxNeedsModifiesRealSaveInPlace()
    {
        string? path = RealSavePath();
        if (path is null)
        {
            return;
        }

        byte[] decompressed = SnappyBlockCodec.Decode(File.ReadAllBytes(path));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeeds,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(modified);
        Assert.Equal(decompressed.Length, modified!.Length);
    }
    [Fact]
    public void MaxNeuronalEnergyPatchesTheFloatArrayInPlace()
    {
        string? path = RealSavePath();
        if (path is null)
        {
            return;
        }

        byte[] decompressed = SnappyBlockCodec.Decode(File.ReadAllBytes(path));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.ModifiedCount > 50, $"Expected many fields (array), got {result.ModifiedCount}");
        Assert.Equal(decompressed.Length, modified!.Length);
    }

    [Fact]
    public void ForceMutationsReportsNoSupportedFields()
    {
        string? path = RealSavePath();
        if (path is null)
        {
            return;
        }

        byte[] decompressed = SnappyBlockCodec.Decode(File.ReadAllBytes(path));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.ForceMutations,
            out _);

        Assert.False(result.Succeeded);
        Assert.Contains("no supported", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}