using AncestorsEnhanced.App.Accessibility;
using AncestorsEnhanced.App.ViewModels;
using Avalonia;
using Avalonia.Media;

namespace AncestorsEnhanced.App.Tests.Accessibility;

public sealed class AccessibilityThemeTests
{
    [Fact]
    public void HighContrastUsesStrongColorsForTheSharedApplicationResources()
    {
        var application = new Application();

        AccessibilityTheme.Apply(application, true);

        Assert.Equal(Color.Parse("#FFFFFFFF"), ColorOf(application, "PrimaryTextBrush"));
        Assert.Equal(Color.Parse("#FF000000"), ColorOf(application, "AppBackgroundBrush"));
        Assert.Equal(Color.Parse("#FFFFFF00"), ColorOf(application, "AccentBrush"));
        Assert.Equal(Color.Parse("#FF00FFFF"), ColorOf(application, "FocusBrush"));
        Assert.Equal(Color.Parse("#FFFFFF00"), ColorOf(application, "ToggleSwitchCurtainFillOn"));
    }

    [Fact]
    public void StandardThemeRestoresTheDefaultPalette()
    {
        var application = new Application();

        AccessibilityTheme.Apply(application, true);
        AccessibilityTheme.Apply(application, false);

        Assert.Equal(Color.Parse(AccessibilityTheme.StandardPalette["PrimaryTextBrush"]), ColorOf(application, "PrimaryTextBrush"));
        Assert.Equal(Color.Parse(AccessibilityTheme.StandardPalette["AppBackgroundBrush"]), ColorOf(application, "AppBackgroundBrush"));
    }

    [Fact]
    public void StandardAndHighContrastPalettesDefineTheSameKeys()
    {
        // A key missing from one palette would silently keep the other theme's color
        // after a high-contrast switch, so the two palettes must be key-complete twins.
        Assert.Empty(AccessibilityTheme.StandardPalette.Keys.Except(AccessibilityTheme.HighContrastPalette.Keys));
        Assert.Empty(AccessibilityTheme.HighContrastPalette.Keys.Except(AccessibilityTheme.StandardPalette.Keys));
    }

    [Fact]
    public void HighContrastRecolorsEveryPageLevelSemanticToken()
    {
        string[] pageLevelTokens =
        [
            "HeadingTextBrush",
            "BodyTextBrush",
            "LabelTextBrush",
            "MidTextBrush",
            "OrangeAccentBrush",
            "GoldTextBrush",
            "EditorSurfaceBrush",
            "EditorBorderBrush",
            "OliveBorderBrush",
            "CloudWarningBorderBrush",
            "OnboardingPanelBrush",
            "ModalOverlayBrush",
            "DialogScrimBrush",
            "NavActiveBackgroundBrush",
        ];

        var application = new Application();
        AccessibilityTheme.Apply(application, true);

        foreach (string token in pageLevelTokens)
        {
            Assert.True(
                application.Resources.ContainsKey(token),
                $"High contrast palette is missing semantic token '{token}'.");
        }

        // Page-level text tokens must become near-white; accent tokens must stay vivid.
        Assert.Equal(Color.Parse("#FFFFFFFF"), ColorOf(application, "HeadingTextBrush"));
        Assert.Equal(Color.Parse("#FFFFFFFF"), ColorOf(application, "BodyTextBrush"));
        Assert.Equal(Color.Parse("#FFFFFF00"), ColorOf(application, "GoldTextBrush"));
        Assert.True(application.Resources["NavActiveBackgroundBrush"] is IBrush);
    }

    [Fact]
    public void DynamicStatusBrushesResolveTheCurrentSemanticPalette()
    {
        var application = new Application();
        AccessibilityTheme.Apply(application, false);
        Assert.Equal(Color.Parse("#FFB4D941"), Assert.IsType<SolidColorBrush>(
            StatusPresentation.BrushForLegacyAccent("#B4D941", application)).Color);

        AccessibilityTheme.Apply(application, true);
        Assert.Equal(Color.Parse("#FFFFFF00"), Assert.IsType<SolidColorBrush>(
            StatusPresentation.BrushForLegacyAccent("#B4D941", application)).Color);
        Assert.Equal(Color.Parse("#FFFF4D4D"), Assert.IsType<SolidColorBrush>(
            StatusPresentation.BrushForLegacyAccent("#E04D42", application)).Color);
    }

    [Fact]
    public void StandardSidebarSubtitleMeetsNormalTextContrastOnItsRaisedSurface()
    {
        double contrast = ContrastRatio(
            Color.Parse(AccessibilityTheme.StandardPalette["MutedTextBrush"]),
            Color.Parse(AccessibilityTheme.StandardPalette["RaisedSurfaceBrush"]));

        Assert.True(contrast >= 4.5, $"Expected at least 4.5:1 contrast, got {contrast:F2}:1.");
    }

    private static Color ColorOf(Application application, string key)
    {
        var brush = Assert.IsType<SolidColorBrush>(application.Resources[key]);
        return brush.Color;
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        double foregroundLuminance = Luminance(foreground);
        double backgroundLuminance = Luminance(background);
        return (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
               (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Linearize(byte channel)
        {
            double normalized = channel / 255d;
            return normalized <= 0.04045
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
    }
}
