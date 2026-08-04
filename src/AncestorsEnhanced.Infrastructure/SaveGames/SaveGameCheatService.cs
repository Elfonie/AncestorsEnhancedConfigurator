using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Orchestrates a cheat injection: reads the live slot save, decompresses it, applies
/// the chosen injection, recompresses, and stores the modified save as a NEW checkpoint.
/// The live slot file is never overwritten, so the original is always recoverable.
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

        try
        {
            string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slot);
            if (!File.Exists(slotPath))
            {
                return new CheatApplyResult(false, $"There is no save in slot {slot} to modify.");
            }

            byte[] compressed = File.ReadAllBytes(slotPath);
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
            var store = new SaveGameCheckpointStore(
                () => DateTimeOffset.UtcNow,
                maxCheckpointsPerSlot: 50);
            string checkpointId = store.Create(_userDataDirectory, slot, recompressed);

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

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}