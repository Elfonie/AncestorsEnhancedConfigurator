namespace AncestorsEnhanced.Infrastructure.FileSystem;

internal interface IReadOnlyFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    ReadOnlyFileMetadata GetFileMetadata(string path);

    IReadOnlyList<ReadOnlyFileMetadata> EnumerateFiles(string directoryPath, string searchPattern);

    string ComputeSha256(string path);
}
