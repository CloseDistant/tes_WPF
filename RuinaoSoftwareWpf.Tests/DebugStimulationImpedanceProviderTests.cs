namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class DebugStimulationImpedanceProviderTests
{
    [Fact]
    public void GetSnapshot_BeforeSimulationConnect_ReturnsNull()
    {
        var provider = new DebugStimulationImpedanceProvider(
            new DebugHardwareSimulationService());

        Assert.Null(provider.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_DuringSimulationConnect_ReturnsStableNormalValues()
    {
        var simulation = new DebugHardwareSimulationService();
        var provider = new DebugStimulationImpedanceProvider(simulation);

#if DEBUG
        Assert.True(simulation.Connect(realHardwareConnected: false).Succeeded);

        var first = Assert.IsType<StimulationImpedanceSnapshot>(provider.GetSnapshot());
        var second = Assert.IsType<StimulationImpedanceSnapshot>(provider.GetSnapshot());

        Assert.Equal(16, first.Channels.Count);
        Assert.Equal(500m, first.Channels[0].ImpedanceOhms);
        Assert.Equal(800m, first.Channels[15].ImpedanceOhms);
        Assert.Equal(
            first.Channels.Select(channel => channel.ImpedanceOhms),
            second.Channels.Select(channel => channel.ImpedanceOhms));
#else
        Assert.False(simulation.Connect(realHardwareConnected: false).Succeeded);
        Assert.Null(provider.GetSnapshot());
#endif
    }
}
