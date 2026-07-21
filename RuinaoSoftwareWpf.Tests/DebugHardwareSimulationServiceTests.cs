namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class DebugHardwareSimulationServiceTests
{
    [Fact]
    public void Connect_RespectsBuildConfigurationAndRealConnectionState()
    {
        var service = new DebugHardwareSimulationService();
        var changeCount = 0;
        service.ConnectionChanged += (_, _) => changeCount++;

#if DEBUG
        Assert.True(service.IsAvailable);
        Assert.False(service.Connect(realHardwareConnected: true).Succeeded);
        Assert.False(service.IsConnected);

        Assert.True(service.Connect(realHardwareConnected: false).Succeeded);
        Assert.True(service.IsConnected);
        Assert.Equal(1, changeCount);

        Assert.True(service.Connect(realHardwareConnected: false).Succeeded);
        Assert.Equal(1, changeCount);
#else
        Assert.False(service.IsAvailable);
        Assert.False(service.Connect(realHardwareConnected: false).Succeeded);
        Assert.False(service.IsConnected);
        Assert.Equal(0, changeCount);
#endif
    }
}
