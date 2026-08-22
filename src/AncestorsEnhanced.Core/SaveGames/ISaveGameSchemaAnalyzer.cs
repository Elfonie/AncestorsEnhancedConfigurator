namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>Result of a read-only analysis of a UE4 save's tagged-property schema.</summary>
public sealed class SaveGameSchemaAnalysis
{
    public SaveGameSchemaAnalysis(
        IReadOnlyList<SaveGameSchemaNode> tree,
        IReadOnlyList<SaveGameSchemaNode> findings)
    {
        Tree = tree;
        Findings = findings;
    }

    public IReadOnlyList<SaveGameSchemaNode> Tree { get; }

    public IReadOnlyList<SaveGameSchemaNode> Findings { get; }
}

/// <summary>Reads the nested tagged-property schema of a save without changing it.</summary>
public interface ISaveGameSchemaAnalyzer
{
    SaveGameSchemaAnalysis Analyze(byte[] compressedSave);
}
