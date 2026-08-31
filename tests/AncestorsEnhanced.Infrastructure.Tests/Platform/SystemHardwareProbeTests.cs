using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.Infrastructure.Tests.Platform;

public sealed class SystemHardwareProbeTests
{
    [Fact]
    public void InspectReturnsALocalSnapshotWithoutThrowing()
    {
        HardwareSnapshot snapshot = new SystemHardwareProbe().Inspect();

        Assert.False(string.IsNullOrWhiteSpace(snapshot.CpuName));
        Assert.True(snapshot.LogicalProcessorCount > 0);
    }

    [Fact]
    public void DetailedInspectReturnsAUsableSnapshotWithoutTreatingSharedMemoryAsDedicated()
    {
        HardwareSnapshot snapshot = new SystemHardwareProbe().Inspect(includeDetailedGraphics: true);

        Assert.False(string.IsNullOrWhiteSpace(snapshot.CpuName));
        Assert.True(snapshot.LogicalProcessorCount > 0);
        Assert.All(snapshot.GraphicsAdapters.Where(adapter => adapter.IsMemoryAuthoritative), adapter =>
            Assert.NotNull(adapter.ReportedMemoryBytes));
    }

    [Fact]
    public void ParseDxDiagXmlUsesDedicatedMemoryBeforeDisplayMemory()
    {
        GraphicsAdapterSnapshot adapter = Assert.Single(SystemHardwareProbe.ParseDxDiagXml("""
            <DxDiag><DisplayDevices><DisplayDevice>
              <CardName>Example GPU</CardName>
              <DedicatedMemory>12012 MB</DedicatedMemory>
              <DisplayMemory>16000 MB</DisplayMemory>
            </DisplayDevice></DisplayDevices></DxDiag>
            """));

        Assert.Equal("Example GPU", adapter.Name);
        Assert.Equal(12012UL * 1024 * 1024, adapter.ReportedMemoryBytes);
        Assert.True(adapter.IsMemoryAuthoritative);
    }

    [Fact]
    public void ParseDxDiagDisplayMemoryIsInformationalAndAcceptsDecimalComma()
    {
        GraphicsAdapterSnapshot adapter = Assert.Single(SystemHardwareProbe.ParseDxDiagXml("""
            <DxDiag><DisplayDevices><DisplayDevice>
              <CardName>Integrated GPU</CardName>
              <DisplayMemory>8191,5 MB</DisplayMemory>
            </DisplayDevice></DisplayDevices></DxDiag>
            """));

        Assert.Equal((ulong)(8191.5 * 1024 * 1024), adapter.ReportedMemoryBytes);
        Assert.False(adapter.IsMemoryAuthoritative);
    }

    [Fact]
    public void ParseDxDiagXmlDoesNotInventMemoryWhenTheReportOmitsIt()
    {
        GraphicsAdapterSnapshot adapter = Assert.Single(SystemHardwareProbe.ParseDxDiagXml("""
            <DxDiag><DisplayDevices><DisplayDevice><CardName>Example GPU</CardName></DisplayDevice></DisplayDevices></DxDiag>
            """));

        Assert.Null(adapter.ReportedMemoryBytes);
    }

    [Fact]
    public void DetailedAdaptersUpgradeANameMatchedOrdinaryAdapterOnlyWithDedicatedMemory()
    {
        GraphicsAdapterSnapshot[] merged = SystemHardwareProbe.MergeDetailedGraphicsAdapters(
            [new GraphicsAdapterSnapshot("Example GPU", null, false)],
            [new GraphicsAdapterSnapshot("Example GPU", 8UL * 1024 * 1024 * 1024, true)]);

        GraphicsAdapterSnapshot adapter = Assert.Single(merged);
        Assert.Equal(8UL * 1024 * 1024 * 1024, adapter.ReportedMemoryBytes);
        Assert.True(adapter.IsMemoryAuthoritative);
    }

    [Fact]
    public void DetailedDisplayMemoryDoesNotOverrideAnOrdinaryAdapterAsAuthoritative()
    {
        GraphicsAdapterSnapshot[] merged = SystemHardwareProbe.MergeDetailedGraphicsAdapters(
            [new GraphicsAdapterSnapshot("Integrated GPU", null, false)],
            [new GraphicsAdapterSnapshot("Integrated GPU", 8UL * 1024 * 1024 * 1024, false)]);

        GraphicsAdapterSnapshot adapter = Assert.Single(merged);
        Assert.Null(adapter.ReportedMemoryBytes);
        Assert.False(adapter.IsMemoryAuthoritative);
    }
}
