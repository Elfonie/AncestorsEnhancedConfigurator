using Avalonia;
using Avalonia.Media;

namespace AncestorsEnhanced.App.Accessibility;

/// <summary>
/// Central semantic color palette for the whole application. Every semantic color used by
/// any window must be defined here and referenced via <c>{DynamicResource ...}</c>, so a
/// high-contrast switch re-colors the entire UI at runtime.
/// </summary>
public static class AccessibilityTheme
{
    private static readonly IReadOnlyDictionary<string, string> StandardColors = new Dictionary<string, string>
    {
        // Surfaces
        ["AppBackgroundBrush"] = "#070907",
        ["SurfaceBrush"] = "#121612",
        ["RaisedSurfaceBrush"] = "#191F19",
        ["BorderBrush"] = "#2A332A",
        ["ControlBackgroundBrush"] = "#1A221A",
        ["ControlBorderBrush"] = "#3A4A2E",
        ["ControlHoverBrush"] = "#253025",
        ["InfoSurfaceBrush"] = "#17242B",
        ["InfoBorderBrush"] = "#344A56",
        ["WarningSurfaceBrush"] = "#211C18",
        ["WarningBorderBrush"] = "#765536",
        ["ReviewSurfaceBrush"] = "#10191F",
        ["ReviewBorderBrush"] = "#3B7598",
        ["PrimaryActionBrush"] = "#9B3A12",
        ["PrimaryActionBorderBrush"] = "#C7591C",
        ["PrimaryActionHoverBrush"] = "#B54816",
        ["DangerBrush"] = "#8E2F29",
        ["DangerBorderBrush"] = "#C44A41",

        // Text
        ["PrimaryTextBrush"] = "#E8E4D9",
        ["MutedTextBrush"] = "#7A877A",
        ["ControlTextBrush"] = "#C8CFC4",
        ["SecondaryTextBrush"] = "#9CB0BC",
        ["TechnicalTextBrush"] = "#71828D",
        ["SuccessTextBrush"] = "#B4D941",
        ["AccentBrush"] = "#B4D941",
        ["AccentHoverBrush"] = "#9ABF31",
        ["FocusBrush"] = "#B4D941",
        ["FocusGlowBrush"] = "#66B4D941",
        ["InfoTextBrush"] = "#B8D7E5",
        ["WarningTextBrush"] = "#E8D5B5",
        ["ReviewTextBrush"] = "#DCEEFF",

        // Page-level tokens previously hardcoded in MainWindow.axaml
        ["HeadingTextBrush"] = "#EBF0F3",
        ["BodyTextBrush"] = "#E6ECEF",
        ["LabelTextBrush"] = "#82939D",
        ["MidTextBrush"] = "#8B9AA4",
        ["OrangeAccentBrush"] = "#FF5A00",
        ["GoldTextBrush"] = "#D6BC84",
        ["PrimaryActionTextBrush"] = "#FFF3EA",
        ["DangerTextBrush"] = "#FFFFFF",

        // Page-level surfaces/borders previously hardcoded in MainWindow.axaml
        ["EditorSurfaceBrush"] = "#182127",
        ["EditorBorderBrush"] = "#2A363E",
        ["OliveBorderBrush"] = "#4A5A23",
        ["CloudWarningBorderBrush"] = "#3B3120",
        ["OnboardingPanelBrush"] = "#15232C",
        ["ModalOverlayBrush"] = "#E60C1115",
        ["DialogScrimBrush"] = "#1A000000",
    };

