namespace AncestorsEnhanced.Core.Editing;

public sealed record ConfigurationFileChangePlan(
    string FileName,
    string FullPath,
    bool Existed,
    string OriginalSha256,
    byte[] OriginalContent,
    byte[] UpdatedContent);
