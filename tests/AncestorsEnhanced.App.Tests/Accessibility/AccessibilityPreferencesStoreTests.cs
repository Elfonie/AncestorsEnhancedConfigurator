using AncestorsEnhanced.App.Accessibility;

namespace AncestorsEnhanced.App.Tests.Accessibility;

public sealed class AccessibilityPreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"AncestorsEnhanced-{Guid.NewGuid():N}");

    [Fact]
    public void MissingPreferencesUseTheAccessibleDefault()
    {
        var store = new AccessibilityPreferencesStore(_directory);

        Assert.False(store.Load().HighContrastEnabled);
    }

    [Fact]
    public void HighContrastPreferencePersistsOutsideGameData()
    {
        var store = new AccessibilityPreferencesStore(_directory);

        Assert.True(store.TrySave(new AccessibilityPreferences(true)));
        Assert.True(store.Load().HighContrastEnabled);
        Assert.True(File.Exists(Path.Combine(_directory, "accessibility.json")));
    }

    [Fact]
    public void OnboardingCompletionPersistsOutsideGameData()
    {
        var store = new AccessibilityPreferencesStore(_directory);

        Assert.True(store.TrySave(new AccessibilityPreferences(HasCompletedOnboarding: true)));

        Assert.True(store.Load().HasCompletedOnboarding);
    }

    [Fact]
    public void ExperimentalGraphicsAndHardwareScanPreferencesPersist()
    {
        var store = new AccessibilityPreferencesStore(_directory);

        Assert.True(store.TrySave(new AccessibilityPreferences(
            ExperimentalGraphicsSettingsEnabled: true,
            HasAcknowledgedDetailedHardwareScan: true)));

        AccessibilityPreferences loaded = store.Load();
        Assert.True(loaded.ExperimentalGraphicsSettingsEnabled);
        Assert.True(loaded.HasAcknowledgedDetailedHardwareScan);
    }

    [Fact]
    public void InvalidPreferenceFileFailsClosedToTheDefault()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "accessibility.json"), "not json");

        Assert.False(new AccessibilityPreferencesStore(_directory).Load().HighContrastEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
