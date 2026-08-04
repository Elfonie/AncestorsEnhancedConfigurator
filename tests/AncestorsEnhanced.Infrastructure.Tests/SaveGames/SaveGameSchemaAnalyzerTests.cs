using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.Tests.SystemSave;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SaveGameSchemaAnalyzerTests
{
    [Fact]
    public void AnalyzeDivesIntoNestedStructs()
    {
        byte[] save = VerifiedSystemSaveFixture.Read();
        var analyzer = new SaveGameSchemaAnalyzer();
        SaveGameSchemaAnalysis result = analyzer.Analyze(save);

        Assert.NotEmpty(result.Tree);
        Assert.NotNull(result.Tree[0].Children.SingleOrDefault(child => child.Name == "Options"));
    }

    [Fact]
    public void AnalyzeExposesNestedPropertyOffsets()
    {
        byte[] save = VerifiedSystemSaveFixture.Read();
        var analyzer = new SaveGameSchemaAnalyzer();
        SaveGameSchemaAnalysis result = analyzer.Analyze(save);

        SaveGameSchemaNode options = result.Tree[0].Children
            .Single(child => child.Name == "Options");
        SaveGameSchemaNode graphics = options.Children
            .Single(child => child.Name == "GraphicOptions");
        Assert.Contains(graphics.Children, child => child.Name == "FullScreenResolution");
        Assert.Contains(graphics.Children, child => child.Name == "Brightness");
        Assert.Contains(graphics.Children, child => child.Name == "QualityLevel");
    }

    [Fact]
    public void AnalyzeDistinguishesBinaryStructsFromNestedLists()
    {
        byte[] save = VerifiedSystemSaveFixture.Read();
        var analyzer = new SaveGameSchemaAnalyzer();
        SaveGameSchemaAnalysis result = analyzer.Analyze(save);

        SaveGameSchemaNode options = result.Tree[0].Children
            .Single(child => child.Name == "Options");
        SaveGameSchemaNode graphics = options.Children
            .Single(child => child.Name == "GraphicOptions");

        SaveGameSchemaNode resolution = graphics.Children
            .Single(child => child.Name == "FullScreenResolution");
        Assert.Equal("IntPoint", resolution.StructType);
        Assert.Empty(resolution.Children);

        SaveGameSchemaNode scalability = graphics.Children
            .Single(child => child.Name == "ScalabilitySetting");
        Assert.Equal("ScalabilitySetting", scalability.StructType);
        Assert.NotEmpty(scalability.Children);
    }
}