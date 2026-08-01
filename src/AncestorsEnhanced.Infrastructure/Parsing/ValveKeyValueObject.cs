namespace AncestorsEnhanced.Infrastructure.Parsing;

internal sealed class ValveKeyValueObject
{
    private readonly List<ValveKeyValueEntry> _entries = [];

    public IReadOnlyList<ValveKeyValueEntry> Entries => _entries;

    public void Add(ValveKeyValueEntry entry) => _entries.Add(entry);

    public string? GetString(string key) => _entries
        .LastOrDefault(entry =>
            entry.Child is null &&
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
        ?.Value;

    public ValveKeyValueObject? GetObject(string key) => _entries
        .LastOrDefault(entry =>
            entry.Child is not null &&
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
        ?.Child;
}

internal sealed record ValveKeyValueEntry(
    string Key,
    string? Value,
    ValveKeyValueObject? Child);