    private static readonly IReadOnlyDictionary<string, string> HighContrastColors = new Dictionary<string, string>
    {
        // Surfaces
        ["AppBackgroundBrush"] = "#000000",
        ["SurfaceBrush"] = "#000000",
        ["RaisedSurfaceBrush"] = "#0E0E0E",
        ["BorderBrush"] = "#FFFFFF",
        ["ControlBackgroundBrush"] = "#000000",
        ["ControlBorderBrush"] = "#FFFFFF",
        ["ControlHoverBrush"] = "#1C1C1C",
        ["InfoSurfaceBrush"] = "#000000",
        ["InfoBorderBrush"] = "#00FFFF",
        ["WarningSurfaceBrush"] = "#000000",
        ["WarningBorderBrush"] = "#FFFF00",
        ["ReviewSurfaceBrush"] = "#000000",
        ["ReviewBorderBrush"] = "#00FFFF",
        ["PrimaryActionBrush"] = "#003A8C",
        ["PrimaryActionBorderBrush"] = "#FFFFFF",
        ["PrimaryActionHoverBrush"] = "#005FCC",
        ["DangerBrush"] = "#8A0000",
        ["DangerBorderBrush"] = "#FFFFFF",

        // Text
        ["PrimaryTextBrush"] = "#FFFFFF",
        ["MutedTextBrush"] = "#F0F0F0",
        ["ControlTextBrush"] = "#FFFFFF",
        ["SecondaryTextBrush"] = "#FFFFFF",
        ["TechnicalTextBrush"] = "#F0F0F0",
        ["SuccessTextBrush"] = "#FFFF00",
        ["AccentBrush"] = "#FFFF00",
        ["AccentHoverBrush"] = "#FFFF66",
        ["FocusBrush"] = "#00FFFF",
        ["FocusGlowBrush"] = "#CC00FFFF",
        ["InfoTextBrush"] = "#FFFFFF",
        ["WarningTextBrush"] = "#FFFFFF",
        ["ReviewTextBrush"] = "#FFFFFF",

        // Page-level tokens previously hardcoded in MainWindow.axaml
        ["HeadingTextBrush"] = "#FFFFFF",
        ["BodyTextBrush"] = "#FFFFFF",
        ["LabelTextBrush"] = "#F0F0F0",
        ["MidTextBrush"] = "#F0F0F0",
        ["OrangeAccentBrush"] = "#FFA500",
        ["GoldTextBrush"] = "#FFFF00",
        ["PrimaryActionTextBrush"] = "#FFFFFF",
        ["DangerTextBrush"] = "#FFFFFF",

        // Page-level surfaces/borders previously hardcoded in MainWindow.axaml
        ["EditorSurfaceBrush"] = "#000000",
        ["EditorBorderBrush"] = "#FFFFFF",
        ["OliveBorderBrush"] = "#FFFFFF",
        ["CloudWarningBorderBrush"] = "#FFFF00",
        ["OnboardingPanelBrush"] = "#000000",
        ["ModalOverlayBrush"] = "#E6000000",
        ["DialogScrimBrush"] = "#B3000000",
    };

    /// <summary>
    /// Non-solid brushes that cannot be expressed as a single hex color.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IBrush> StandardBrushes = new Dictionary<string, IBrush>
    {
        ["NavActiveBackgroundBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#4A5A23"), 0),
                new GradientStop(Color.Parse("#1E2720"), 1),
            },
        },
    };

    private static readonly IReadOnlyDictionary<string, IBrush> HighContrastBrushes = new Dictionary<string, IBrush>
    {
        // Solid dark blue keeps yellow accent nav text readable at high contrast.
        ["NavActiveBackgroundBrush"] = new SolidColorBrush(Color.Parse("#003A8C")),
    };

    internal static IReadOnlyDictionary<string, string> StandardPalette => StandardColors;

    internal static IReadOnlyDictionary<string, string> HighContrastPalette => HighContrastColors;

    public static void Apply(Application application, bool highContrastEnabled)
    {
        ArgumentNullException.ThrowIfNull(application);
        IReadOnlyDictionary<string, string> colors = highContrastEnabled ? HighContrastColors : StandardColors;
        foreach ((string key, string color) in colors)
        {
            application.Resources[key] = new SolidColorBrush(Color.Parse(color));
        }

        IReadOnlyDictionary<string, IBrush> brushes = highContrastEnabled ? HighContrastBrushes : StandardBrushes;
        foreach ((string key, IBrush brush) in brushes)
        {
            application.Resources[key] = brush;
        }
    }
}
