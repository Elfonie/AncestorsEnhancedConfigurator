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
    public void HealCurrentApeModifiesFloatFieldsInPlace()
    {
        byte[] decompressed = DecompressedSaveWithCurrentCharacter(
            ("Health", 0.5f),
            ("Energy", 0.5f),
            ("Stamina", 0.5f));
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.HealCurrentApe,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.ModifiedCount);
        Assert.NotNull(modified);
        Assert.Equal(decompressed.Length, modified!.Length);
    }

    [Fact]
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
    }

    [Fact]
    public void MaxNeuronalEnergyPatchesOnlyAvailablePoolAndLeavesSourcesUntouched()
    {
        byte[] decompressed = DecompressedSaveWithRpgEnergy([0.5f, 0.6f, 0.7f], 0.02f);
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.ModifiedCount);
        Assert.NotNull(modified);
        Assert.Equal(decompressed.Length, modified!.Length);
        SaveGameSchemaNode root = SaveGameSchemaAnalyzer.Parse(modified);
        SaveGameSchemaNode pool = FindSchemaNode(root, "AvailableNeuronalEnergy")!;
        SaveGameSchemaNode sources = FindSchemaNode(root, "NeuronalEnergySources")!;
        Assert.Equal(1000.0f, ReadFloat(modified, pool.ValueOffset));
        Assert.Equal([0.5f, 0.6f, 0.7f], ReadArray(modified, sources));
        Assert.True(ArraysEqualExcept(decompressed, modified, result.ModifiedRanges));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    [InlineData("wrong-parent")]
    [InlineData("duplicate")]
    public void MaxNeuronalEnergyRejectsAnyAmbiguousOrInvalidTarget(string shape)
    {
        byte[] save = shape switch
        {
            "missing" => DecompressedSaveWithRpgArray([0.5f]),
            "wrong-type" => DecompressedSaveWithRpgProperty("AvailableNeuronalEnergy", "IntProperty"),
            "wrong-parent" => DecompressedSaveWithOtherPool(),
            "duplicate" => DecompressedSaveWithRpgDuplicatePool(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        CheatInjectionResult result = new SaveGameCheatInjector().TryInject(
            save, CheatKind.MaxNeuronalEnergy, out byte[]? modified);

        Assert.False(result.Succeeded);
        Assert.Null(modified);
        Assert.Empty(result.ModifiedRanges);
    }
    [Fact]
    public void HealCurrentApeDoesNotTouchUnrelatedHealthFields()
    {
        // A Health field owned by an unrelated object (not under the active character)
        // must be left alone.
        byte[] current = DecompressedSaveWithCurrentCharacter(("Energy", 0.5f), ("Stamina", 0.5f), ("Health", 0.5f));
        byte[] unrelated = DecompressedSaveWithRootHealth(0.5f);
        byte[] decompressed = current.Concat(unrelated).ToArray();
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.HealCurrentApe,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(modified);
        Assert.Equal(3, result.ModifiedCount);
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

    private static byte[] DecompressedSaveWithRpgEnergy(float[] elements, float available)
    {
        byte[] array = DecompressedSaveWithArray("NeuronalEnergySources", elements);
        array = array[..^UnrealTaggedProperties.EncodeTerminator().Length];
        byte[] rpg = Concat(
            array,
            UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", available),
            UnrealTaggedProperties.EncodeTerminator());
        return Concat(
            UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", rpg),
            UnrealTaggedProperties.EncodeTerminator());
    }

    private static byte[] DecompressedSaveWithRpgProperty(string name, string type)
    {
        byte[] property = type == "IntProperty"
            ? UnrealTaggedProperties.EncodeInt(name, 2)
            : UnrealTaggedProperties.EncodeFloat(name, 0.02f);
        return Concat(
            UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", Concat(property, UnrealTaggedProperties.EncodeTerminator())),
            UnrealTaggedProperties.EncodeTerminator());
    }

    private static byte[] DecompressedSaveWithOtherPool() => Concat(
        UnrealTaggedProperties.EncodeStruct("OtherData", "OtherData", Concat(
            UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", 0.02f),
            UnrealTaggedProperties.EncodeTerminator())),
        UnrealTaggedProperties.EncodeTerminator());

    private static byte[] DecompressedSaveWithRpgDuplicatePool() => Concat(
        UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", Concat(
            UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", 0.02f),
            UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", 0.03f),
            UnrealTaggedProperties.EncodeTerminator())),
        UnrealTaggedProperties.EncodeTerminator());

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

    private static float[] ReadArray(byte[] data, SaveGameSchemaNode node)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(node.ValueOffset, sizeof(int)));
        return Enumerable.Range(0, count)
            .Select(index => ReadFloat(data, node.ValueOffset + sizeof(int) + index * sizeof(float)))
            .ToArray();
    }

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
