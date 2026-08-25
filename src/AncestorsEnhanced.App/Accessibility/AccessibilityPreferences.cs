using System.Text.Json;

namespace AncestorsEnhanced.App.Accessibility;

public sealed record AccessibilityPreferences(
    bool HighContrastEnabled = false,
    bool DiscordRichPresenceEnabled = false,
    bool HasCompletedOnboarding = false,
    bool ExperimentalGraphicsSettingsEnabled = false,
    bool HasAcknowledgedDetailedHardwareScan = false);

public sealed class AccessibilityPreferencesStore
{
    private const string FileName = "accessibility.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public AccessibilityPreferencesStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AncestorsEnhanced");
        _filePath = Path.Combine(directory, FileName);
    }

    public AccessibilityPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AccessibilityPreferences();
            }

            return JsonSerializer.Deserialize<AccessibilityPreferences>(File.ReadAllText(_filePath), JsonOptions)
                ?? new AccessibilityPreferences();
        }
        catch (Exception)
        {
            return new AccessibilityPreferences();
        }
    }

    public bool TrySave(AccessibilityPreferences preferences)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporaryPath, _filePath, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
