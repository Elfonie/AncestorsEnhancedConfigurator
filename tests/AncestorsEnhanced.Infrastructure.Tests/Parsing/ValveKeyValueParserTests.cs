using AncestorsEnhanced.Infrastructure.Parsing;

namespace AncestorsEnhanced.Infrastructure.Tests.Parsing;

public sealed class ValveKeyValueParserTests
{
    [Fact]
    public void ParseReadsNestedObjectsCommentsAndEscapedPaths()
    {
        const string Content = """
            "libraryfolders"
            {
                "0"
                {
                    "path" "C:\\Program Files (x86)\\Steam"
                }
            }
            """;

        ValveKeyValueObject root = ValveKeyValueParser.Parse(Content);

        string? path = root
            .GetObject("libraryfolders")
            ?.GetObject("0")
            ?.GetString("path");
        Assert.Equal(@"C:\Program Files (x86)\Steam", path);
    }

    [Fact]
    public void ParseRejectsExcessiveNestingDepth()
    {
        // 70 nested objects exceed the 64-level depth limit. Before the depth fix the
        // recursion did not increment the depth, so this was accepted (F157).
        const int depth = 70;
        string content = string.Concat(Enumerable.Repeat("\"k\" { ", depth)) +
            new string('}', depth);

        Assert.Throws<FormatException>(() => ValveKeyValueParser.Parse(content));
    }

    [Fact]
    public void ParseRejectsAnUnclosedObject()
    {
        const string Content = "\"AppState\" { \"appid\" \"536270\"";

        Assert.Throws<FormatException>(() => ValveKeyValueParser.Parse(Content));
    }
}
