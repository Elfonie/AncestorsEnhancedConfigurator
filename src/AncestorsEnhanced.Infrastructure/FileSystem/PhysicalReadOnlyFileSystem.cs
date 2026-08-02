namespace AncestorsEnhanced.Infrastructure.FileSystem;

internal sealed class PhysicalReadOnlyFileSystem : IReadOnlyFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public ReadOnlyFileMetadata GetFileMetadata(string path)
    {
        FileInfo file = new(path);
        return new ReadOnlyFileMetadata(
            file.Name,
            file.FullName,
            file.Length,
            file.LastWriteTimeUtc);
    }

    public IReadOnlyList<ReadOnlyFileMetadata> EnumerateFiles(
        string directoryPath,
        string searchPattern)
    {
        return Directory
            .EnumerateFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly)
            .Select(GetFileMetadata)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

}
