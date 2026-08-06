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
        byte[] decompressed = DecompressedSaveWithCurrentCharacter(
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
    }    [Fact]
    public void MaxNeedsModifiesFloatFieldsInPlace()
    {
        byte[] decompressed = DecompressedSaveWithCurrentCharacter(
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
    }    [Fact]
    public void MaxNeuronalEnergyPatchesTheRpgArrayInPlace()
    {
        byte[] decompressed = DecompressedSaveWithRpgArray([0.5f, 0.6f, 0.7f]);
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
    public void ArrayElementCountBeyondNodePayloadIsRejected()
    {
        // A valid 3-element array whose header is changed to claim 10 elements must be
        // rejected as a whole: no clamping, no partial patching.
        byte[] decompressed = DecompressedSaveWithRpgArray([0.5f, 0.6f, 0.7f]);
        WriteCount(decompressed, FindArrayCountOffset(decompressed), 10);
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.False(result.Succeeded);
        Assert.Contains("no matching", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(modified);
    }
    [Fact]
    public void MaxNeuronalEnergyRejectsNegativeCount()
    {
        // A valid 3-element array whose header is changed to -1 must be rejected
        // without modifying any byte.
        byte[] decompressed = DecompressedSaveWithRpgArray([0.5f, 0.6f, 0.7f]);
        WriteCount(decompressed, FindArrayCountOffset(decompressed), -1);
        byte[] beforeInject = (byte[])decompressed.Clone();
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.False(result.Succeeded);
        Assert.Null(modified);
        Assert.Empty(result.ModifiedRanges);
        // The Injector must not touch the input bytes at all for a rejected array.
        Assert.Equal(beforeInject, decompressed);
    }
    [Fact]
    public void MaxNeuronalEnergyRejectsCountThatOverflowsTheLengthMath()
    {
        // An extreme count must be rejected before any multiplication can overflow.
        byte[] decompressed = DecompressedSaveWithRpgArray([0.5f, 0.6f, 0.7f]);
        WriteCount(decompressed, FindArrayCountOffset(decompressed), int.MaxValue);
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.False(result.Succeeded);
        Assert.Null(modified);
    }
    [Fact]
    public void MaxNeuronalEnergyLeavesTheCountHeaderAndTrailingPropertyUnchanged()
    {
        // A NeuronalEnergySources array immediately followed by a normal FloatProperty.
        // Only the array elements may change; the count header and the trailing property
        // must stay byte-identical.
        byte[] original = DecompressedSaveWithRpgArrayAndTrailing([0.5f, 0.6f, 0.7f], 42.0f);
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            original,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.NotNull(modified);

        int countOffset = FindArrayCountOffset(modified!);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(modified.AsSpan(countOffset, 4)));
        Assert.Equal(999_999.0f, ReadFloat(modified, countOffset + 4));
        Assert.Equal(999_999.0f, ReadFloat(modified, countOffset + 8));
        Assert.Equal(999_999.0f, ReadFloat(modified, countOffset + 12));
        Assert.True(ArraysEqualExcept(original, modified, result.ModifiedRanges));
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

    private static float ReadFloat(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));

    // True when actual equals expected except for the given ranges.
    private static bool ArraysEqualExcept(
        byte[] expected,
        byte[] actual,
        IReadOnlyList<ByteRange> ranges)
    {
        byte[] copy = (byte[])expected.Clone();
        foreach (ByteRange range in ranges)
        {
            for (int index = range.Offset; index < range.EndExclusive; index++)
            {
                copy[index] = actual[index];
            }
        }

        return copy.AsSpan().SequenceEqual(actual);
    }

    private static void WriteCount(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);

    // The count header of the NeuronalEnergySources array in a schema-verified way.
    private static int FindArrayCountOffset(byte[] data)
    {
        SaveGameSchemaNode root = SaveGameSchemaAnalyzer.Parse(data);
        SaveGameSchemaNode node = FindSchemaNode(root, "NeuronalEnergySources")
            ?? throw new InvalidDataException("The NeuronalEnergySources array was not found.");
        return node.ValueOffset;
    }

    private static SaveGameSchemaNode? FindSchemaNode(SaveGameSchemaNode node, string name)
    {
        if (string.Equals(node.Name, name, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (SaveGameSchemaNode child in node.Children)
        {
            SaveGameSchemaNode? found = FindSchemaNode(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // RPGData { NeuronalEnergySources[3] ; TrailingFloat } - one valid property tree.
    private static byte[] DecompressedSaveWithRpgArrayAndTrailing(float[] elements, float trailing)
    {
        using var rpg = new MemoryStream();
        rpg.Write(DecompressedSaveWithArray("NeuronalEnergySources", elements));
        rpg.Write(UnrealTaggedProperties.EncodeFloat("TrailingValue", trailing));
        rpg.Write(UnrealTaggedProperties.EncodeTerminator());
        using var root = new MemoryStream();
        root.Write(UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", rpg.ToArray()));
        root.Write(UnrealTaggedProperties.EncodeTerminator());
        return root.ToArray();
    }
}