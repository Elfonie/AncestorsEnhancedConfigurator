using System.Buffers.Binary;
using System.Text;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

/// <summary>Structural cheat targeting by exact schema path and type, never by name alone.</summary>
public sealed class CheatTargetSpecTests
{
    [Fact]
    public void SpecMatchesEnforcesTypeAndElementType()
    {
        var scalar = new SaveGameSchemaNode("Energy", "FloatProperty");
        Assert.True(new CheatTargetSpec("p", "Energy", "FloatProperty", null, false, 1.0f).Matches(scalar));

        var wrongType = new SaveGameSchemaNode("Energy", "IntProperty");
        Assert.False(new CheatTargetSpec("p", "Energy", "FloatProperty", null, false, 1.0f).Matches(wrongType));

        var array = new SaveGameSchemaNode("NeuronalEnergySources", "ArrayProperty") { ElementType = "FloatProperty" };
        Assert.True(new CheatTargetSpec("p", "NeuronalEnergySources", "ArrayProperty", "FloatProperty", true, 1.0f).Matches(array));
    }

    [Fact]
    public void CheatTargetsCarryExactUniquePaths()
    {
        var needs = SaveGameCheatTargets.CheatTargetsFor(CheatKind.MaxNeeds);
        Assert.All(needs, spec => Assert.NotEmpty(spec.SchemaPath));
        Assert.Contains(needs, spec =>
            spec.SchemaPath == "<save>/PlayerControllerData/CharacterData/VitalityData/RegimenStamina");
        Assert.All(needs, spec => Assert.False(spec.IsArray));
    }

    [Fact]
    public void SamePropertyNameAtTwoPathsOnlyPatchesTheExactTarget()
    {
        byte[] current = DecompressedSaveWithCurrentCharacter(("Energy", 0.5f), ("Stamina", 0.5f), ("Health", 0.5f));
        byte[] unrelated = DecompressedSaveWithRootHealth(0.5f);
        byte[] decompressed = Concat(current, unrelated).ToArray();
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.HealCurrentApe,
            out byte[]? modified);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(modified);
        Assert.Equal(3, result.ModifiedCount);
        Assert.True(ArraysEqualExcept(decompressed, modified!, result.ModifiedRanges));
    }

    [Fact]
    public void MaxNeedsRequiresEverySchemaTargetBeforePublishingBytes()
    {
        var injector = new SaveGameCheatInjector();
        byte[] complete = DecompressedSaveWithCurrentCharacter(
            ("RegimenStamina", 0.2f), ("Energy", 0.3f), ("Stamina", 0.4f));

        CheatInjectionResult accepted = injector.TryInject(complete, CheatKind.MaxNeeds, out byte[]? modified);

        Assert.True(accepted.Succeeded, accepted.Message);
        Assert.NotNull(modified);
        Assert.Equal(3, accepted.ModifiedCount);

        byte[] incomplete = DecompressedSaveWithCurrentCharacter(
            ("RegimenStamina", 0.2f), ("Energy", 0.3f));
        CheatInjectionResult rejected = injector.TryInject(incomplete, CheatKind.MaxNeeds, out byte[]? partial);

        Assert.False(rejected.Succeeded);
        Assert.Null(partial);
        Assert.Empty(rejected.ModifiedRanges);
    }

    [Fact]
    public void MultipleTargetsAtTheSamePathExceedTheAuthorisedCount()
    {
        // Two AvailableNeuronalEnergy properties inside one RPGData struct share the
        // exact schema path. The scalar target authorises only one match.
        byte[] rpg = Concat(
            UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", 0.5f),
            UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", 0.6f),
            UnrealTaggedProperties.EncodeTerminator()).ToArray();
        byte[] decompressed = Concat(
            UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", rpg),
            UnrealTaggedProperties.EncodeTerminator()).ToArray();
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.False(result.Succeeded);
        Assert.Contains("exactly 1", result.Message, StringComparison.Ordinal);
        Assert.Null(modified);
    }

    [Fact]
    public void RightPathWrongTypeIsNotPatched()
    {
        byte[] rpg = Concat(
            UnrealTaggedProperties.EncodeInt("AvailableNeuronalEnergy", 1),
            UnrealTaggedProperties.EncodeTerminator()).ToArray();
        byte[] decompressed = Concat(
            UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", rpg),
            UnrealTaggedProperties.EncodeTerminator()).ToArray();
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.False(result.Succeeded);
        Assert.Contains("exactly 1", result.Message, StringComparison.Ordinal);
        Assert.Null(modified);
    }

    [Fact]
    public void RightNameWrongParentIsNotPatched()
    {
        byte[] other = Concat(
            UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", 0.5f),
            UnrealTaggedProperties.EncodeTerminator()).ToArray();
        byte[] decompressed = Concat(
            UnrealTaggedProperties.EncodeStruct("OtherData", "SomethingElse", other),
            UnrealTaggedProperties.EncodeTerminator()).ToArray();
        var injector = new SaveGameCheatInjector();

        CheatInjectionResult result = injector.TryInject(
            decompressed,
            CheatKind.MaxNeuronalEnergy,
            out byte[]? modified);

        Assert.False(result.Succeeded);
        Assert.Null(modified);
    }

    private static byte[] ArrayProperty(string name, float[] elements)
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
            BinaryPrimitives.WriteInt32LittleEndian(valueBytes, BitConverter.SingleToInt32Bits(element));
            stream.Write(valueBytes);
        }

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
        using var character = new MemoryStream();
        if (vitality.Length > UnrealTaggedProperties.EncodeTerminator().Length)
        {
            character.Write(UnrealTaggedProperties.EncodeStruct("VitalityData", "VitalityServiceData", vitality.ToArray()));
        }

        if (health.Length > UnrealTaggedProperties.EncodeTerminator().Length)
        {
            character.Write(UnrealTaggedProperties.EncodeStruct("HealthData", "HealthServiceData", health.ToArray()));
        }

        character.Write(UnrealTaggedProperties.EncodeTerminator());
        using var controller = new MemoryStream();
        controller.Write(UnrealTaggedProperties.EncodeStruct("CharacterData", "GameCharacterSaveGame", character.ToArray()));
        controller.Write(UnrealTaggedProperties.EncodeTerminator());
        using var root = new MemoryStream();
        root.Write(UnrealTaggedProperties.EncodeStruct("PlayerControllerData", "GamePlayerControllerSaveData", controller.ToArray()));
        root.Write(UnrealTaggedProperties.EncodeTerminator());
        return root.ToArray();
    }

    private static byte[] DecompressedSaveWithRootHealth(float value) =>
        Concat(
            UnrealTaggedProperties.EncodeFloat("Health", value),
            UnrealTaggedProperties.EncodeTerminator()).ToArray();

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

    private static bool ArraysEqualExcept(byte[] expected, byte[] actual, IReadOnlyList<ByteRange> ranges)
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
}
