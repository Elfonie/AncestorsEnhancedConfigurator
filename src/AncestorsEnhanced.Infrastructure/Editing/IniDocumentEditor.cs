using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class IniDocumentEditor
{
    public static string Apply(
        string content,
        IReadOnlyList<SettingChangeRequest> changes)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(changes);

        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hasFinalNewline = content.EndsWith('\n') || content.EndsWith('\r');
        List<string> lines = [.. content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')];

        if (hasFinalNewline && lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        foreach (SettingChangeRequest change in changes)
        {
            ApplyOne(lines, change);
        }

        string result = string.Join(newline, lines);
        return hasFinalNewline && lines.Count > 0 ? result + newline : result;
    }

    public static string? FindLastValue(
        string content,
        string section,
        string key)
    {
        string currentSection = string.Empty;
        string? value = null;

        foreach (string sourceLine in content
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n'))
        {
            string line = sourceLine.Trim();
            if (TryReadSection(line, out string? parsedSection))
            {
                currentSection = parsedSection;
                continue;
            }

            if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase) &&
                TryReadKeyValue(line, out string? parsedKey, out string? parsedValue) &&
                string.Equals(parsedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                value = parsedValue;
            }
        }

        return value;
    }

    private static void ApplyOne(List<string> lines, SettingChangeRequest change)
    {
        List<int> matchingLines = [];
        int lastSectionStart = -1;
        int lastSectionEnd = -1;
        string currentSection = string.Empty;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].Trim();
            if (TryReadSection(line, out string? parsedSection))
            {
                if (lastSectionStart >= 0 && lastSectionEnd < lastSectionStart)
                {
                    lastSectionEnd = index;
                }

                currentSection = parsedSection;
                if (string.Equals(currentSection, change.Section, StringComparison.OrdinalIgnoreCase))
                {
                    lastSectionStart = index;
                    lastSectionEnd = -1;
                }

                continue;
            }

            if (string.Equals(currentSection, change.Section, StringComparison.OrdinalIgnoreCase) &&
                TryReadKeyValue(line, out string? key, out _) &&
                string.Equals(key, change.Key, StringComparison.OrdinalIgnoreCase))
            {
                matchingLines.Add(index);
            }
        }

        if (lastSectionStart >= 0 && lastSectionEnd < lastSectionStart)
        {
            lastSectionEnd = lines.Count;
        }

        if (change.Value is null)
        {
            for (int index = matchingLines.Count - 1; index >= 0; index--)
            {
                lines.RemoveAt(matchingLines[index]);
            }

            return;
        }

        if (matchingLines.Count > 0)
        {
            int index = matchingLines[^1];
            string indentation = lines[index][..(lines[index].Length - lines[index].TrimStart().Length)];
            lines[index] = $"{indentation}{change.Key}={change.Value}";
            return;
        }

        if (lastSectionStart >= 0)
        {
            lines.Insert(lastSectionEnd, $"{change.Key}={change.Value}");
            return;
        }

        if (lines.Count > 0 && lines[^1].Length != 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add($"[{change.Section}]");
        lines.Add($"{change.Key}={change.Value}");
    }

    private static bool TryReadSection(string line, out string section)
    {
        if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
        {
            section = line[1..^1].Trim();
            return section.Length > 0;
        }

        section = string.Empty;
        return false;
    }

    private static bool TryReadKeyValue(
        string line,
        out string key,
        out string value)
    {
        if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        int separator = line.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separator].Trim();
        value = line[(separator + 1)..].Trim();
        return key.Length > 0;
    }
}
