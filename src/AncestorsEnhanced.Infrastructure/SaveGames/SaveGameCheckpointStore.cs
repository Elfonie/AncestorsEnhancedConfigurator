using System.Globalization;
using System.Text;
using System.Text.Json;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

internal sealed class SaveGameCheckpointStore(
    Func<DateTimeOffset> utcNow,
    int maxCheckpointsPerSlot,
    Func<DateTimeOffset, string>? newCheckpointId = null)
{
    private const string ManifestFileName = "checkpoint.json";
    private const int ManifestVersion = 1;
    private const int MaximumManifestSize = 64 * 1024;
    private const int MaximumSaveSize = 64 * 1024 * 1024;
    private const int MaximumOriginLength = 64;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<DateTimeOffset> _utcNow = utcNow;
    private readonly Func<DateTimeOffset, string> _newCheckpointId =
        newCheckpointId ?? SaveGamePaths.NewCheckpointId;

    public string Create(string userDataDirectory, int slotNumber, byte[] content, string origin = "Manual")
    {
        if (maxCheckpointsPerSlot < 1)
        {
            throw new InvalidOperationException("The retention limit must be at least one checkpoint per slot.");
        }

        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("The save file is empty and was not backed up.");
        }
        ValidateOrigin(origin);
        ValidateSave(content);

        string digest = Sha256(content);
        string slotRoot = SaveGamePaths.GetSlotRoot(userDataDirectory, slotNumber);
        Directory.CreateDirectory(slotRoot);

        DateTimeOffset createdAt = _utcNow();
        string checkpointId;
        string checkpointDirectory;
        string tempDirectory;
        int collisionAttempts = 0;
        do
        {
            if (++collisionAttempts > 8)
            {
                throw new IOException("A unique checkpoint identifier could not be created.");
            }
            checkpointId = _newCheckpointId(createdAt);
            SaveGamePaths.ValidateCheckpointId(checkpointId);
            checkpointDirectory = Path.Combine(slotRoot, checkpointId);
            tempDirectory = Path.Combine(slotRoot, $".{checkpointId}.tmp");
        }
        while (Directory.Exists(checkpointDirectory) || Directory.Exists(tempDirectory));
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                throw new IOException("A stale temporary checkpoint directory already exists.");
            }

            // 1. Build the checkpoint fully in a temporary directory.
            Directory.CreateDirectory(tempDirectory);
            WriteBytesAtomically(Path.Combine(tempDirectory, "save.sav"), content);
            WriteManifest(
                tempDirectory,
                new CheckpointManifest(ManifestVersion, createdAt, content.Length, digest, origin));

            // 2. Re-read and validate the manifest before publishing anything.
            CheckpointManifest? manifest = ReadManifest(tempDirectory);
            if (manifest is null ||
                manifest.SizeBytes != content.Length ||
                !string.Equals(manifest.Sha256, digest, StringComparison.Ordinal) ||
                !string.Equals(manifest.Origin, origin, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The checkpoint manifest failed validation.");
            }

            // 3. Validate the stored save before publishing.
            byte[] stored = ReadStableBounded(Path.Combine(tempDirectory, "save.sav"), MaximumSaveSize);
            if (!string.Equals(Sha256(stored), digest, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The checkpoint save failed validation.");
            }
            ValidateSave(stored);

            // 4. Publish atomically: the temporary directory becomes the final one.
            Directory.Move(tempDirectory, checkpointDirectory);
        }
        catch
        {
            TryDeleteDirectory(tempDirectory);
            throw;
        }

        // Publication above is the commit point. Retention is best-effort and must
        // never turn a successfully published checkpoint into a reported failure.
        try
        {
            EnforceCap(userDataDirectory, slotRoot, slotNumber, maxCheckpointsPerSlot, checkpointDirectory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            System.Security.SecurityException)
        {
        }
        return checkpointId;
    }

    public static byte[] Read(string userDataDirectory, int slotNumber, string checkpointId)
    {
        string path = SaveGamePaths.GetCheckpointPath(userDataDirectory, slotNumber, checkpointId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The checkpoint could not be found.", path);
        }

        byte[] content = ReadStableBounded(path, MaximumSaveSize);
        CheckpointManifest? manifest = ReadManifest(
            Path.Combine(SaveGamePaths.GetSlotRoot(userDataDirectory, slotNumber), checkpointId));
        if (manifest is null || manifest.SizeBytes != content.Length ||
            !string.Equals(manifest.Sha256, Sha256(content), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The checkpoint failed validation and will not be loaded.");
        }
        ValidateSave(content);

        return content;
    }

    public static IReadOnlyList<SaveGameCheckpoint> ListCheckpoints(string userDataDirectory, int slotNumber)
    {
        string slotRoot = SaveGamePaths.GetSlotRoot(userDataDirectory, slotNumber);
        if (!Directory.Exists(slotRoot))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(slotRoot)
            .Select(path => TryReadCheckpointMetadata(path, slotNumber))
            .OfType<SaveGameCheckpoint>()
            .OrderByDescending(checkpoint => checkpoint.CreatedAtUtc)
            .ToArray();
    }

    private static void EnforceCap(
        string userDataDirectory,
        string slotRoot,
        int slotNumber,
        int maxCheckpointsPerSlot,
        string protectedCheckpointDirectory)
    {
        string[] directories = Directory.EnumerateDirectories(slotRoot).ToArray();
        List<string> checkpoints = directories
            .Where(path => TryReadCheckpointMetadata(path, slotNumber) is not null)
            // Checkpoint IDs begin with a fixed-width UTC timestamp. Ordering by the
            // immutable directory name avoids trusting mutable manifest timestamps.
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        int overflow = checkpoints.Count - maxCheckpointsPerSlot;
        HashSet<string>? favorites = ReadFavoriteCheckpointIds(userDataDirectory, slotNumber);
        // Metadata is advisory. If it cannot be read, retain everything rather than
        // risk deleting a checkpoint the user explicitly pinned.
        if (favorites is null)
        {
            return;
        }
        foreach (string checkpoint in checkpoints
                     .Where(path => !string.Equals(
                         Path.GetFullPath(path),
                         protectedCheckpointDirectory,
                         PathComparison)
                         && !favorites.Contains(Path.GetFileName(path)))
                     .Take(overflow))
        {
            TryDeleteDirectory(checkpoint);
        }
    }

    private static HashSet<string>? ReadFavoriteCheckpointIds(string userDataDirectory, int slotNumber)
    {
        string settingsPath = Path.Combine(userDataDirectory, "AncestorsEnhanced_ToolSettings.json");
        if (!File.Exists(settingsPath))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            FileInfo info = new(settingsPath);
            if (info.Length > 1024 * 1024)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(settingsPath));
            if (!document.RootElement.TryGetProperty("CheckpointMetadata", out JsonElement metadata) ||
                metadata.ValueKind != JsonValueKind.Object)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            string prefix = slotNumber.ToString(CultureInfo.InvariantCulture) + ":";
            var favorites = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in metadata.EnumerateObject())
            {
                if (!property.Name.StartsWith(prefix, StringComparison.Ordinal) ||
                    !property.Value.TryGetProperty("IsFavorite", out JsonElement isFavorite) ||
                    isFavorite.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                string id = property.Name[prefix.Length..];
                if (id.Length > 0)
                {
                    favorites.Add(id);
                }
            }

            return favorites;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static SaveGameCheckpoint? TryReadCheckpointMetadata(string path, int slotNumber)
    {
        try
        {
            if (!IsFinalCheckpointDirectory(path))
            {
                return null;
            }

            CheckpointManifest? manifest = ReadManifest(path);
            if (manifest is null)
            {
                return null;
            }
            string savePath = Path.Combine(path, "save.sav");
            if (!File.Exists(savePath) ||
                File.GetAttributes(savePath).HasFlag(FileAttributes.ReparsePoint) ||
                new FileInfo(savePath).Length != manifest.SizeBytes)
            {
                return null;
            }
            return new SaveGameCheckpoint(
                    Path.GetFileName(path), manifest.CreatedAtUtc,
                    slotNumber.ToString(CultureInfo.InvariantCulture), manifest.SizeBytes,
                    manifest.Sha256, manifest.Origin);
        }
        catch (Exception exception) when (IsCandidateReadException(exception))
        {
            return null;
        }
    }

    private static bool IsNormalDirectory(string path) =>
        Directory.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static bool IsFinalCheckpointDirectory(string path)
    {
        try
        {
            if (!IsNormalDirectory(path))
            {
                return false;
            }

            SaveGamePaths.ValidateCheckpointId(Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (IsCandidateReadException(exception))
        {
            return false;
        }
    }

    private static bool IsCandidateReadException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or
            InvalidOperationException or System.Security.SecurityException;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            string root = Path.GetDirectoryName(Path.GetFullPath(path))
                ?? throw new InvalidOperationException("The checkpoint root is invalid.");
            DeleteDirectorySafely(root, path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
        }
    }

    private static void WriteManifest(string checkpointDirectory, CheckpointManifest manifest)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        WriteBytesAtomically(Path.Combine(checkpointDirectory, ManifestFileName), bytes);
    }

    private static CheckpointManifest? ReadManifest(string checkpointDirectory)
    {
        string path = Path.Combine(checkpointDirectory, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            CheckpointManifest? manifest = JsonSerializer.Deserialize<CheckpointManifest>(
                ReadStableBounded(path, MaximumManifestSize));
            if (manifest is null || manifest.Version is not (0 or ManifestVersion) ||
                manifest.SizeBytes is < 1 or > MaximumSaveSize ||
                manifest.Sha256.Length != 64 || !manifest.Sha256.All(char.IsAsciiHexDigit))
            {
                return null;
            }
            ValidateOrigin(manifest.Origin);
            return manifest;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or
                System.Security.SecurityException)
        {
            return null;
        }
    }

    private static void ValidateSave(byte[] content)
    {
        if (content.Length is < 1 or > MaximumSaveSize)
        {
            throw new InvalidDataException("The checkpoint save has an invalid size.");
        }
        byte[] decompressed = SnappyBlockCodec.Decode(content);
        if (decompressed.Length == 0)
        {
            throw new InvalidDataException("The checkpoint save has an empty payload.");
        }
    }

    private static void ValidateOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > MaximumOriginLength ||
            (origin is not ("Manual" or "AutoBackup" or "PreRestore") &&
             !origin.StartsWith("Cheat:", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The checkpoint origin is invalid.");
        }
    }
}

internal sealed record CheckpointManifest(
    int Version,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    string Sha256,
    string Origin = "Manual");
