namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>Result of applying a cheat injection to a decompressed save.</summary>
public sealed class CheatInjectionResult
{
    public CheatInjectionResult(bool succeeded, string message, int modifiedCount = 0)
    {
        Succeeded = succeeded;
        Message = message;
        ModifiedCount = modifiedCount;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public int ModifiedCount { get; }
}

/// <summary>Applies safe, schema-verified value injections to a decompressed lineage save.</summary>
public interface ISaveGameCheatInjector
{
    CheatInjectionResult TryInject(byte[] decompressedSave, CheatKind kind, out byte[]? modifiedSave);
}