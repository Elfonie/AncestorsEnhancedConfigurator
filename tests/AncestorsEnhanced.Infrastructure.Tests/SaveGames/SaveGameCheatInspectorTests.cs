using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SaveGameCheatInspectorTests
{
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
    public void HealClanModifiesFloatFieldsInPlace()
    {
        byte[] decompressed = DecompressedSaveWith(
            ("Health", 0.5f),
            ("Energy", 0.5f),
            ("Stamina", 0.5f));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.HealClan,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.NotNull(modified);
        Assert.Equal(decompressed.Length, modified!.Length);
    }

    [Fact]
    public void MaxNeedsModifiesFloatFieldsInPlace()
    {
        byte[] decompressed = DecompressedSaveWith(
            ("RegimenStamina", 0.5f),
            ("Energy", 0.5f),
            ("Stamina", 0.5f));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeeds,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.NotNull(modified);
        Assert.Equal(decompressed.Length, modified!.Length);
    }

    [Fact]
    public void MaxNeuronalEnergyPatchesTheFloatArrayInPlace()
    {
        byte[] decompressed = DecompressedSaveWithArray(
            name: "NeuronalEnergySources",
            elements: [0.5f, 0.6f, 0.7f]);
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.Equal(decompressed.Length, modified!.Length);
    }

    [Fact]
    public void ForceMutationsReportsNoSupportedFields()
    {
        byte[] decompressed = DecompressedSaveWith(("Health", 0.5f));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.ForceMutations,
            out _);

        Assert.False(result.Succeeded);
        Assert.Contains("no supported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] DecompressedSaveWith(params (string Name, float Value)[] floats)
    {
        using var stream = new MemoryStream();
        foreach ((string name, float value) in floats)
        {
            stream.Write(UnrealTaggedProperties.EncodeFloat(name, value));
        }

        stream.Write(UnrealTaggedProperties.EncodeTerminator());
        return stream.ToArray();
    }

    private static byte[] DecompressedSaveWithArray(string name, float[] elements)
    {
        using var stream = new MemoryStream();
        stream.Write(EncodeString(name));
        stream.Write(EncodeString("ArrayProperty"));

        int length = 4 + elements.Length * sizeof(float);
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(size, length);
        stream.Write(size);

        stream.Write(EncodeString("FloatProperty"));
        stream.WriteByte(0);

        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, elements.Length);
        stream.Write(count);
        Span<byte> valueBytes = stackalloc byte[4];
        foreach (float element in elements)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                valueBytes,
                BitConverter.SingleToInt32Bits(element));
            stream.Write(valueBytes);
        }

        stream.Write(UnrealTaggedProperties.EncodeTerminator());
        return stream.ToArray();
    }

    private static byte[] EncodeString(string value)
    {
        byte[] text = Encoding.UTF8.GetBytes(value);
        byte[] result = new byte[text.Length + 5];
        BinaryPrimitives.WriteInt32LittleEndian(result, text.Length + 1);
        text.CopyTo(result, 4);
        return result;
    }
}