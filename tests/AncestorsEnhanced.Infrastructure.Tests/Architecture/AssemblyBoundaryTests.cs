using AncestorsEnhanced.Infrastructure;

namespace AncestorsEnhanced.Infrastructure.Tests.Architecture;

public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void InfrastructureAssemblyHasExpectedName()
    {
        string? assemblyName = typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name;

        Assert.Equal("AncestorsEnhanced.Infrastructure", assemblyName);
    }
}
