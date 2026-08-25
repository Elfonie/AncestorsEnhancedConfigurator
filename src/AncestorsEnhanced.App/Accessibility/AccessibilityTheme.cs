using Avalonia;
using Avalonia.Media;

namespace AncestorsEnhanced.App.Accessibility;

public static class AccessibilityTheme
{
    private static readonly IReadOnlyDictionary<string, string> StandardColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#070907",
        ["SurfaceBrush"] = "#121612",
        ["RaisedSurfaceBrush"] = "#191F19",
        ["BorderBrush"] = "#2A332A",
        ["PrimaryTextBrush"] = "#E8E4D9",
        ["MutedTextBrush"] = "#7A877A",
        ["ControlBackgroundBrush"] = "#1A221A",
        ["ControlBorderBrush"] = "#3A4A2E",
        ["ControlTextBrush"] = "#C8CFC4",
        ["ControlHoverBrush"] = "#253025",
        ["AccentBrush"] = "#B4D941",
        ["AccentHoverBrush"] = "#9ABF31",
        ["FocusBrush"] = "#B4D941",
        ["FocusGlowBrush"] = "#66B4D941",
        ["PrimaryActionBrush"] = "#9B3A12",
        ["PrimaryActionBorderBrush"] = "#C7591C",
        ["PrimaryActionHoverBrush"] = "#B54816",
        ["DangerBrush"] = "#8E2F29",
        ["DangerBorderBrush"] = "#C44A41",
        ["InfoSurfaceBrush"] = "#17242B",
        ["InfoBorderBrush"] = "#344A56",
        ["InfoTextBrush"] = "#B8D7E5",
        ["WarningSurfaceBrush"] = "#211C18",
        ["WarningBorderBrush"] = "#765536",
        ["WarningTextBrush"] = "#E8D5B5",
        ["SecondaryTextBrush"] = "#9CB0BC",
        ["TechnicalTextBrush"] = "#71828D",
        ["SuccessTextBrush"] = "#B4D941",
        ["ReviewSurfaceBrush"] = "#10191F",
        ["ReviewBorderBrush"] = "#3B7598",
        ["ReviewTextBrush"] = "#DCEEFF",
    };

    private static readonly IReadOnlyDictionary<string, string> HighContrastColors = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#000000",
        ["SurfaceBrush"] = "#000000",
        ["RaisedSurfaceBrush"] = "#0E0E0E",
        ["BorderBrush"] = "#FFFFFF",
        ["PrimaryTextBrush"] = "#FFFFFF",
        ["MutedTextBrush"] = "#F0F0F0",
        ["ControlBackgroundBrush"] = "#000000",
        ["ControlBorderBrush"] = "#FFFFFF",
        ["ControlTextBrush"] = "#FFFFFF",
        ["ControlHoverBrush"] = "#1C1C1C",
        ["AccentBrush"] = "#FFFF00",
        ["AccentHoverBrush"] = "#FFFF66",
        ["FocusBrush"] = "#00FFFF",
        ["FocusGlowBrush"] = "#CC00FFFF",
        ["PrimaryActionBrush"] = "#003A8C",
        ["PrimaryActionBorderBrush"] = "#FFFFFF",
        ["PrimaryActionHoverBrush"] = "#005FCC",
        ["DangerBrush"] = "#8A0000",
        ["DangerBorderBrush"] = "#FFFFFF",
        ["InfoSurfaceBrush"] = "#000000",
        ["InfoBorderBrush"] = "#00FFFF",
        ["InfoTextBrush"] = "#FFFFFF",
        ["WarningSurfaceBrush"] = "#000000",
        ["WarningBorderBrush"] = "#FFFF00",
        ["WarningTextBrush"] = "#FFFFFF",
        ["SecondaryTextBrush"] = "#FFFFFF",
        ["TechnicalTextBrush"] = "#F0F0F0",
        ["SuccessTextBrush"] = "#FFFF00",
        ["ReviewSurfaceBrush"] = "#000000",
        ["ReviewBorderBrush"] = "#00FFFF",
        ["ReviewTextBrush"] = "#FFFFFF",
    };

    public static void Apply(Application application, bool highContrastEnabled)
    {
        ArgumentNullException.ThrowIfNull(application);
        IReadOnlyDictionary<string, string> colors = highContrastEnabled ? HighContrastColors : StandardColors;
        foreach ((string key, string color) in colors)
        {
            application.Resources[key] = new SolidColorBrush(Color.Parse(color));
        }
    }
}
