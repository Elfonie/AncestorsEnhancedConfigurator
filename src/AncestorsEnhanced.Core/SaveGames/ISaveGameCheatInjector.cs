namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>Result of applying a cheat injection to a decompressed save.</summary>
public sealed class CheatInjectionResult
{
    public CheatInjectionResult(
        bool succeeded,
        string message,
        int modifiedCount = 0,
        IReadOnlyList<ByteRange>? modifiedRanges = null)
    {
        Succeeded = succeeded;
        Message = message;
        ModifiedCount = modifiedCount;
        ModifiedRanges = modifiedRanges ?? [];
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public int ModifiedCount { get; }

    /// <summary>
    /// The byte ranges that were patched inside the decompressed save. Used to verify
    /// that exactly the expected bytes changed before a checkpoint is stored.
    /// </summary>
    public IReadOnlyList<ByteRange> ModifiedRanges { get; }
}

/// <summary>A byte range inside the decompressed save payload.</summary>
public readonly record struct ByteRange(int Offset, int Length)
{
    public int EndExclusive => Offset + Length;
}

/// <summary>Applies safe, schema-verified value injections to a decompressed lineage save.</summary>
public interface ISaveGameCheatInjector
{
    CheatInjectionResult TryInject(byte[] decompressedSave, CheatKind kind, out byte[]? modifiedSave);
}
