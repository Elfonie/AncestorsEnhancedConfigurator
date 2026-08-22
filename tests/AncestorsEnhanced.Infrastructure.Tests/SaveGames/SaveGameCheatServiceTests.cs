using System;
using System.Buffers.Binary;
using System.IO;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SaveGameCheatServiceTests : IDisposable
{
    private readonly string _userData = Path.Combine(
        Path.GetTempPath(),
        $"aec-cheat-service-{Guid.NewGuid():N}");

    [Fact]
    public void MaxNeuronalEnergyAppliesAndStoresAnIntegralCheckpoint()
    {
        Directory.CreateDirectory(Path.Combine(_userData, "SaveGames"));
        byte[] decompressed = DecompressedSaveWithRpgArray([0.5f, 0.6f, 0.7f]);
        byte[] compressed = SnappyBlockCodec.EncodeLiteral(decompressed);
        File.WriteAllBytes(
            Path.Combine(_userData, "SaveGames", "Savegame0.sav"),
            compressed);

        var service = new SaveGameCheatService(
            new SaveGameCheatInjector(),
            _userData);

        CheatApplyResult result = service.Apply(CheatKind.MaxNeuronalEnergy, "0");

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(result.CheckpointId);
        string checkpointPath = SaveGamePaths.GetCheckpointPath(_userData, 0, result.CheckpointId!);
        Assert.True(File.Exists(checkpointPath));

        byte[] stored = File.ReadAllBytes(checkpointPath);
        byte[] roundTripped = SnappyBlockCodec.Decode(stored);
        SaveGameSchemaNode root = SaveGameSchemaAnalyzer.Parse(roundTripped);
        SaveGameSchemaNode arrayNode = FindNode(root, "NeuronalEnergySources")
            ?? throw new InvalidDataException("The stored checkpoint lost the NeuronalEnergySources array.");
        SaveGameSchemaNode poolNode = FindNode(root, "AvailableNeuronalEnergy")
            ?? throw new InvalidDataException("The stored checkpoint lost the AvailableNeuronalEnergy pool.");
        Assert.Equal("ArrayProperty", arrayNode.Type);
        Assert.Equal("FloatProperty", arrayNode.ElementType);
        // 4 (count header) + 3 * 4 (float elements).
        Assert.Equal(4 + 3 * sizeof(float), arrayNode.ValueLength);

        int count = BinaryPrimitives.ReadInt32LittleEndian(
            roundTripped.AsSpan(arrayNode.ValueOffset, 4));
        Assert.Equal(3, count);
        Assert.Equal(0.5f, ReadFloat(roundTripped, arrayNode.ValueOffset + 4));
        Assert.Equal(0.6f, ReadFloat(roundTripped, arrayNode.ValueOffset + 8));
        Assert.Equal(0.7f, ReadFloat(roundTripped, arrayNode.ValueOffset + 12));
        Assert.Equal(1000.0f, ReadFloat(roundTripped, poolNode.ValueOffset));
    }

    [Fact]
    public void MaxNeuronalEnergyReportsMissingSlotWithoutWritingAnything()
    {
        var service = new SaveGameCheatService(new SaveGameCheatInjector(), _userData);

        CheatApplyResult result = service.Apply(CheatKind.MaxNeuronalEnergy, "0");

        Assert.False(result.Succeeded);
        Assert.Contains("no save", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.CheckpointId);
    }

    [Fact]
    public void ApplyRefusesWhenTheLiveSaveChangesDuringPreparation()
    {
        Directory.CreateDirectory(Path.Combine(_userData, "SaveGames"));
        string slotPath = Path.Combine(_userData, "SaveGames", "Savegame0.sav");
        byte[] original = SnappyBlockCodec.EncodeLiteral(DecompressedSaveWithRpgArray([0.5f]));
        byte[] foreign = SnappyBlockCodec.EncodeLiteral(DecompressedSaveWithRpgArray([0.9f]));
        File.WriteAllBytes(slotPath, original);
        var service = new SaveGameCheatService(
            new MutatingInjector(slotPath, foreign),
            _userData);

        CheatApplyResult result = service.Apply(CheatKind.MaxNeuronalEnergy, "0");

        Assert.False(result.Succeeded);
        Assert.Contains("live save changed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(foreign, File.ReadAllBytes(slotPath));
        Assert.Empty(SaveGameCheckpointStore.ListCheckpoints(_userData, 0));
    }

    private static SaveGameSchemaNode? FindNode(SaveGameSchemaNode node, string name)
    {
        if (string.Equals(node.Name, name, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (SaveGameSchemaNode child in node.Children)
        {
            SaveGameSchemaNode? found = FindNode(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static byte[] EncodeString(string value)
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] result = new byte[text.Length + 5];
        BinaryPrimitives.WriteInt32LittleEndian(result, text.Length + 1);
        text.CopyTo(result, 4);
        return result;
    }

    private static byte[] DecompressedSaveWithRpgArray(float[] elements)
    {
        using var stream = new MemoryStream();
        stream.Write(EncodeString("NeuronalEnergySources"));
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
        Span<byte> value = stackalloc byte[4];
        foreach (float element in elements)
        {
            BinaryPrimitives.WriteInt32LittleEndian(value, BitConverter.SingleToInt32Bits(element));
            stream.Write(value);
        }

        stream.Write(UnrealTaggedProperties.EncodeFloat("AvailableNeuronalEnergy", 0.02f));
        stream.Write(UnrealTaggedProperties.EncodeTerminator());
        byte[] rpgBody = stream.ToArray();
        using var rpg = new MemoryStream();
        rpg.Write(UnrealTaggedProperties.EncodeStruct("RPGData", "RPGData", rpgBody));
        rpg.Write(UnrealTaggedProperties.EncodeTerminator());
        return rpg.ToArray();
    }

    private static float ReadFloat(byte[] data, int offset) => BitConverter.Int32BitsToSingle(
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(float))));

    private sealed class MutatingInjector(string slotPath, byte[] foreign) : ISaveGameCheatInjector
    {
        private readonly SaveGameCheatInjector _inner = new();

        public CheatInjectionResult TryInject(
            byte[] decompressedSave,
            CheatKind kind,
            out byte[]? modifiedSave)
        {
            File.WriteAllBytes(slotPath, foreign);
            return _inner.TryInject(decompressedSave, kind, out modifiedSave);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_userData))
        {
            Directory.Delete(_userData, recursive: true);
        }
    }
}
