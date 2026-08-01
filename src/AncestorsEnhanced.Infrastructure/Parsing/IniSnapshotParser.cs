using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Infrastructure.Parsing;

internal static class IniSnapshotParser
{
    public static IReadOnlyList<IniSettingSnapshot> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        List<IniSettingSnapshot> settings = [];
        string section = string.Empty;
        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }

            int separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            string value = line[(separatorIndex + 1)..].Trim();
            settings.Add(new IniSettingSnapshot(section, key, value, index + 1));
        }

        return settings;
    }
}
