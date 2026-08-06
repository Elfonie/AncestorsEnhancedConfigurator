using System.Buffers.Binary;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
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
    [Fact]
    public void MissingTerminatorReportsOffset()
    {
        // A property without a None terminator.
        byte[] save = ConcatBytes(
            EncodeString("Foo"),
            EncodeString("IntProperty"),
            LittleEndian(8L),
            new byte[] { 0 }, // hasPropertyGuid = 0
            LittleEndian(42));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new SaveGameSchemaAnalyzer().Analyze(UncompressedWrapper(save)));

        Assert.Contains("terminator", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OversizedStringIsRejected()
    {
        // 70_000-byte string header exceeds the 65536 limit.
        byte[] save = ConcatBytes(
            LittleEndian(70_000),
            new byte[70_000 - 1],
            new byte[1]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new SaveGameSchemaAnalyzer().Analyze(UncompressedWrapper(save)));

        Assert.Contains("maximum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] UncompressedWrapper(byte[] decompressed) =>
        SnappyBlockCodec.EncodeLiteral(decompressed);

    private static byte[] ConcatBytes(params byte[][] parts)
    {
        using var stream = new MemoryStream();
        foreach (byte[] part in parts)
        {
            stream.Write(part);
        }

        return stream.ToArray();
    }

    private static byte[] LittleEndian(long value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] EncodeString(string value)
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] result = new byte[text.Length + 5];
        BinaryPrimitives.WriteInt32LittleEndian(result, text.Length + 1);
        text.CopyTo(result, 4);
        return result;
    }

}
