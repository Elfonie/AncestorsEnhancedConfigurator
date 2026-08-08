namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>
/// Structural description of an exact cheat target inside a UE4 save schema (F027).
/// Targeting by property name alone is not enough: a target is identified by its
/// unique full schema path, the expected property type and, for arrays, the element
/// type, whether the value is scalar or an array, the exact intended value and how many
/// matching nodes are authorised. The injector and the post-reparse verification both
/// resolve the same exact target path, so an equally named node elsewhere in the tree is
/// never accepted by accident.
/// </summary>
public sealed record CheatTargetSpec(
    string SchemaPath,
    string PropertyName,
    string PropertyType,
    string? ElementType,
    bool IsArray,
    float TargetValue,
    int ExpectedMatchCount = 1)
{
    /// <summary>True when this schema node matches the target's name, type and element type.</summary>
    public bool Matches(SaveGameSchemaNode node)
    {
        if (!string.Equals(node.Name, PropertyName, StringComparison.Ordinal) ||
            !string.Equals(node.Type, PropertyType, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsArray)
        {
            return string.Equals(node.ElementType, ElementType, StringComparison.Ordinal);
        }

        return true;
    }
}
