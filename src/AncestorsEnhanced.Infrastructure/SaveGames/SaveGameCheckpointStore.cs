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

    public string Create(string userDataDirectory, int slotNumber, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("The save file is empty and was not backed up.");
        }

        string slotRoot = SaveGamePaths.GetSlotRoot(userDataDirectory, slotNumber);
        Directory.CreateDirectory(slotRoot);

        DateTimeOffset createdAt = _utcNow();
        string checkpointId = $"{createdAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..32];
        string checkpointDirectory = Path.Combine(slotRoot, checkpointId);
        Directory.CreateDirectory(checkpointDirectory);

        WriteBytesAtomically(Path.Combine(checkpointDirectory, "save.sav"), content);
        WriteManifest(
            checkpointDirectory,
            new CheckpointManifest(createdAt, content.Length, Sha256(content)));

        EnforceCap(slotRoot, maxCheckpointsPerSlot);
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
            .Where(path => IsNormalDirectory(path))
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
                        manifest.Sha256);
            })
            .OfType<SaveGameCheckpoint>()
            .OrderByDescending(checkpoint => checkpoint.CreatedAtUtc)
            .ToArray();
    }

    private static void EnforceCap(string slotRoot, int maxCheckpointsPerSlot)
    {
        List<(string Path, DateTimeOffset CreatedAtUtc)> checkpoints = Directory
            .EnumerateDirectories(slotRoot)
            .Where(IsNormalDirectory)
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
    string Sha256);
