using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Orchestrates a cheat injection: reads the live slot save, decompresses it, applies
/// the chosen injection, recompresses, and only then stores the modified save as a NEW
/// checkpoint after verifying the compress/decompress round trip, the re-parsed schema
/// and the patched byte ranges. The live slot file is never overwritten, so the
/// original is always recoverable.
/// </summary>
public sealed class SaveGameCheatService : ISaveGameCheatService
{
    private readonly ISaveGameCheatInjector _injector;
    private readonly string _userDataDirectory;

    public SaveGameCheatService(
        ISaveGameCheatInjector injector,
        string userDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(userDataDirectory);
        _injector = injector;
        _userDataDirectory = userDataDirectory;
    }

    public CheatApplyResult Apply(CheatKind kind, string slotNumber)
    {
        if (!int.TryParse(slotNumber, out int slot) ||
            slot < 0 ||
            slot >= SaveGamePaths.SlotCount)
        {
            return new CheatApplyResult(false, "The save slot is invalid.");
        }

        SaveGameGuard.ValidateSlot(_userDataDirectory, slot);
        if (IsAncestorsRunning())
        {
            return new CheatApplyResult(false, "Close Ancestors before applying a cheat.");
        }

        try
        {
            string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slot);
            if (!File.Exists(slotPath))
            {
                return new CheatApplyResult(false, $"There is no save in slot {slot} to modify.");
            }

            byte[] compressed = ReadSaveWithRetries(slotPath);
            byte[] decompressed = SnappyBlockCodec.Decode(compressed);

            CheatInjectionResult injected = _injector.TryInject(
                decompressed,
                kind,
                out byte[]? modified);
            if (!injected.Succeeded || modified is null)
            {
                return new CheatApplyResult(false, injected.Message);
            }

            byte[] recompressed = SnappyBlockCodec.EncodeLiteral(modified);

            // Pre-store validation: a checkpointed cheat must survive a full
            // compress -> decompress round trip, re-parse to the same schema, keep
            // every patch inside the payload and expose the expected target nodes
            // with the expected values.
            byte[] roundTripped;
            SaveGameSchemaNode roundTripRoot;
            try
            {
                roundTripped = SnappyBlockCodec.Decode(recompressed);
                roundTripRoot = SaveGameSchemaAnalyzer.Parse(roundTripped);
            }
            catch (InvalidDataException exception)
            {
                return new CheatApplyResult(false, $"The modified save failed compression validation: {exception.Message}");
            }

            if (!IsByteRoundTripFaithful(modified, roundTripped))
            {
                return new CheatApplyResult(false, "The modified save failed the byte round-trip check.");
            }

            foreach (ByteRange range in injected.ModifiedRanges)
            {
                if (!IsRangeInsidePayload(roundTripped, range))
                {
                    return new CheatApplyResult(false, "The modified save failed the byte-range check.");
                }
            }

            if (!VerifyPatchedFields(roundTripRoot, kind, injected.ModifiedRanges, roundTripped))
            {
                return new CheatApplyResult(false, "The modified save failed field verification.");
            }

            if (!IsDiffConfinedToRanges(decompressed, modified, injected.ModifiedRanges))
            {
                return new CheatApplyResult(false, "The modified save changed bytes outside the reported ranges.");
            }

            var store = new SaveGameCheckpointStore(
                () => DateTimeOffset.UtcNow,
                maxCheckpointsPerSlot: 50);
            string checkpointId = AncestorsEnhanced.Infrastructure.Editing.MutationCoordinator.Run(
                () => store.Create(_userDataDirectory, slot, recompressed, $"Cheat:{kind}"));

            return new CheatApplyResult(
                true,
                $"{kind} applied and saved as a new checkpoint for slot {slot}.",
                checkpointId);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            return new CheatApplyResult(false, $"Nothing was applied: {exception.Message}");
        }
    }

    private static bool IsByteRoundTripFaithful(byte[] deferred, byte[] roundTripped) =>
        deferred.AsSpan().SequenceEqual(roundTripped);

    private static bool IsRangeInsidePayload(byte[] payload, ByteRange range) =>
        range.Offset >= 0 && range.Length > 0 && range.EndExclusive <= payload.Length;

    /// <summary>
    /// Verifies that the difference between the original and the modified save is
    /// confined to the reported modified ranges: no byte outside any reported range
    /// may have changed, and every reported range must be bounded by the payload. This
    /// proves exactly which bytes the cheat altered (F025).
    /// </summary>
    private static bool IsDiffConfinedToRanges(
        byte[] original,
        byte[] modified,
        IReadOnlyList<ByteRange> ranges)
    {
        if (original.Length != modified.Length || ranges.Count == 0)
        {
            return false;
        }

        for (int index = 0; index < original.Length; index++)
        {
            if (original[index] == modified[index])
            {
                continue;
            }

            bool insideAnyRange = false;
            foreach (ByteRange range in ranges)
            {
                if (range.Offset >= 0 && range.Length > 0 &&
                    (long)range.Offset <= index && index < range.EndExclusive)
                {
                    insideAnyRange = true;
                    break;
                }
            }

            if (!insideAnyRange)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Every reported patched range must resolve to a schema node of the exact expected
    /// name and type whose stored float value(s) equal the injected target value.
    /// </summary>
    private static bool VerifyPatchedFields(
        SaveGameSchemaNode root,
        CheatKind kind,
        IReadOnlyList<ByteRange> ranges,
        byte[] payload)
    {
        if (ranges.Count == 0)
        {
            return false;
        }

        (HashSet<string> Names, bool IsArray, float Target) expected = ExpectedTargetFor(kind);
        List<SaveGameSchemaNode> candidates = EnumerateSchemaNodes(root)
            .Where(node => !node.IsTerminator && expected.Names.Contains(node.Name))
            .ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        foreach (ByteRange range in ranges)
        {
            SaveGameSchemaNode? match = candidates.FirstOrDefault(node => {
                long nodeEnd = (long)node.ValueOffset + node.ValueLength;
                return range.Offset >= node.ValueOffset && range.EndExclusive <= nodeEnd;
            });
            if (match is null)
            {
                return false;
            }

            bool isArray = string.Equals(match.Type, "ArrayProperty", StringComparison.Ordinal) &&
                string.Equals(match.ElementType, "FloatProperty", StringComparison.Ordinal);
            bool isScalar = string.Equals(match.Type, "FloatProperty", StringComparison.Ordinal);
            if (isArray != expected.IsArray || (!isArray && !isScalar))
            {
                return false;
            }

            if (!VerifyValueAt(range, expected.Target, payload))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerifyValueAt(ByteRange range, float expected, byte[] payload)
    {
        if (range.Offset < 0 || range.Length < 0 || range.EndExclusive > payload.Length)
        {
            return false;
        }

        if ((range.EndExclusive - range.Offset) % 4 != 0)
        {
            return false;
        }

        for (int offset = range.Offset; offset + 4 <= range.EndExclusive; offset += 4)
        {
            float actual = BitConverter.Int32BitsToSingle(
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(offset, 4)));
            if (actual != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static (HashSet<string> Names, bool IsArray, float Target) ExpectedTargetFor(CheatKind kind) =>
        kind switch
        {
            CheatKind.MaxNeuronalEnergy => (new HashSet<string>(StringComparer.Ordinal) { "NeuronalEnergySources" }, true, 999_999.0f),
            CheatKind.MaxNeeds => (new HashSet<string>(StringComparer.Ordinal) { "RegimenStamina", "Energy", "Stamina" }, false, 1_000.0f),
            CheatKind.HealClan => (new HashSet<string>(StringComparer.Ordinal) { "Health", "Energy", "Stamina" }, false, 1.0f),
            CheatKind.ForceMutations => (new HashSet<string>(StringComparer.Ordinal) { "ForceMutations" }, false, 1.0f),
            _ => (new HashSet<string>(StringComparer.Ordinal), false, 0.0f),
        };

    private static IEnumerable<SaveGameSchemaNode> EnumerateSchemaNodes(SaveGameSchemaNode root)
    {
        var stack = new Stack<SaveGameSchemaNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            SaveGameSchemaNode node = stack.Pop();
            yield return node;
            foreach (SaveGameSchemaNode child in node.Children)
            {
                stack.Push(child);
            }
        }
    }

    private static bool IsAncestorsRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("Ancestors-Win64-Shipping").Length > 0 ||
                   System.Diagnostics.Process.GetProcessesByName("Ancestors").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static byte[] ReadSaveWithRetries(string slotPath)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    slotPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(150);
            }
        }

        throw new IOException("The save file is locked by the game and could not be read.");
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}
