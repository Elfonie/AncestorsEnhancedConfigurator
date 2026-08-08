using System.Linq;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.SystemSave;
using AncestorsEnhanced.Infrastructure.Tests.SystemSave;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

/// <summary>F065 - System.sav preview must come from the freshly-read file, not the stale snapshot.</summary>
public sealed class F065SystemSavePreviewTests
{
    [Fact]
    public void SystemSavePreviewComesFromTheCurrentFileNotTheStaleSnapshot()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"ae-f065-{Guid.NewGuid():N}");
        try
        {
            string userData = CreateUserData(temporary);
            string saveDirectory = Directory.CreateDirectory(Path.Combine(userData, "SaveGames")).FullName;
            string systemSave = Path.Combine(saveDirectory, "System.sav");

            // The canonical fixture is the "current" file on disk (B).
            byte[] currentBytes = VerifiedSystemSaveFixture.Read();
            File.WriteAllBytes(systemSave, currentBytes);

            SystemGraphicsSettingsSnapshot fileGraphics = AncestorsSystemSaveCodec.Read(currentBytes);
            string fileResolution = $"{fileGraphics.FullscreenWidth}x{fileGraphics.FullscreenHeight}";

            // A stale snapshot claims a *different but valid* resolution (A), while the
            // file on disk still contains B.
            string[] choices = SystemGraphicsOptionCatalog.Resolutions
                .Where(resolution => resolution != fileResolution)
                .ToArray();
            string staleResolution = choices[0];
            string requestedResolution = choices[1];
            (int staleWidth, int staleHeight) = ParseResolution(staleResolution);
            SystemGraphicsSettingsSnapshot staleGraphics = fileGraphics with
            {
                FullscreenWidth = staleWidth,
                FullscreenHeight = staleHeight,
            };

            SafeGameSettingsEditor editor = new(() => DateTimeOffset.UnixEpoch, () => false);
            GameInspectionSnapshot snapshot = CreateSnapshot(userData) with
            {
                BinarySettingsFile = new BinarySettingsFileSnapshot(
                    "System.sav",
                    systemSave,
                    true,
                    currentBytes.Length,
                    DateTimeOffset.UnixEpoch,
                    "Decoded",
                    staleGraphics),
            };

            SettingsChangePlan plan = editor.CreatePlan(
                snapshot,
                [SystemChange(SystemSaveSettingKeys.FullscreenResolution, requestedResolution)]);

            SettingChangePreview? preview = plan.Changes.FirstOrDefault(
                change => string.Equals(change.Key, SystemSaveSettingKeys.FullscreenResolution, StringComparison.Ordinal));
            Assert.NotNull(preview);
            // The preview must reflect the value read from disk (B), never the stale
            // snapshot's value (A).
            Assert.Equal(fileResolution, preview!.Before);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static (int Width, int Height) ParseResolution(string value)
    {
        string[] parts = value.Split('x');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string CreateUserData(string temporary)
    {
        string userData = Path.Combine(temporary, "Saved");
        Directory.CreateDirectory(Path.Combine(userData, "Config", "WindowsNoEditor"));
        return userData;
    }

    private static SettingChangeRequest SystemChange(string key, string value) =>
        new("Resolution", "System.sav", "GraphicsOptions", key, value);

    private static GameInspectionSnapshot CreateSnapshot(string userData) =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(
                StoreKind.Steam,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "library",
                "install",
                AncestorsEnhanced.Core.AncestorsGameProfile.SupportedBuildId,
                ExecutableExists: true,
                AncestorsEnhanced.Core.AncestorsGameProfile.SupportedContentSignature),
            userData,
            [],
            null,
            [],
            []);
}
