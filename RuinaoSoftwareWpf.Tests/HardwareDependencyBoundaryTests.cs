namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class HardwareDependencyBoundaryTests
{
    [Fact]
    public void WpfAssembly_DoesNotReferenceProtocolAssemblyDirectly()
    {
        var referencedAssemblyNames = typeof(MainViewModel)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("RuinaoTesProtocol", referencedAssemblyNames);
        Assert.Contains("RuinaoTesHardware", referencedAssemblyNames);
    }
}
