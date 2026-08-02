using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Parsing;

namespace AncestorsEnhanced.Infrastructure.Tests.Parsing;

public sealed class IniSnapshotParserTests
{
    [Fact]
    public void ParsePreservesDuplicateAndUnrealDirectiveKeys()
    {
        const string Content = """
            ; comment
            [Core.System]
            Paths=../../../Engine/Content
            Paths=%GAMEDIR%Content

            [/Script/MoviePlayer.MoviePlayerSettings]
            !StartupMovies=ClearArray
            """;

        IReadOnlyList<IniSettingSnapshot> settings = IniSnapshotParser.Parse(Content);

        Assert.Collection(
            settings,
            first =>
            {
                Assert.Equal("Core.System", first.Section);
                Assert.Equal("Paths", first.Key);
                Assert.Equal("../../../Engine/Content", first.Value);
                Assert.Equal(3, first.LineNumber);
            },
            second => Assert.Equal("%GAMEDIR%Content", second.Value),
            third =>
            {
                Assert.Equal("/Script/MoviePlayer.MoviePlayerSettings", third.Section);
                Assert.Equal("!StartupMovies", third.Key);
                Assert.Equal("ClearArray", third.Value);
            });
    }
}
