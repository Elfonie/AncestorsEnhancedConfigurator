using System.Text.Json;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.Paks;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal static class InspectionLimits
{
    public const long TextFile = 4 * 1024 * 1024;
    public const long SystemSave = 1024 * 1024;
}

internal static class InspectionErrors
{
    public static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Security.SecurityException or FormatException or JsonException or
            ArgumentException or NotSupportedException or OverflowException;
}

internal sealed class GameInstallationFactory(IReadOnlyFileSystem fileSystem)
{
    public GameInstallationSnapshot? CreateWindows(
        StoreKind store,
        string installDirectory,
        string? buildId)
    {
        string fullInstall = Path.GetFullPath(installDirectory);
        string executable = GetExecutablePath(fullInstall);
        if (!fileSystem.FileExists(executable))
        {
            return null;
        }

        (string? Signature, bool Failed) signature = ReadContentSignature(fullInstall);
        return new GameInstallationSnapshot(
            store,
            HostKind.Windows,
            CompatibilityLayerKind.None,
            fullInstall,
            fullInstall,
            buildId,
            true,
            signature.Signature,
            signature.Failed);
    }

    public static string GetExecutablePath(string installDirectory) => Path.Combine(
        installDirectory,
        "Ancestors",
        "Binaries",
        "Win64",
        "Ancestors-Win64-Shipping.exe");

    public GameInstallationSnapshot? CreateLinux(
        StoreKind store,
        string installDirectory,
        string? buildId,
        CompatibilityLayerKind compatibilityLayer,
        string? compatibilityPrefixPath = null)
    {
        string fullInstall = Path.GetFullPath(installDirectory);
        string executable = GetExecutablePath(fullInstall);
        if (!fileSystem.FileExists(executable))
        {
            return null;
        }

        (string? Signature, bool Failed) signature = ReadContentSignature(fullInstall);
        return new GameInstallationSnapshot(
            store,
            HostKind.Linux,
            compatibilityLayer,
            fullInstall,
            fullInstall,
            buildId,
            true,
            signature.Signature,
            signature.Failed,
            NormalizePrefixOrNull(compatibilityPrefixPath));
    }

    private static string? NormalizePrefixOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            return null;
        }
    }

    public static (string? Signature, bool Failed) ReadContentSignature(string installDirectory)
    {
        try
        {
            string paks = Path.Combine(installDirectory, "Ancestors", "Content", "Paks");
            string main = PakV5Archive.ReadIndexIdentity(
                Path.Combine(paks, "Ancestors-WindowsNoEditor.pak"));
            string level = PakV5Archive.ReadIndexIdentity(Path.Combine(paks, "VL01E01.pak"));
            return ($"{main}:{level[(level.IndexOf(':') + 1)..]}", false);
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            // A read failure is not the same as "this evidence does not exist on this
            // platform"; it must fail closed.
            return (null, true);
        }
    }
}
