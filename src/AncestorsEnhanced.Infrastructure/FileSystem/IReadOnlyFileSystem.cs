namespace AncestorsEnhanced.Infrastructure.FileSystem;

internal interface IReadOnlyFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    byte[] ReadAllBytes(string path);

    Stream OpenRead(string path);

    ReadOnlyFileMetadata GetFileMetadata(string path);

    IReadOnlyList<ReadOnlyFileMetadata> EnumerateFiles(string directoryPath, string searchPattern);

    IReadOnlyList<string> EnumerateDirectories(string directoryPath);

}
