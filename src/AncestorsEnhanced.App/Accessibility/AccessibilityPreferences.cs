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
    private bool _hasUnreadablePreferences;

    public AccessibilityPreferencesStore(string? directory = null, Action<string>? diagnosticLog = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AncestorsEnhanced");
        _filePath = Path.Combine(directory, FileName);
        _diagnosticLog = diagnosticLog ?? (message => AppDiagnostics.Logger?.Write(message));
    }

    /// <summary>
    /// A malformed preferences file is deliberately never overwritten by a later
    /// preference change. The UI must ask the user to reset it explicitly.
    /// </summary>
    public bool HasUnreadablePreferences => _hasUnreadablePreferences;

    public AccessibilityPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AccessibilityPreferences();
            }

            AccessibilityPreferences? preferences = JsonSerializer.Deserialize<AccessibilityPreferences>(
                File.ReadAllText(_filePath),
                JsonOptions);
            if (preferences is not null)
            {
                return preferences;
            }

            _hasUnreadablePreferences = true;
            WriteDiagnostic("Could not load accessibility preferences; the file contained null.");
            return new AccessibilityPreferences();
        }
        catch (Exception exception)
        {
            _hasUnreadablePreferences = true;
            WriteDiagnostic($"Could not load accessibility preferences; using defaults: {exception.GetType().Name}");
            return new AccessibilityPreferences();
        }
    }

    public bool TrySave(AccessibilityPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (_hasUnreadablePreferences)
        {
            WriteDiagnostic("Accessibility preferences were not saved because the unreadable original has not been reset.");
            return false;
        }

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

    /// <summary>
    /// Archives the unreadable original before replacing it with known-safe
    /// defaults. Failure leaves the original file untouched.
    /// </summary>
    public bool TryReset(AccessibilityPreferences preferences, out string? archivedFileName)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        archivedFileName = null;
        if (!_hasUnreadablePreferences)
        {
            return TrySave(preferences);
        }

        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrWhiteSpace(directory) || !File.Exists(_filePath))
            {
                return false;
            }

            string archivePath = Path.Combine(
                directory,
                $"{FileName}.invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.bak");
            File.Move(_filePath, archivePath);
            _hasUnreadablePreferences = false;
            if (TrySave(preferences))
            {
                archivedFileName = Path.GetFileName(archivePath);
                return true;
            }

            // Keep the original recoverable even if the replacement could not be written.
            File.Move(archivePath, _filePath, overwrite: false);
            _hasUnreadablePreferences = true;
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _hasUnreadablePreferences = true;
            WriteDiagnostic($"Could not reset accessibility preferences: {exception.GetType().Name}");
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
