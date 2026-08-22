using System.Globalization;

namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>A node in the nested tagged-property tree of a UE4 save.</summary>
public sealed class SaveGameSchemaNode
{
    public SaveGameSchemaNode(string name, string? type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }

    public string? Type { get; }

    public string? StructType { get; set; }

    public string? EnumType { get; set; }

    public string? ElementType { get; set; }

    public int ValueOffset { get; set; }

    public int ValueLength { get; set; }

    public List<SaveGameSchemaNode> Children { get; } = [];

    public bool IsTerminator => Type is null;

    public static string Describe(SaveGameSchemaNode node)
    {
        string kind = node.Type ?? "terminator";
        if (!string.IsNullOrEmpty(node.StructType))
        {
            kind = $"{kind}<{node.StructType}>";
        }
        else if (!string.IsNullOrEmpty(node.EnumType))
        {
            kind = $"{kind}::{node.EnumType}";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{node.Name} : {kind} @0x{node.ValueOffset:X} len={node.ValueLength}");
    }
}
