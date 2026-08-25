using AncestorsEnhanced.Core.Profiles;

namespace AncestorsEnhanced.Infrastructure.Profiles;

public sealed class UserProfileLibrary : IUserProfileLibrary
{
    private const string Extension = ".aecprofile";
    private readonly string _rootDirectory;

    public UserProfileLibrary()
        : this(GetDefaultDirectory())
    {
    }

    internal UserProfileLibrary(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));
    }

    public IReadOnlyList<StoredUserProfile> List()
    {
        if (!Directory.Exists(_rootDirectory) || IsReparsePoint(_rootDirectory))
        {
            return [];
        }

        var profiles = new List<StoredUserProfile>();
        foreach (string path in Directory.EnumerateFiles(_rootDirectory, $"*{Extension}", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (IsReparsePoint(path) || !TryGetId(path, out string? id))
                {
                    continue;
                }
                profiles.Add(new StoredUserProfile(id!, ReadFile(path)));
            }
            catch (Exception exception) when (IsProfileReadException(exception))
            {
            }
        }

        return profiles
            .OrderBy(profile => profile.Profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public StoredUserProfile Save(UserProfile profile)
    {
        byte[] content = UserProfileCodec.Serialize(profile);
        EnsureRoot();

        for (int attempt = 0; attempt < 8; attempt++)
        {
            string id = Guid.NewGuid().ToString("N");
            string finalPath = GetOwnedPath(id);
            string temporaryPath = Path.Combine(_rootDirectory, $".{id}.tmp");
            try
            {
                WriteNewFile(temporaryPath, content);
                File.Move(temporaryPath, finalPath, overwrite: false);
                return new StoredUserProfile(id, profile);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        throw new IOException("A unique profile file could not be created.");
    }

    public UserProfile Read(string id)
    {
        string path = GetOwnedPath(id);
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            throw new FileNotFoundException("The saved profile could not be found.", path);
        }
        return ReadFile(path);
    }

    public UserProfile ReadExternal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) || IsReparsePoint(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Select a normal .aecprofile file.");
        }
        return ReadFile(fullPath);
    }

    private static string GetDefaultDirectory()
    {
        string? localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "AncestorsEnhanced", "Profiles");
        }

        string? home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            return Path.Combine(home, ".local", "share", "AncestorsEnhanced", "Profiles");
        }

        throw new InvalidOperationException("A safe profile-library folder could not be determined.");
    }

    private void EnsureRoot()
    {
        Directory.CreateDirectory(_rootDirectory);
        if (IsReparsePoint(_rootDirectory))
        {
            throw new IOException("The profile-library folder is a link and will not be used.");
        }
    }

    private string GetOwnedPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new InvalidOperationException("The profile identifier is invalid.");
        }
        return Path.Combine(_rootDirectory, id + Extension);
    }

    private static UserProfile ReadFile(string path)
    {
        var file = new FileInfo(path);
        if (file.Length is < 2 or > UserProfileCodec.MaximumFileSize)
        {
            throw new InvalidDataException("The profile file has an invalid size.");
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] content = new byte[file.Length];
        stream.ReadExactly(content);
        return UserProfileCodec.Deserialize(content);
    }

    private static bool TryGetId(string path, out string? id)
    {
        id = Path.GetFileNameWithoutExtension(path);
        return Guid.TryParseExact(id, "N", out _);
    }

    private static void WriteNewFile(string path, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsProfileReadException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or
        System.Security.SecurityException;
}
