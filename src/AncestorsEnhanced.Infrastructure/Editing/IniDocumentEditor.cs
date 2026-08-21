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

        List<IniLine> lines = ParseLines(content);
        string newline = lines.Select(line => line.Ending).FirstOrDefault(ending => ending.Length > 0) ?? "\n";
        bool hasFinalNewline = lines.Count > 0 && lines[^1].Ending.Length > 0;

        foreach (SettingChangeRequest change in changes)
        {
            ApplyOne(lines, change, newline, hasFinalNewline);
        }

        return string.Concat(lines.Select(line => line.Text + line.Ending));
    }

    public static string? FindLastValue(
        string content,
        string section,
        string key)
    {
        string currentSection = string.Empty;
        string? value = null;

        foreach (IniLine sourceLine in ParseLines(content))
        {
            string line = sourceLine.Text.Trim();
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

    private static void ApplyOne(
        List<IniLine> lines,
        SettingChangeRequest change,
        string newline,
        bool preserveFinalNewline)
    {
        List<int> matchingLines = [];
        int lastSectionStart = -1;
        int lastSectionEnd = -1;
        string currentSection = string.Empty;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].Text.Trim();
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
            string indentation = lines[index].Text[..(lines[index].Text.Length - lines[index].Text.TrimStart().Length)];
            lines[index] = lines[index] with { Text = $"{indentation}{change.Key}={change.Value}" };
            return;
        }

        if (lastSectionStart >= 0)
        {
            InsertLine(lines, lastSectionEnd, $"{change.Key}={change.Value}", newline, preserveFinalNewline);
            return;
        }

        if (lines.Count > 0 && lines[^1].Text.Length != 0)
        {
            InsertLine(lines, lines.Count, string.Empty, newline, preserveFinalNewline);
        }

        InsertLine(lines, lines.Count, $"[{change.Section}]", newline, preserveFinalNewline);
        InsertLine(lines, lines.Count, $"{change.Key}={change.Value}", newline, preserveFinalNewline);
    }

    private static void InsertLine(
        List<IniLine> lines,
        int index,
        string text,
        string newline,
        bool preserveFinalNewline)
    {
        if (index < lines.Count)
        {
            lines.Insert(index, new IniLine(text, newline));
            return;
        }

        if (lines.Count > 0 && lines[^1].Ending.Length == 0)
        {
            lines[^1] = lines[^1] with { Ending = newline };
        }
        lines.Add(new IniLine(text, preserveFinalNewline ? newline : string.Empty));
    }

    private static List<IniLine> ParseLines(string content)
    {
        var lines = new List<IniLine>();
        int start = 0;
        for (int index = 0; index < content.Length; index++)
        {
            if (content[index] is not ('\r' or '\n'))
            {
                continue;
            }
            int endingLength = content[index] == '\r' && index + 1 < content.Length && content[index + 1] == '\n'
                ? 2
                : 1;
            lines.Add(new IniLine(content[start..index], content.Substring(index, endingLength)));
            index += endingLength - 1;
            start = index + 1;
        }
        if (start < content.Length)
        {
            lines.Add(new IniLine(content[start..], string.Empty));
        }
        return lines;
    }

    private sealed record IniLine(string Text, string Ending);

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
