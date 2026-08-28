using System.Text.Json;
using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.App.Accessibility;

public sealed record AccessibilityPreferences(
    bool HighContrastEnabled = false,
    bool DiscordRichPresenceEnabled = false,
    bool HasCompletedOnboarding = false,
    bool ExperimentalGraphicsSettingsEnabled = false,
    bool ExperimentalGameplaySettingsEnabled = false,
    bool HasAcknowledgedDetailedHardwareScan = false,
    HardwareSnapshot? DetailedHardwareSnapshot = null);

public sealed class AccessibilityPreferencesStore
{
    private const string FileName = "accessibility.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly Action<string> _diagnosticLog;

    public AccessibilityPreferencesStore(string? directory = null, Action<string>? diagnosticLog = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AncestorsEnhanced");
        _filePath = Path.Combine(directory, FileName);
        _diagnosticLog = diagnosticLog ?? (message => AppDiagnostics.Logger?.Write(message));
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
        catch (Exception exception)
        {
            WriteDiagnostic($"Could not load accessibility preferences; using defaults: {exception.GetType().Name}");
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
            string temporaryPath = Path.Combine(
                directory,
                $".{FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
                File.Move(temporaryPath, _filePath, overwrite: true);
                return true;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    WriteDiagnostic($"Could not clean up temporary accessibility preferences: {exception.GetType().Name}");
                }
            }
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"Could not save accessibility preferences: {exception.GetType().Name}");
            return false;
        }
    }

    private void WriteDiagnostic(string message)
    {
        try
        {
            _diagnosticLog(message);
        }
        catch
        {
            // Diagnostics must never turn an optional-preference failure into an app failure.
        }
    }
}
