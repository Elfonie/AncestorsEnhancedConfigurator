using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Read-only navigator over the nested tagged-property schema of a UE4 lineage save.
/// Decodes the Snappy payload and walks the full LineageData tree, recording each
/// property's name, type, struct/enum type, file offset and length. It never writes back.
/// </summary>
public sealed class SaveGameSchemaAnalyzer : ISaveGameSchemaAnalyzer
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Structs whose payload is serialized data (not a nested tagged-property list).
    private static readonly HashSet<string> BinaryStructs = new(StringComparer.Ordinal)
    {
        "Vector",
        "Vector2D",
        "Rotator",
        "Color",
        "LinearColor",
        "Guid",
        "DateTime",
        "Timespan",
        "IntPoint",
        "CharacterName",
    };

    public SaveGameSchemaAnalysis Analyze(byte[] compressedSave)
    {
        ArgumentNullException.ThrowIfNull(compressedSave);
        byte[] data = SnappyBlockCodec.Decode(compressedSave);
        SaveGameSchemaNode root = Parse(data);
        List<SaveGameSchemaNode> findings = FindInterestingNodes(root);
        return new SaveGameSchemaAnalysis([.. Flatten(root)], findings);
    }

    internal static SaveGameSchemaNode Parse(byte[] decompressed)
    {
        ArgumentNullException.ThrowIfNull(decompressed);
        var root = new SaveGameSchemaNode("<save>", null);
        ParsePropertyList(decompressed, 0, decompressed.Length, root, depth: 0);
        return root;
    }

    private static int ParsePropertyList(
        byte[] data,
        int start,
        int limit,
        SaveGameSchemaNode parent,
        int depth)
    {
        int offset = start;
        while (offset < limit)
        {
            int propertyStart = offset;
            string name = ReadString(data, ref offset, limit);
            if (name == "None")
            {
                parent.Children.Add(new SaveGameSchemaNode(name, null)
                {
                    ValueOffset = propertyStart,
                    ValueLength = 0,
                });
                return offset;
            }

            string type = ReadString(data, ref offset, limit);
            long longSize = ReadInt64(data, ref offset, limit);
            if (longSize is < 0 or > int.MaxValue)
            {
                throw new InvalidDataException($"Property {name} has an invalid size.");
            }

            int valueLength = (int)longSize;
            string? structType = null;
            string? enumType = null;
            string? elementType = null;
            switch (type)
            {
                case "StructProperty":
                    structType = ReadString(data, ref offset, limit);
                    Advance(ref offset, 16, limit);
                    break;
                case "EnumProperty":
                case "ByteProperty":
                    enumType = ReadString(data, ref offset, limit);
                    break;
                case "BoolProperty":
                    Advance(ref offset, 1, limit);
                    break;
                case "ArrayProperty":
                case "SetProperty":
                    elementType = ReadString(data, ref offset, limit);
                    break;
                case "MapProperty":
                    _ = ReadString(data, ref offset, limit);
                    _ = ReadString(data, ref offset, limit);
                    break;
            }

            Advance(ref offset, 1, limit);
            byte hasPropertyGuid = data[offset - 1];
            if (hasPropertyGuid > 1)
            {
                throw new InvalidDataException($"Property {name} has an invalid GUID marker.");
            }

            if (hasPropertyGuid == 1)
            {
                Advance(ref offset, 16, limit);
            }

            int valueOffset = offset;
            Advance(ref offset, valueLength, limit);

            var node = new SaveGameSchemaNode(name, type)
            {
                StructType = structType,
                EnumType = enumType,
                ElementType = elementType,
                ValueOffset = valueOffset,
                ValueLength = valueLength,
            };
            parent.Children.Add(node);

            bool isBinaryStruct = structType is not null && BinaryStructs.Contains(structType);
            if (type == "StructProperty" && valueLength > 0 && depth < 40 && !isBinaryStruct)
            {
                ParsePropertyList(data, valueOffset, valueOffset + valueLength, node, depth + 1);
            }
        }

        throw new InvalidDataException("A save property list has no None terminator.");
    }

    private static List<SaveGameSchemaNode> FindInterestingNodes(SaveGameSchemaNode root)
    {
        var findings = new List<SaveGameSchemaNode>();
        var interesting = new HashSet<string>(StringComparer.Ordinal)
        {
            "NeuronalEnergySources",
            "PendingNodes",
            "FoodNeedState",
            "Liquid",
            "Sleep",
            "BleedData",
            "Vitality",
            "Stamina",
            "Energy",
            "VenomPoison",
        };
        foreach (SaveGameSchemaNode node in Flatten(root))
        {
            if (!node.IsTerminator && string.Equals(node.Name, "None", StringComparison.Ordinal) is false &&
                (interesting.Contains(node.Name) ||
                 node.Name.Contains("Neuronal", StringComparison.OrdinalIgnoreCase) ||
                 node.Name.Contains("Need", StringComparison.OrdinalIgnoreCase) ||
                 node.Name.Contains("Mut", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(node);
            }
        }

        return findings;
    }

    internal static IEnumerable<SaveGameSchemaNode> Flatten(SaveGameSchemaNode node)
    {
        yield return node;
        foreach (SaveGameSchemaNode child in node.Children)
        {
            foreach (SaveGameSchemaNode descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static string ReadString(byte[] data, ref int offset, int limit)
    {
        int start = offset;
        int length = ReadInt32(data, ref offset, limit);
        if (length <= 0 || length > limit - offset || data[offset + length - 1] != 0)
        {
            throw new InvalidDataException(
                $"A save contains an invalid Unreal string at offset 0x{start:X}.");
        }

        string value = StrictUtf8.GetString(data, offset, length - 1);
        offset += length;
        return value;
    }

    private static int ReadInt32(byte[] data, ref int offset, int limit)
    {
        Advance(ref offset, 4, limit);
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset - 4, 4));
    }

    private static long ReadInt64(byte[] data, ref int offset, int limit)
    {
        Advance(ref offset, 8, limit);
        return BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset - 8, 8));
    }

    private static void Advance(ref int offset, int count, int limit)
    {
        if (count < 0 || offset < 0 || offset > limit - count)
        {
            throw new InvalidDataException("A save is truncated or malformed.");
        }

        offset += count;
    }
}