using Avalonia;
using Avalonia.Media;

namespace AncestorsEnhanced.App.ViewModels;

/// <summary>
/// Semantic status mapping. View models choose meaning, while the active
/// accessibility palette supplies the actual brush.
/// </summary>
public static class StatusPresentation
{
    public const string Neutral = "StatusNeutral";
    public const string Success = "StatusSuccess";
    public const string Warning = "StatusWarning";
    public const string Error = "StatusError";
    public const string Modified = "StatusModified";
    public const string Technical = "StatusTechnical";

    public static string FromLegacyAccent(string? accent) => accent?.ToUpperInvariant() switch
    {
        "#B4D941" => Success,
        "#D6BC84" => Warning,
        "#E04D42" => Error,
        "#FF5A00" => Modified,
        "#687668" => Technical,
        _ => Neutral,
    };

    public static IBrush BrushForLegacyAccent(string? accent, Application? application = null)
    {
        string resourceKey = FromLegacyAccent(accent) switch
        {
            Success => "SuccessTextBrush",
            Warning => "GoldTextBrush",
            Error => "ErrorTextBrush",
            Modified => "OrangeAccentBrush",
            Technical => "TechnicalTextBrush",
            _ => "MutedTextBrush",
        };
        if ((application ?? Application.Current)?.Resources.TryGetValue(resourceKey, out object? resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(accent ?? "#7A877A"));
    }
}
