using System.Buffers.Binary;
using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Applies safe, schema-verified value injections to a decompressed lineage save.
/// Targets are structural <see cref="CheatTargetSpec"/>s resolved by their exact schema
/// path, so equally named fields owned by other objects are never touched.
/// Nothing is resized, so the tagged-property layout stays valid and the modified bytes
/// re-encode cleanly. The number of matched nodes must equal the authorised count,
/// otherwise the injection fails closed.
/// </summary>
public sealed class SaveGameCheatInjector : ISaveGameCheatInjector
{
    public CheatInjectionResult TryInject(
        byte[] decompressedSave,
        CheatKind kind,
        out byte[]? modifiedSave)
    {
        ArgumentNullException.ThrowIfNull(decompressedSave);
        modifiedSave = null;

        IReadOnlyList<CheatTargetSpec> targets = SaveGameCheatTargets.CheatTargetsFor(kind);
        if (targets.Count == 0)
        {
            return new CheatInjectionResult(false, "The cheat has no supported fields.");
        }

        byte[] work = [.. decompressedSave];
        SaveGameSchemaNode root = SaveGameSchemaAnalyzer.Parse(work);

        var ranges = new List<ByteRange>();
        var actual = new Dictionary<CheatTargetSpec, int>();
        int modified = ApplyToTree(root, targets, work, [], ranges, actual);

        // Every production target is required.  A missing target is just as unsafe
        // as a duplicate: accepting either case would publish a partially modified
        // save whose real schema has not been established.
        foreach (CheatTargetSpec spec in targets)
        {
            int count = actual.TryGetValue(spec, out int found) ? found : 0;
            if (count != spec.ExpectedMatchCount)
            {
                return new CheatInjectionResult(
                    false,
                    $"The target matched {count} node(s), but exactly {spec.ExpectedMatchCount} were required.");
            }
        }

        if (modified == 0)
        {
            return new CheatInjectionResult(false, "No matching fields were found; nothing was changed.");
        }

        modifiedSave = work;
        return new CheatInjectionResult(
            true,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{kind} applied to {modified} float field(s)."),
            modified,
            ranges);
    }

    private static int ApplyToTree(
        SaveGameSchemaNode node,
        IReadOnlyList<CheatTargetSpec> targets,
        byte[] work,
        List<string> path,
        List<ByteRange> ranges,
        Dictionary<CheatTargetSpec, int> actual)
    {
        int modified = 0;
        path.Add(node.Name);
        if (!node.IsTerminator)
        {
            string nodePath = string.Join("/", path);
            foreach (CheatTargetSpec spec in targets)
            {
                if (string.Equals(nodePath, spec.SchemaPath, StringComparison.Ordinal) &&
                    spec.Matches(node))
                {
                    int patched = PatchNode(node, spec, work, ranges);
                    if (patched > 0)
                    {
                        modified += patched;
                        actual[spec] = actual.TryGetValue(spec, out int count) ? count + 1 : 1;
                    }
                }
            }
        }

        foreach (SaveGameSchemaNode child in node.Children)
        {
            modified += ApplyToTree(child, targets, work, path, ranges, actual);
        }

        path.RemoveAt(path.Count - 1);
        return modified;
    }

    /// <summary>
    /// Patches a scalar FloatProperty or a FloatProperty array in place. Returns the number
    /// of patched float values (0 when the target is malformed), and reports the patched
    /// ranges for later verification.
    /// </summary>
    private static int PatchNode(
        SaveGameSchemaNode node,
        CheatTargetSpec spec,
        byte[] work,
        List<ByteRange> ranges)
    {
        if (spec.IsArray)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(
                work.AsSpan(node.ValueOffset, sizeof(int)));
            if (count < 0 || count > (int.MaxValue - sizeof(int)) / sizeof(float))
            {
                return 0;
            }

            int expectedLength = checked(sizeof(int) + count * sizeof(float));
            if (expectedLength != node.ValueLength ||
                node.ValueOffset < 0 ||
                node.ValueOffset > work.Length - expectedLength)
            {
                return 0;
            }

            for (int element = 0; element < count; element++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    work.AsSpan(node.ValueOffset + sizeof(int) + element * sizeof(float), sizeof(float)),
                    BitConverter.SingleToInt32Bits(spec.TargetValue));
            }

            if (count > 0)
            {
                ranges.Add(new ByteRange(node.ValueOffset + sizeof(int), count * sizeof(float)));
            }

            return count;
        }

        if (node.ValueLength != 4 || node.ValueOffset < 0 || node.ValueOffset > work.Length - 4)
        {
            return 0;
        }

        BinaryPrimitives.WriteInt32LittleEndian(
            work.AsSpan(node.ValueOffset, 4),
            BitConverter.SingleToInt32Bits(spec.TargetValue));
        ranges.Add(new ByteRange(node.ValueOffset, 4));
        return 1;
    }
}
