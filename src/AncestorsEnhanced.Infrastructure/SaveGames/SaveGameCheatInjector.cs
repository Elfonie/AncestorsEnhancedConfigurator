using System.Buffers.Binary;
using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Applies safe, schema-verified value injections to a decompressed lineage save.
/// Scalar FloatProperty values are overwritten in place; FloatProperty arrays (e.g.
/// NeuronalEnergySources) have every element set. Nothing is resized, so the
/// tagged-property layout stays valid and the modified bytes re-encode cleanly.
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

        byte[] work = [.. decompressedSave];
        SaveGameSchemaNode root = SaveGameSchemaAnalyzer.Parse(work);
        float value = ValueFor(kind);
        HashSet<string> scalars = ScalarTargetsFor(kind);
        HashSet<string> arrays = ArrayTargetsFor(kind);
        if (scalars.Count == 0 && arrays.Count == 0)
        {
            return new CheatInjectionResult(false, "The cheat has no supported fields.");
        }

        int modified = 0;
        foreach (SaveGameSchemaNode node in SaveGameSchemaAnalyzer.Flatten(root))
        {
            if (IsScalar(node, scalars, work))
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    work.AsSpan(node.ValueOffset, 4),
                    BitConverter.SingleToInt32Bits(value));
                modified++;
            }
            else if (IsFloatArray(node, arrays, work))
            {
                int count = BinaryPrimitives.ReadInt32LittleEndian(
                    work.AsSpan(node.ValueOffset, 4));
                int capped = Math.Min(count, 1_000_000);
                if (node.ValueOffset + 4 + capped * 4 > work.Length)
                {
                    continue;
                }

                for (int element = 0; element < capped; element++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        work.AsSpan(node.ValueOffset + 4 + element * 4, 4),
                        BitConverter.SingleToInt32Bits(value));
                }

                modified += capped;
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
            modified);
    }

    private static bool IsScalar(
        SaveGameSchemaNode node,
        HashSet<string> scalars,
        byte[] work) =>
        string.Equals(node.Type, "FloatProperty", StringComparison.Ordinal) &&
        scalars.Contains(node.Name) &&
        node.ValueLength == 4 &&
        node.ValueOffset + 4 <= work.Length;

    private static bool IsFloatArray(
        SaveGameSchemaNode node,
        HashSet<string> arrays,
        byte[] work) =>
        string.Equals(node.Type, "ArrayProperty", StringComparison.Ordinal) &&
        string.Equals(node.ElementType, "FloatProperty", StringComparison.Ordinal) &&
        arrays.Contains(node.Name) &&
        node.ValueLength >= 4 &&
        node.ValueOffset + 4 <= work.Length;

    private static float ValueFor(CheatKind kind) => kind switch
    {
        CheatKind.MaxNeuronalEnergy => 999_999.0f,
        CheatKind.MaxNeeds => 1_000.0f,
        CheatKind.HealClan => 1.0f,
        CheatKind.ForceMutations => 1.0f,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The cheat kind is unknown."),
    };

    private static HashSet<string> ScalarTargetsFor(CheatKind kind) => kind switch
    {
        CheatKind.MaxNeuronalEnergy => new HashSet<string>(StringComparer.Ordinal) { "Energy" },
        CheatKind.MaxNeeds => new HashSet<string>(StringComparer.Ordinal)
        {
            "RegimenStamina",
            "Energy",
            "Stamina",
        },
        CheatKind.HealClan => new HashSet<string>(StringComparer.Ordinal)
        {
            "Health",
            "Energy",
            "Stamina",
        },
        // ForceMutations needs rework: PendingNodes is an array of names and NodeSaveData is a
        // complex struct; safe float injection does not apply. Revisit with element-aware parsing.
        CheatKind.ForceMutations => [],
        _ => [],
    };

    private static HashSet<string> ArrayTargetsFor(CheatKind kind) => kind switch
    {
        CheatKind.MaxNeuronalEnergy => new HashSet<string>(StringComparer.Ordinal)
        {
            "NeuronalEnergySources",
        },
        CheatKind.MaxNeeds => [],
        CheatKind.HealClan => [],
        // ForceMutations needs rework: PendingNodes is an array of names and NodeSaveData is a
        // complex struct; safe float injection does not apply. Revisit with element-aware parsing.
        CheatKind.ForceMutations => [],
        _ => [],
    };
}