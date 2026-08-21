using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.SystemSave;
using AncestorsEnhanced.Infrastructure.Platform;

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
    private readonly Func<bool>? _revalidate;
    private readonly int _maxCheckpointsPerSlot;

    /// <summary>Binds to a verified game context; the user-data path comes from the context (F078).</summary>
    public SaveGameCheatService(VerifiedGameContext context, GameContextVerifier verifier)
        : this(new SaveGameCheatInjector(), context.UserDataDirectory, () => verifier.Verify(context))
    {
    }

    public SaveGameCheatService(
        ISaveGameCheatInjector injector,
        string userDataDirectory,
        Func<bool>? revalidate = null,
        SaveGameManagerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(userDataDirectory);
        _injector = injector;
        _userDataDirectory = userDataDirectory;
        _revalidate = revalidate;
        _maxCheckpointsPerSlot = (options ?? new SaveGameManagerOptions()).MaxCheckpointsPerSlot;
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
        if (GameProcessProbe.IsAncestorsRunning())
        {
            return new CheatApplyResult(false, "Close Ancestors before applying a cheat.");
        }

        try
        {
            string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slot);
            if (!File.Exists(slotPath))
            {
                return new CheatApplyResult(false, $"There is no save in slot {slot + 1} to modify.");
            }

            byte[] compressed = ReadSaveWithRetries(slotPath);
            string sourceSha256 = AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations.Sha256(compressed);
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
                _maxCheckpointsPerSlot);
            string checkpointId = AncestorsEnhanced.Infrastructure.Editing.MutationCoordinator.Run(() =>
            {
                // Revalidate inside the global mutation lock, immediately before the store write (F063-1c).
                if (_revalidate is not null && !_revalidate())
                {
                    throw new InvalidOperationException("The game context changed; the cheat cannot be applied safely. Refresh and try again.");
                }

                if (GameProcessProbe.IsAncestorsRunning())
                {
                    throw new InvalidOperationException("Close Ancestors before applying a cheat.");
                }
                byte[] current = ReadSaveWithRetries(slotPath);
                if (!string.Equals(
                        AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations.Sha256(current),
                        sourceSha256,
                        StringComparison.Ordinal))
                {
                    throw new IOException("The live save changed while the cheat was being prepared. Nothing was applied.");
                }

                return store.Create(_userDataDirectory, slot, recompressed, $"Cheat:{DisplayName(kind)}");
            });

            return new CheatApplyResult(
                true,
                $"{DisplayName(kind)} applied and saved as a new checkpoint for slot {slot + 1}.",
                checkpointId);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            return new CheatApplyResult(false, $"Nothing was applied: {exception.Message}");
        }
    }

    private static string DisplayName(CheatKind kind) => kind switch
    {
        CheatKind.HealClan => "Heal Current Ape",
        _ => kind.ToString(),
    };

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
                long nodeEnd = range.EndExclusive;
                if (index >= range.Offset && index < nodeEnd)
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
    /// Every reported patched range must resolve to exactly one authorised
    /// <see cref="CheatTargetSpec"/> (matched by its full schema path and type) whose
    /// stored float value(s) equal the injected target value (F027).
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

        IReadOnlyList<CheatTargetSpec> targets = SaveGameCheatTargets.CheatTargetsFor(kind);
        if (targets.Count == 0)
        {
            return false;
        }

        var resolved = new List<(CheatTargetSpec Spec, SaveGameSchemaNode Node)>();
        CollectTargetNodes(root, targets, [], resolved);
        if (resolved.Count == 0)
        {
            return false;
        }

        foreach (ByteRange range in ranges)
        {
            bool found = resolved.Any(entry =>
            {
                long nodeEnd = (long)entry.Node.ValueOffset + entry.Node.ValueLength;
                return range.Offset >= entry.Node.ValueOffset &&
                    range.EndExclusive <= nodeEnd &&
                    VerifyValueAt(range, entry.Spec.TargetValue, payload);
            });
            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static void CollectTargetNodes(
        SaveGameSchemaNode node,
        IReadOnlyList<CheatTargetSpec> targets,
        List<string> path,
        List<(CheatTargetSpec Spec, SaveGameSchemaNode Node)> resolved)
    {
        path.Add(node.Name);
        if (!node.IsTerminator)
        {
            string nodePath = string.Join("/", path);
            foreach (CheatTargetSpec spec in targets)
            {
                if (string.Equals(nodePath, spec.SchemaPath, StringComparison.Ordinal) &&
                    spec.Matches(node))
                {
                    resolved.Add((spec, node));
                }
            }
        }

        foreach (SaveGameSchemaNode child in node.Children)
        {
            CollectTargetNodes(child, targets, path, resolved);
        }

        path.RemoveAt(path.Count - 1);
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

    private static byte[] ReadSaveWithRetries(string slotPath) =>
        AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations.ReadStableBounded(slotPath, 64L * 1024 * 1024);

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}
