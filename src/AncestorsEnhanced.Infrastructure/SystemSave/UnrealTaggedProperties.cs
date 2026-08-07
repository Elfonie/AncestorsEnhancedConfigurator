using System.Buffers.Binary;
using System.Text;

namespace AncestorsEnhanced.Infrastructure.SystemSave;

internal sealed record TaggedProperty(
    string Name,
    string? Type,
    int Start,
    int SizeOffset,
    int ValueOffset,
    int ValueLength,
    int End,
    string? StructType = null,
    string? EnumType = null,
    int? BooleanValueOffset = null)
{
    public bool IsTerminator => Type is null;
}

internal static class UnrealTaggedProperties
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<TaggedProperty> Read(byte[] data, int start, int length)
    {
        int limit = checked(start + length);
        int offset = start;
        List<TaggedProperty> properties = [];
        while (offset < limit)
        {
            int propertyStart = offset;
            string name = ReadString(data, ref offset, limit);
            if (name == "None")
            {
                properties.Add(new TaggedProperty(name, null, propertyStart, -1, offset, 0, offset));
                return properties;
            }

            string type = ReadString(data, ref offset, limit);
            int sizeOffset = offset;
            long longSize = ReadInt64(data, ref offset, limit);
            if (longSize is < 0 or > int.MaxValue)
            {
                throw new InvalidDataException($"Property {name} has an invalid size.");
            }

            int valueLength = (int)longSize;
            string? structType = null;
            string? enumType = null;
            int? booleanOffset = null;
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
                    booleanOffset = offset;
                    Advance(ref offset, 1, limit);
                    break;
                case "ArrayProperty":
                case "SetProperty":
                    _ = ReadString(data, ref offset, limit);
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
            properties.Add(new TaggedProperty(
                name,
                type,
                propertyStart,
                sizeOffset,
                valueOffset,
                valueLength,
                offset,
                structType,
                enumType,
                booleanOffset));
        }

        throw new InvalidDataException("A System.sav property list has no None terminator.");
    }

    public static TaggedProperty Require(
        IReadOnlyList<TaggedProperty> properties,
        string name,
        string type) =>
        FindOptional(properties, name, type)
        ?? throw new InvalidDataException($"System.sav is missing {name} as {type}.");

    public static TaggedProperty? FindOptional(
        IReadOnlyList<TaggedProperty> properties,
        string name,
        string type)
    {
        TaggedProperty[] matches = [.. properties.Where(property =>
                string.Equals(property.Name, name, StringComparison.Ordinal) &&
                string.Equals(property.Type, type, StringComparison.Ordinal))
            .Take(2)];
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidDataException($"System.sav contains {name} more than once."),
        };
    }

    public static string ReadValueString(byte[] data, TaggedProperty property)
    {
        int offset = property.ValueOffset;
        string value = ReadString(data, ref offset, property.End);
        if (offset != property.End)
        {
            throw new InvalidDataException($"Property {property.Name} contains unexpected trailing data.");
        }

        return value;
    }

    public static int ReadIntValue(byte[] data, TaggedProperty property)
    {
        RequireLength(property, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(property.ValueOffset, 4));
    }

    public static float ReadFloatValue(byte[] data, TaggedProperty property)
    {
        RequireLength(property, 4);
        return BitConverter.Int32BitsToSingle(ReadIntValue(data, property));
    }

    public static (int X, int Y) ReadIntPoint(byte[] data, TaggedProperty property)
    {
        RequireLength(property, 8);
        return (
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(property.ValueOffset, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(property.ValueOffset + 4, 4)));
    }

    public static bool ReadBoolValue(byte[] data, TaggedProperty property)
    {
        RequireLength(property, 0);
        if (property.BooleanValueOffset is not int offset)
        {
            throw new InvalidDataException($"Property {property.Name} has no Boolean value.");
        }

        byte value = data[offset];
        // Only 0 and 1 are valid stored booleans. Any other byte is a structural
        // error rather than truthy data.
        if (value is not (0 or 1))
        {
            throw new InvalidDataException($"Property {property.Name} has an invalid Boolean value.");
        }

        return value == 1;
    }

    public static byte[] EncodeEnum(string name, string value)
    {
        const string enumType = "EGraphicsQualitySettings";
        byte[] payload = EncodeString($"{enumType}::{value.ToUpperInvariant()}");
        return EncodeProperty(name, "EnumProperty", payload, EncodeString(enumType));
    }

    public static byte[] EncodeInt(string name, int value)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value);
        return EncodeProperty(name, "IntProperty", payload, []);
    }

    public static byte[] EncodeFloat(string name, float value) =>
        EncodeIntPropertyBytes(name, "FloatProperty", BitConverter.SingleToInt32Bits(value));

    public static byte[] EncodeIntPoint(string name, int x, int y)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), y);
        byte[] metadata = Combine(EncodeString("IntPoint"), new byte[16]);
        return EncodeProperty(name, "StructProperty", payload, metadata);
    }

    public static byte[] EncodeBool(string name, bool value)
    {
        using var output = new MemoryStream();
        output.Write(EncodeString(name));
        output.Write(EncodeString("BoolProperty"));
        output.Write(new byte[8]);
        output.WriteByte(value ? (byte)1 : (byte)0);
        output.WriteByte(0);
        return output.ToArray();
    }

    public static byte[] EncodeStruct(string name, string structType, byte[] payload) =>
        EncodeProperty(
            name,
            "StructProperty",
            payload,
            Combine(EncodeString(structType), new byte[16]));

    public static byte[] EncodeTerminator() => EncodeString("None");

    private static byte[] EncodeIntPropertyBytes(string name, string type, int bits)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, bits);
        return EncodeProperty(name, type, payload, []);
    }

    private static byte[] EncodeProperty(
        string name,
        string type,
        byte[] payload,
        byte[] metadata)
    {
        using var output = new MemoryStream();
        output.Write(EncodeString(name));
        output.Write(EncodeString(type));
        Span<byte> number = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(number, payload.Length);
        output.Write(number);
        output.Write(metadata);
        output.WriteByte(0);
        output.Write(payload);
        return output.ToArray();
    }

    private static byte[] EncodeString(string value)
    {
        byte[] text = StrictUtf8.GetBytes(value);
        byte[] result = new byte[text.Length + 5];
        BinaryPrimitives.WriteInt32LittleEndian(result, text.Length + 1);
        text.CopyTo(result, 4);
        return result;
    }

    private static string ReadString(byte[] data, ref int offset, int limit)
    {
        int start = offset;
        int length = ReadInt32(data, ref offset, limit);
        if (length <= 0 || length > limit - offset || data[offset + length - 1] != 0)
        {
            throw new InvalidDataException(
                $"System.sav contains an invalid Unreal string at offset 0x{start:X}.");
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
            throw new InvalidDataException("System.sav is truncated or malformed.");
        }

        offset += count;
    }

    private static void RequireLength(TaggedProperty property, int length)
    {
        if (property.ValueLength != length)
        {
            throw new InvalidDataException($"Property {property.Name} has an unexpected size.");
        }
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }
}
