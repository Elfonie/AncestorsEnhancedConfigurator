using AncestorsEnhanced.App.Accessibility;
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
    }

    [Fact]
    public void StandardThemeRestoresTheDefaultPalette()
    {
        var application = new Application();

        AccessibilityTheme.Apply(application, true);
        AccessibilityTheme.Apply(application, false);

        Assert.Equal(Color.Parse("#FFE8E4D9"), ColorOf(application, "PrimaryTextBrush"));
        Assert.Equal(Color.Parse("#FF070907"), ColorOf(application, "AppBackgroundBrush"));
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

    private static Color ColorOf(Application application, string key)
    {
        var brush = Assert.IsType<SolidColorBrush>(application.Resources[key]);
        return brush.Color;
    }
}
