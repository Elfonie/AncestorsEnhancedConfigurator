using System.Globalization;
using System.Text;
using System.Text.Json;
using AncestorsEnhanced.Core.SaveGames;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

internal sealed class SaveGameCheckpointStore(
    Func<DateTimeOffset> utcNow,
    int maxCheckpointsPerSlot)
{
    private const string ManifestFileName = "checkpoint.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<DateTimeOffset> _utcNow = utcNow;

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

        string digest = Sha256(content);
        string slotRoot = SaveGamePaths.GetSlotRoot(userDataDirectory, slotNumber);
        Directory.CreateDirectory(slotRoot);

        DateTimeOffset createdAt = _utcNow();
        string checkpointId = SaveGamePaths.NewCheckpointId(createdAt);
        string checkpointDirectory = Path.Combine(slotRoot, checkpointId);
        string tempDirectory = Path.Combine(slotRoot, $".{checkpointId}.tmp");
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
                new CheckpointManifest(createdAt, content.Length, digest, origin));

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
            byte[] stored = File.ReadAllBytes(Path.Combine(tempDirectory, "save.sav"));
            if (!string.Equals(Sha256(stored), digest, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The checkpoint save failed validation.");
            }

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
            EnforceCap(slotRoot, maxCheckpointsPerSlot);
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

        byte[] content = File.ReadAllBytes(path);
        CheckpointManifest? manifest = ReadManifest(
            Path.Combine(SaveGamePaths.GetSlotRoot(userDataDirectory, slotNumber), checkpointId));
        if (manifest is null ||
            !string.Equals(manifest.Sha256, Sha256(content), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The checkpoint failed validation and will not be loaded.");
        }

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
            .Where(path => IsFinalCheckpointDirectory(path))
            .Select(path =>
            {
                CheckpointManifest? manifest = ReadManifest(path);
                string checkpointId = Path.GetFileName(path);
                return manifest is null
                    ? null
                    : new SaveGameCheckpoint(
                        checkpointId,
                        manifest.CreatedAtUtc,
                        slotNumber.ToString(CultureInfo.InvariantCulture),
                        manifest.SizeBytes,
                        manifest.Sha256,
                        manifest.Origin);
            })
            .OfType<SaveGameCheckpoint>()
            .OrderByDescending(checkpoint => checkpoint.CreatedAtUtc)
            .ToArray();
    }

    private static void EnforceCap(string slotRoot, int maxCheckpointsPerSlot)
    {
        List<(string Path, DateTimeOffset CreatedAtUtc)> checkpoints = Directory
            .EnumerateDirectories(slotRoot)
            .Where(IsFinalCheckpointDirectory)
            .Select(path => (Path: path, Manifest: ReadManifest(path)))
            .Where(entry => entry.Manifest is not null)
            .Select(entry => (entry.Path, entry.Manifest!.CreatedAtUtc))
            .OrderBy(entry => entry.CreatedAtUtc)
            .ToList();

        int overflow = checkpoints.Count - maxCheckpointsPerSlot;
        for (int index = 0; index < overflow; index++)
        {
            TryDeleteDirectory(checkpoints[index].Path);
        }
    }

    private static bool IsNormalDirectory(string path) =>
        Directory.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static bool IsFinalCheckpointDirectory(string path)
    {
        if (!IsNormalDirectory(path))
        {
            return false;
        }

        try
        {
            SaveGamePaths.ValidateCheckpointId(Path.GetFileName(path));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
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
            return JsonSerializer.Deserialize<CheckpointManifest>(
                File.ReadAllText(path, Encoding.UTF8));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record CheckpointManifest(
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    string Sha256,
    string Origin = "Manual");
