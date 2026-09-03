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
        ["AppBackgroundBrush"] = "#0A0E0B",
        ["SurfaceBrush"] = "#111713",
        ["RaisedSurfaceBrush"] = "#161F19",
        ["BorderBrush"] = "#233027",
        ["ControlBackgroundBrush"] = "#1C261F",
        ["ControlBorderBrush"] = "#28362B",
        ["ControlHoverBrush"] = "#223026",
        ["InfoSurfaceBrush"] = "#111C18",
        ["InfoBorderBrush"] = "#2B4435",
        ["WarningSurfaceBrush"] = "#241C12",
        ["WarningBorderBrush"] = "#765830",
        ["ReviewSurfaceBrush"] = "#141C16",
        ["ReviewBorderBrush"] = "#3D5943",
        ["PrimaryActionBrush"] = "#8B481A",
        ["PrimaryActionBorderBrush"] = "#D8792C",
        ["PrimaryActionHoverBrush"] = "#A65720",
        ["DangerBrush"] = "#3A1917",
        ["DangerBorderBrush"] = "#C44A41",

        // Text
        ["PrimaryTextBrush"] = "#F0F5EE",
        ["MutedTextBrush"] = "#768D7B",
        ["ControlTextBrush"] = "#C8D6C5",
        ["SecondaryTextBrush"] = "#A1B5A5",
        ["TechnicalTextBrush"] = "#768D7B",
        ["SuccessTextBrush"] = "#B4D941",
        ["ErrorTextBrush"] = "#EF5350",
        ["AccentBrush"] = "#B4D941",
        ["AccentHoverBrush"] = "#C4E84F",
        ["FocusBrush"] = "#B4D941",
        ["FocusGlowBrush"] = "#4DB4D941",
        ["InfoTextBrush"] = "#A5D6B4",
        ["WarningTextBrush"] = "#E8D5B5",
        ["ReviewTextBrush"] = "#F0F5EE",

        // Page-level tokens
        ["HeadingTextBrush"] = "#F0F5EE",
        ["BodyTextBrush"] = "#E4EBE2",
        ["LabelTextBrush"] = "#869D8B",
        ["MidTextBrush"] = "#869D8B",
        ["OrangeAccentBrush"] = "#FF9800",
        ["GoldTextBrush"] = "#E2B45C",
        ["PrimaryActionTextBrush"] = "#FFFFFF",
        ["DangerTextBrush"] = "#EF5350",

        // Page-level surfaces/borders
        ["EditorSurfaceBrush"] = "#161F19",
        ["EditorBorderBrush"] = "#28362B",
        ["OliveBorderBrush"] = "#3B5226",
        ["CloudWarningBorderBrush"] = "#3B3120",
        ["OnboardingPanelBrush"] = "#161F19",
        ["ModalOverlayBrush"] = "#E60A0E0B",
        ["DialogScrimBrush"] = "#1A000000",
        ["ToggleSwitchCurtainFillOn"] = "#B4D941",
        ["ToggleSwitchCurtainFillOnPointerOver"] = "#C4E84F",
        ["ToggleSwitchCurtainStrokeOn"] = "#B4D941",
        ["ToggleSwitchCurtainStrokeOnPointerOver"] = "#C4E84F",
        ["ToggleSwitchKnobFillOn"] = "#0A0E0B",
        ["ToggleSwitchKnobFillOnPointerOver"] = "#0A0E0B",
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
        ["ErrorTextBrush"] = "#FF4D4D",
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
        ["ToggleSwitchCurtainFillOn"] = "#FFFF00",
        ["ToggleSwitchCurtainFillOnPointerOver"] = "#FFFF66",
        ["ToggleSwitchCurtainStrokeOn"] = "#FFFFFF",
        ["ToggleSwitchCurtainStrokeOnPointerOver"] = "#FFFFFF",
        ["ToggleSwitchKnobFillOn"] = "#000000",
        ["ToggleSwitchKnobFillOnPointerOver"] = "#000000",
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
        ["BrandGradientBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#223026"), 0),
                new GradientStop(Color.Parse("#162217"), 1),
            },
        },
    };

    private static readonly IReadOnlyDictionary<string, IBrush> HighContrastBrushes = new Dictionary<string, IBrush>
    {
        // Solid dark blue keeps yellow accent nav text readable at high contrast.
        ["NavActiveBackgroundBrush"] = new SolidColorBrush(Color.Parse("#003A8C")),
        ["BrandGradientBrush"] = new SolidColorBrush(Color.Parse("#000000")),
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
