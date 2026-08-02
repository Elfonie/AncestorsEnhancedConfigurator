namespace AncestorsEnhanced.Core.Editing;

public static class SystemGraphicsOptionCatalog
{
    public static IReadOnlyList<int> FrameRateLimits { get; } =
        Array.AsReadOnly<int>([0, 30, 60, 120, 144, 160, 165, 180, 200, 240]);

    public static IReadOnlyList<string> Resolutions { get; } =
        Array.AsReadOnly<string>(
        [
            "1024x576", "1152x648", "1280x720", "1280x800", "1366x768", "1440x900",
            "1600x900", "1680x1050", "1920x1080", "1920x1200", "2560x1440", "2560x1600",
            "3840x2160", "7680x4320",
        ]);

    public static int GetFrameRateIndex(int frameRate)
    {
        for (int index = 0; index < FrameRateLimits.Count; index++)
        {
            if (FrameRateLimits[index] == frameRate)
            {
                return index;
            }
        }

        return -1;
    }

    public static bool IsSupportedResolution(string value) =>
        Resolutions.Contains(value, StringComparer.Ordinal);
}
