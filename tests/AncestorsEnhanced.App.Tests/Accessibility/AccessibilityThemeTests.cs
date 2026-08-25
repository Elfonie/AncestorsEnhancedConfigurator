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

    private static Color ColorOf(Application application, string key)
    {
        var brush = Assert.IsType<SolidColorBrush>(application.Resources[key]);
        return brush.Color;
    }
}
