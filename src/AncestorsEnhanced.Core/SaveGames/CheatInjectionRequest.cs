using System.Globalization;

namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>Request describing a set of value injections to apply to a decompressed save.</summary>
public sealed class CheatInjectionRequest
{
    public CheatInjectionRequest(CheatKind kind)
    {
        Kind = kind;
    }

    public CheatKind Kind { get; }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"CheatInjection({Kind})");
}