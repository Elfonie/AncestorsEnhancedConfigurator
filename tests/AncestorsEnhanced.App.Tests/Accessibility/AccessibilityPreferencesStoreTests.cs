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
    public void ExperimentalSettingsAndHardwareScanPreferencesPersist()
    {
        var store = new AccessibilityPreferencesStore(_directory);

        Assert.True(store.TrySave(new AccessibilityPreferences(
            ExperimentalGraphicsSettingsEnabled: true,
            ExperimentalGameplaySettingsEnabled: true,
            HasAcknowledgedDetailedHardwareScan: true)));

        AccessibilityPreferences loaded = store.Load();
        Assert.True(loaded.ExperimentalGraphicsSettingsEnabled);
        Assert.True(loaded.ExperimentalGameplaySettingsEnabled);
        Assert.True(loaded.HasAcknowledgedDetailedHardwareScan);
    }

    [Fact]
    public void InvalidPreferenceFileFailsClosedToTheDefaultAndIsNotOverwritten()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "accessibility.json"), "not json");

        var diagnostics = new List<string>();

        var store = new AccessibilityPreferencesStore(_directory, diagnostics.Add);

        Assert.False(store.Load().HighContrastEnabled);
        Assert.True(store.HasUnreadablePreferences);
        Assert.False(store.TrySave(new AccessibilityPreferences(HighContrastEnabled: true)));
        Assert.Equal("not json", File.ReadAllText(Path.Combine(_directory, "accessibility.json")));
        Assert.Contains(diagnostics, message => message.Contains("Could not load accessibility preferences", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitResetArchivesTheUnreadableFileBeforeWritingDefaults()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "accessibility.json");
        File.WriteAllText(path, "{ bad json");
        var store = new AccessibilityPreferencesStore(_directory);
        _ = store.Load();

        Assert.True(store.TryReset(new AccessibilityPreferences(), out string? archived));
        Assert.False(store.HasUnreadablePreferences);
        Assert.NotNull(archived);
        Assert.Equal("{ bad json", File.ReadAllText(Path.Combine(_directory, archived!)));
        Assert.False(store.Load().HighContrastEnabled);
    }

    [Fact]
    public void OversizedPreferenceFileFailsClosed()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "accessibility.json");
        // Write 300 KiB file
        File.WriteAllBytes(path, new byte[300 * 1024]);

        var diagnostics = new List<string>();
        var store = new AccessibilityPreferencesStore(_directory, diagnostics.Add);

        Assert.False(store.Load().HighContrastEnabled);
        Assert.True(store.HasUnreadablePreferences);
        Assert.Contains(diagnostics, message => message.Contains("exceeds 256 KiB", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
