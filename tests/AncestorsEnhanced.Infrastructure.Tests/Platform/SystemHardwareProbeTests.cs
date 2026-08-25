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
}
