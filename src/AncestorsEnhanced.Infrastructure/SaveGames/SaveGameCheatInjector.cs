using System.Buffers.Binary;
using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Applies safe, schema-verified value injections to a decompressed lineage save.
/// Only known object roots are patched: the active character's vitality/health and
/// the neuronal energy array. Equally named fields owned by other objects are left
/// untouched. Nothing is resized, so the tagged-property layout stays valid and the
/// modified bytes re-encode cleanly.
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
        var scalarTargets = ScalarTargetsFor(kind);
        var arrayTargets = ArrayTargetsFor(kind);
        if (scalarTargets.Count == 0 && arrayTargets.Count == 0)
        {
            return new CheatInjectionResult(false, "The cheat has no supported fields.");
        }

        int modified = ApplyToTree(root, kind, value, scalarTargets, arrayTargets, work, []);

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

    private static int ApplyToTree(
        SaveGameSchemaNode node,
        CheatKind kind,
        float value,
        HashSet<string> scalarTargets,
        HashSet<string> arrayTargets,
        byte[] work,
        List<string> path)
    {
        int modified = 0;
        path.Add(node.Name);
        if (!node.IsTerminator &&
            IsScalar(node, scalarTargets, work) &&
            IsUnderAllowedPath(path, kind, isArray: false))
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                work.AsSpan(node.ValueOffset, 4),
                BitConverter.SingleToInt32Bits(value));
            modified++;
        }
        else if (IsFloatArray(node, arrayTargets, work) && IsUnderAllowedPath(path, kind, isArray: true))
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(
                work.AsSpan(node.ValueOffset, 4));
            int capped = Math.Min(count, 1_000_000);
            if (node.ValueOffset + 4 + capped * 4 <= work.Length)
            {
                for (int element = 0; element < capped; element++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        work.AsSpan(node.ValueOffset + 4 + element * 4, 4),
                        BitConverter.SingleToInt32Bits(value));
                }

                modified += capped;
            }
        }

        foreach (SaveGameSchemaNode child in node.Children)
        {
            modified += ApplyToTree(child, kind, value, scalarTargets, arrayTargets, work, path);
        }

        path.RemoveAt(path.Count - 1);
        return modified;
    }

    private static bool IsUnderAllowedPath(
        List<string> path,
        CheatKind kind,
        bool isArray)
    {
        string[] allowedRoots = AllowedRootsFor(kind, isArray);
        if (allowedRoots.Length == 0)
        {
            return false;
        }

            if (kind == CheatKind.HealClan && IsInsideCharacterDataList(path))
            {
                // Clan members live in PlayerClanData/CharacterDataList. Every
                // CharacterData struct there is a valid target for the clan-wide heal.
                return true;
            }

        foreach (string root in allowedRoots)
        {
            string[] segments = root.Split('/');
            // The path starts with the synthetic "<save>" root; the allowed root must
            // match the path prefix starting right after it.
            if (path.Count < segments.Length + 1)
            {
                continue;
            }

            bool match = true;
            for (int index = 0; index < segments.Length; index++)
            {
                if (!string.Equals(path[index + 1], segments[index], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    // true when the current path descends into PlayerClanData/CharacterDataList.
    private static bool IsInsideCharacterDataList(List<string> path)
    {
        // path = ["<save>", "PlayerClanData", "CharacterDataList", ...] (or deeper after a list element).
        int clanIndex = IndexOf(path, "PlayerClanData");
        return clanIndex >= 0 &&
               path.Count > clanIndex + 1 &&
               string.Equals(path[clanIndex + 1], "CharacterDataList", StringComparison.Ordinal);
    }

    private static int IndexOf(List<string> path, string segment)
    {
        for (int i = 0; i < path.Count; i++)
        {
            if (string.Equals(path[i], segment, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string[] AllowedRootsFor(CheatKind kind, bool isArray) => kind switch
    {
        CheatKind.MaxNeuronalEnergy when isArray =>
            ["RPGData/NeuronalEnergySources"],
        CheatKind.MaxNeeds =>
            ["PlayerControllerData/CharacterData/VitalityData"],
        CheatKind.HealClan =>
            [
                "PlayerControllerData/CharacterData/VitalityData",
                "PlayerControllerData/CharacterData/HealthData",
            ],
        _ => [],
    };

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
        // MaxNeuronalEnergy patches only the NeuronalEnergySources array, not the
        // character's global Energy scalar.
        CheatKind.MaxNeuronalEnergy => new HashSet<string>(StringComparer.Ordinal),
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
        CheatKind.ForceMutations => [],
        _ => [],
    };
}
