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
        byte[] source = DecompressedSaveWithCurrentCharacter(
            ("Health", 0.5f),
            ("Energy", 0.5f),
            ("Stamina", 0.5f));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            source,
            CheatKind.HealClan,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.NotNull(modified);
        Assert.Equal(source.Length, modified!.Length);
        Assert.Equal(3, result.ModifiedRanges.Count);
        foreach (ByteRange range in result.ModifiedRanges)
        {
            Assert.Equal(1.0f, ReadFloat(modified, range.Offset));
        }

        Assert.True(ArraysEqualExcept(source, modified, result.ModifiedRanges));
    }
    [Fact]
    public void MaxNeedsModifiesFloatFieldsInPlace()
    {
        byte[] source = DecompressedSaveWithCurrentCharacter(
            ("RegimenStamina", 0.5f),
            ("Energy", 0.5f),
            ("Stamina", 0.5f));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            source,
            CheatKind.MaxNeeds,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.NotNull(modified);
        Assert.Equal(source.Length, modified!.Length);
        Assert.Equal(3, result.ModifiedRanges.Count);
        foreach (ByteRange range in result.ModifiedRanges)
        {
            Assert.Equal(1_000.0f, ReadFloat(modified, range.Offset));
        }

        Assert.True(ArraysEqualExcept(source, modified, result.ModifiedRanges));
    }
    [Fact]
    public void MaxNeuronalEnergyPatchesTheRpgArrayInPlace()
    {
        byte[] source = DecompressedSaveWithRpgArray([0.5f, 0.6f, 0.7f]);
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            source,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.NotNull(modified);
        Assert.Equal(source.Length, modified!.Length);
        Assert.Equal(999_999.0f, ReadFloat(modified, result.ModifiedRanges.Single().Offset + 4));
        Assert.Equal(999_999.0f, ReadFloat(modified, result.ModifiedRanges.Single().Offset + 8));
        Assert.Equal(999_999.0f, ReadFloat(modified, result.ModifiedRanges.Single().Offset + 12));
        Assert.True(ArraysEqualExcept(source, modified, result.ModifiedRanges));
    }

    [Fact]
    public void ArrayElementCountBeyondNodePayloadIsClampedToZero()
    {
        // Array claims 10 elements but the node payload only contains 4 bytes (the count).
        byte[] source = DecompressedSaveWithArray("NeuronalEnergySources", [0.5f, 0.6f, 0.7f]);
        using var stream = new MemoryStream();
        // Re-encode with a count of 10 but a payload of only 4 bytes.
        stream.Write(EncodeString("NeuronalEnergySources"));
        stream.Write(EncodeString("ArrayProperty"));
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(size, 4);
        stream.Write(size);
        stream.Write(EncodeString("FloatProperty"));
        stream.WriteByte(0);
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, 10);
        stream.Write(count);
        stream.Write(UnrealTaggedProperties.EncodeTerminator());
        byte[] malformed = stream.ToArray();

        var injector = new SaveGameCheatInjector();
        CheatInjectionResult result = injector.TryInject(
            malformed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        // Zero elements fit inside the node payload, so nothing may be patched.
        Assert.False(result.Succeeded);
        Assert.Contains("no matching", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(modified);
    }

    private static float ReadFloat(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));

    private static bool ArraysEqualExcept(byte[] expected, byte[] actual, IReadOnlyList<ByteRange> ranges)
    {
        byte[] copy = (byte[])expected.Clone();
        foreach (ByteRange range in ranges)
        {
            for (int i = range.Offset; i < range.EndExclusive; i++)
            {
                copy[i] = actual[i];
            }
        }

        return copy.AsSpan().SequenceEqual(actual);
    }
    [Fact]
    public void HealClanDoesNotTouchUnrelatedHealthFields()
    {
        // A Health field owned by an unrelated object (not under the active character)
        // must be left alone.
        byte[] current = DecompressedSaveWithCurrentCharacter(("Health", 0.5f));
        byte[] unrelated = DecompressedSaveWithRootHealth(0.5f);
        byte[] decompressed = current.Concat(unrelated).ToArray();
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.HealClan,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(modified);
        Assert.Equal(1, result.ModifiedCount);
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



    private static byte[] DecompressedSaveWithCurrentCharacter(params (string Name, float Value)[] floats)
    {
        using var vitality = new MemoryStream();
        foreach ((string name, float value) in floats.Where(f => f.Name != "Health"))
        {
            vitality.Write(UnrealTaggedProperties.EncodeFloat(name, value));
        }

        vitality.Write(UnrealTaggedProperties.EncodeTerminator());
        using var health = new MemoryStream();
        foreach ((string name, float value) in floats.Where(f => f.Name == "Health"))
        {
            health.Write(UnrealTaggedProperties.EncodeFloat(name, value));
        }

        health.Write(UnrealTaggedProperties.EncodeTerminator());
        using var characterData = new MemoryStream();
        if (vitality.Length > UnrealTaggedProperties.EncodeTerminator().Length)
        {
            characterData.Write(UnrealTaggedProperties.EncodeStruct(
                "VitalityData",
                "VitalityServiceData",
                vitality.ToArray()));
        }

        if (health.Length > UnrealTaggedProperties.EncodeTerminator().Length)
        {
            characterData.Write(UnrealTaggedProperties.EncodeStruct(
                "HealthData",
                "HealthServiceData",
                health.ToArray()));
        }

        characterData.Write(UnrealTaggedProperties.EncodeTerminator());
        using var controller = new MemoryStream();
        controller.Write(UnrealTaggedProperties.EncodeStruct(
            "CharacterData",
            "GameCharacterSaveGame",
            characterData.ToArray()));
        controller.Write(UnrealTaggedProperties.EncodeTerminator());
        using var root = new MemoryStream();
        root.Write(UnrealTaggedProperties.EncodeStruct(
            "PlayerControllerData",
            "GamePlayerControllerSaveData",
            controller.ToArray()));
        root.Write(UnrealTaggedProperties.EncodeTerminator());
        return root.ToArray();
    }

    private static byte[] DecompressedSaveWithRpgArray(float[] elements)
    {
        byte[] array = DecompressedSaveWithArray("NeuronalEnergySources", elements);
        byte[] rpg = Concat(array, UnrealTaggedProperties.EncodeTerminator());
        byte[] root = Concat(
            UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", rpg),
            UnrealTaggedProperties.EncodeTerminator());
        return root;
    }

    private static byte[] DecompressedSaveWithRootHealth(float value)
    {
        return Concat(
            UnrealTaggedProperties.EncodeFloat("Health", value),
            UnrealTaggedProperties.EncodeTerminator());
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using var output = new MemoryStream();
        foreach (byte[] part in parts)
        {
            output.Write(part);
        }

        return output.ToArray();
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