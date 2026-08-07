using System.Buffers.Binary;
using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Infrastructure.SystemSave;

internal static class AncestorsSystemSaveCodec
{
    /// <summary>Upper bound for a re-encoded System.sav (256 KiB).</summary>
    private const int MaximumOutputSizeBytes = 256 * 1024;

    public static SystemGraphicsSettingsSnapshot Read(byte[] file)
    {
        byte[] data = SnappyBlockCodec.Decode(file);
        SaveLayout layout = ReadLayout(data);
        TaggedProperty fullscreen = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, "FullScreenResolution", "StructProperty");
        TaggedProperty windowed = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, "WindowedResolution", "StructProperty");
        TaggedProperty brightness = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, "Brightness", "FloatProperty");
        TaggedProperty overall = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, "QualityLevel", "IntProperty");
        TaggedProperty frameRate = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, "FrameRateLimit", "IntProperty");
        TaggedProperty? custom = UnrealTaggedProperties.FindOptional(
            layout.GraphicsProperties, "QualitySettingIsCustom", "BoolProperty");
        if (fullscreen.StructType != "IntPoint" || windowed.StructType != "IntPoint")
        {
            throw new InvalidDataException("System.sav contains an unsupported resolution structure.");
        }

        (int fullscreenWidth, int fullscreenHeight) = UnrealTaggedProperties.ReadIntPoint(data, fullscreen);
        (int windowedWidth, int windowedHeight) = UnrealTaggedProperties.ReadIntPoint(data, windowed);
        GameGraphicsQuality overallQuality = ParseOverallQuality(
            UnrealTaggedProperties.ReadIntValue(data, overall));
        int frameRateIndex = UnrealTaggedProperties.ReadIntValue(data, frameRate);
        if ((uint)frameRateIndex >= SystemGraphicsOptionCatalog.FrameRateLimits.Count)
        {
            throw new InvalidDataException("System.sav contains an unsupported frame-rate limit.");
        }

        float brightnessValue = UnrealTaggedProperties.ReadFloatValue(data, brightness);
        if (!float.IsFinite(brightnessValue))
        {
            throw new InvalidDataException("System.sav contains an invalid brightness value.");
        }

        return new SystemGraphicsSettingsSnapshot(
            fullscreenWidth,
            fullscreenHeight,
            windowedWidth,
            windowedHeight,
            brightnessValue,
            overallQuality,
            ReadQuality(data, layout.ScalabilityProperties, "ViewDistance", overallQuality),
            ReadQuality(data, layout.ScalabilityProperties, "PostProcessing", overallQuality),
            ReadQuality(data, layout.ScalabilityProperties, "Shadows", overallQuality),
            ReadQuality(data, layout.ScalabilityProperties, "Textures", overallQuality),
            ReadQuality(data, layout.ScalabilityProperties, "VisualEffects", overallQuality),
            ReadQuality(data, layout.ScalabilityProperties, "Foliage", overallQuality),
            SystemGraphicsOptionCatalog.FrameRateLimits[frameRateIndex],
            custom is not null && UnrealTaggedProperties.ReadBoolValue(data, custom));
    }

    public static byte[] Apply(byte[] file, IReadOnlyDictionary<string, string> changes)
    {
        byte[] data = SnappyBlockCodec.Decode(file);
        bool changedQuality = false;
        foreach ((string key, string value) in changes)
        {
            switch (key)
            {
                case SystemSaveSettingKeys.FullscreenResolution:
                    data = SetIntPoint(data, "FullScreenResolution", value);
                    break;
                case SystemSaveSettingKeys.WindowedResolution:
                    data = SetIntPoint(data, "WindowedResolution", value);
                    break;
                case SystemSaveSettingKeys.Brightness:
                    data = SetFloat(data, "Brightness", ParseBrightness(value));
                    break;
                case SystemSaveSettingKeys.FrameRateLimit:
                    data = SetInt(data, "FrameRateLimit", ParseFrameRateIndex(value));
                    break;
                case SystemSaveSettingKeys.ViewDistanceQuality:
                    data = SetQuality(data, "ViewDistance", ParseQuality(value));
                    changedQuality = true;
                    break;
                case SystemSaveSettingKeys.PostProcessingQuality:
                    data = SetQuality(data, "PostProcessing", ParseQuality(value));
                    changedQuality = true;
                    break;
                case SystemSaveSettingKeys.ShadowQuality:
                    data = SetQuality(data, "Shadows", ParseQuality(value));
                    changedQuality = true;
                    break;
                case SystemSaveSettingKeys.TextureQuality:
                    data = SetQuality(data, "Textures", ParseQuality(value));
                    changedQuality = true;
                    break;
                case SystemSaveSettingKeys.VisualEffectsQuality:
                    data = SetQuality(data, "VisualEffects", ParseQuality(value));
                    changedQuality = true;
                    break;
                case SystemSaveSettingKeys.FoliageQuality:
                    data = SetQuality(data, "Foliage", ParseQuality(value));
                    changedQuality = true;
                    break;
                default:
                    throw new InvalidOperationException($"{key} is not a supported System.sav setting.");
            }
        }

        if (changedQuality)
        {
            SystemGraphicsSettingsSnapshot settings = Read(SnappyBlockCodec.EncodeLiteral(data));
            bool isCustom =
                settings.ViewDistanceQuality != settings.OverallQuality ||
                settings.PostProcessingQuality != settings.OverallQuality ||
                settings.ShadowQuality != settings.OverallQuality ||
                settings.TextureQuality != settings.OverallQuality ||
                settings.VisualEffectsQuality != settings.OverallQuality ||
                settings.FoliageQuality != settings.OverallQuality;
            data = SetBool(data, "QualitySettingIsCustom", isCustom);
        }

        byte[] encoded = SnappyBlockCodec.EncodeLiteral(data);
        if (encoded.Length > MaximumOutputSizeBytes)
        {
            // A recompressed System.sav that grows past the supported bound would be a
            // disproportionate regression (F097); abort instead of writing it.
            throw new InvalidOperationException("The updated System.sav exceeds the supported size and was not written.");
        }

        _ = Read(encoded);
        return encoded;
    }

    private static SaveLayout ReadLayout(byte[] data)
    {
        IReadOnlyList<TaggedProperty> root = UnrealTaggedProperties.Read(data, 0, data.Length);
        TaggedProperty options = UnrealTaggedProperties.Require(root, "Options", "StructProperty");
        if (options.StructType != "PanacheOptions")
        {
            throw new InvalidDataException("System.sav has an unsupported options structure.");
        }

        IReadOnlyList<TaggedProperty> optionsProperties = UnrealTaggedProperties.Read(
            data, options.ValueOffset, options.ValueLength);
        TaggedProperty graphics = UnrealTaggedProperties.Require(
            optionsProperties, "GraphicOptions", "StructProperty");
        if (graphics.StructType != "GraphicsOptions")
        {
            throw new InvalidDataException("System.sav has an unsupported graphics structure.");
        }

        IReadOnlyList<TaggedProperty> graphicsProperties = UnrealTaggedProperties.Read(
            data, graphics.ValueOffset, graphics.ValueLength);
        TaggedProperty scalability = UnrealTaggedProperties.Require(
            graphicsProperties, "ScalabilitySetting", "StructProperty");
        if (scalability.StructType != "ScalabilitySetting")
        {
            throw new InvalidDataException("System.sav has an unsupported scalability structure.");
        }

        IReadOnlyList<TaggedProperty> scalabilityProperties = UnrealTaggedProperties.Read(
            data, scalability.ValueOffset, scalability.ValueLength);
        return new SaveLayout(
            options,
            graphics,
            scalability,
            graphicsProperties,
            scalabilityProperties);
    }

    private static byte[] SetQuality(byte[] data, string propertyName, GameGraphicsQuality quality)
    {
        SaveLayout layout = ReadLayout(data);
        TaggedProperty? property = UnrealTaggedProperties.FindOptional(
            layout.ScalabilityProperties, propertyName, "EnumProperty");
        byte[] encoded = UnrealTaggedProperties.EncodeEnum(propertyName, quality.ToString());
        int start = property?.Start ?? layout.ScalabilityProperties.Single(candidate => candidate.IsTerminator).Start;
        int length = property is null ? 0 : property.End - property.Start;
        return ReplaceAndResize(data, start, length, encoded, layout.Options, layout.Graphics, layout.Scalability);
    }

    private static byte[] SetInt(byte[] data, string propertyName, int value)
    {
        SaveLayout layout = ReadLayout(data);
        TaggedProperty property = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, propertyName, "IntProperty");
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(property.ValueOffset, 4), value);
        return data;
    }

    private static byte[] SetFloat(byte[] data, string propertyName, float value)
    {
        SaveLayout layout = ReadLayout(data);
        TaggedProperty property = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, propertyName, "FloatProperty");
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(property.ValueOffset, 4),
            BitConverter.SingleToInt32Bits(value));
        return data;
    }

    private static byte[] SetIntPoint(byte[] data, string propertyName, string value)
    {
        (int x, int y) = ParseResolution(value);
        SaveLayout layout = ReadLayout(data);
        TaggedProperty property = UnrealTaggedProperties.Require(
            layout.GraphicsProperties, propertyName, "StructProperty");
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(property.ValueOffset, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(property.ValueOffset + 4, 4), y);
        return data;
    }

    private static byte[] SetBool(byte[] data, string propertyName, bool value)
    {
        SaveLayout layout = ReadLayout(data);
        TaggedProperty? property = UnrealTaggedProperties.FindOptional(
            layout.GraphicsProperties, propertyName, "BoolProperty");
        if (property?.BooleanValueOffset is int offset)
        {
            data[offset] = value ? (byte)1 : (byte)0;
            return data;
        }

        // If the property exists under a different type, adding a new BoolProperty
        // would create a duplicate entry and corrupt the save. Treat it as an error.
        if (layout.GraphicsProperties.Any(existing => string.Equals(
                existing.Name, propertyName, StringComparison.Ordinal) && !existing.IsTerminator))
        {
            throw new InvalidDataException($"System.sav contains {propertyName} with an unsupported type.");
        }

        byte[] encoded = UnrealTaggedProperties.EncodeBool(propertyName, value);
        int start = layout.GraphicsProperties.Single(candidate => candidate.IsTerminator).Start;
        return ReplaceAndResize(data, start, 0, encoded, layout.Options, layout.Graphics);
    }

    private static byte[] ReplaceAndResize(
        byte[] data,
        int start,
        int oldLength,
        byte[] replacement,
        params TaggedProperty[] containers)
    {
        int delta = replacement.Length - oldLength;
        byte[] result = new byte[checked(data.Length + delta)];
        data.AsSpan(0, start).CopyTo(result);
        replacement.CopyTo(result, start);
        data.AsSpan(start + oldLength).CopyTo(result.AsSpan(start + replacement.Length));
        foreach (TaggedProperty container in containers)
        {
            long oldSize = BinaryPrimitives.ReadInt64LittleEndian(
                result.AsSpan(container.SizeOffset, 8));
            BinaryPrimitives.WriteInt64LittleEndian(
                result.AsSpan(container.SizeOffset, 8),
                checked(oldSize + delta));
        }

        return result;
    }

    private static GameGraphicsQuality ReadQuality(
        byte[] data,
        IReadOnlyList<TaggedProperty> properties,
        string name,
        GameGraphicsQuality fallback)
    {
        TaggedProperty? property = UnrealTaggedProperties.FindOptional(
            properties, name, "EnumProperty");
        if (property is not null && property.EnumType != "EGraphicsQualitySettings")
        {
            throw new InvalidDataException($"System.sav contains an unsupported enum type for {name}.");
        }
        return property is null
            ? fallback
            : ParseQuality(UnrealTaggedProperties.ReadValueString(data, property));
    }

    private static GameGraphicsQuality ParseOverallQuality(int value) => value switch
    {
        0 => GameGraphicsQuality.Low,
        1 => GameGraphicsQuality.Medium,
        2 => GameGraphicsQuality.High,
        _ => throw new InvalidDataException("System.sav contains an unsupported overall quality level."),
    };

    private static GameGraphicsQuality ParseQuality(string value)
    {
        string name = value[(value.LastIndexOf(':') + 1)..];
        // Only the explicitly defined member names are accepted. A numeric value like
        // "3" (or any other undefined member) would otherwise pass Enum.TryParse and
        // surface as an out-of-range quality.
        return Enum.TryParse(name, ignoreCase: true, out GameGraphicsQuality quality) &&
               Enum.IsDefined(quality)
            ? quality
            : throw new InvalidDataException($"System.sav contains an unsupported quality value {value}.");
    }

    private static int ParseFrameRateIndex(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frameRate))
        {
            throw new InvalidOperationException("The frame-rate limit is invalid.");
        }

        int index = SystemGraphicsOptionCatalog.GetFrameRateIndex(frameRate);
        return index >= 0
            ? index
            : throw new InvalidOperationException("The frame-rate limit is not supported by Ancestors.");
    }

    private static float ParseBrightness(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float brightness) ||
            !float.IsFinite(brightness) ||
            brightness is < 0.5f or > 1.5f)
        {
            throw new InvalidOperationException("The brightness value is outside the supported range.");
        }

        return brightness;
    }

    private static (int X, int Y) ParseResolution(string value)
    {
        if (!SystemGraphicsOptionCatalog.IsSupportedResolution(value))
        {
            throw new InvalidOperationException("The resolution is not supported by Ancestors.");
        }

        string[] parts = value.Split('x');
        return (
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private sealed record SaveLayout(
        TaggedProperty Options,
        TaggedProperty Graphics,
        TaggedProperty Scalability,
        IReadOnlyList<TaggedProperty> GraphicsProperties,
        IReadOnlyList<TaggedProperty> ScalabilityProperties);
}
