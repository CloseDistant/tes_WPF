namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class SystemSleepInhibitorTests
{
    [Fact]
    public void ActiveExecutionState_RequiresSystemAndDisplayContinuously()
    {
        var state = SystemSleepInhibitor.ActiveExecutionState;

        Assert.Equal(0x80000003u, (uint)state);
        Assert.True(state.HasFlag(SystemSleepInhibitor.ExecutionState.Continuous));
        Assert.True(state.HasFlag(SystemSleepInhibitor.ExecutionState.SystemRequired));
        Assert.True(state.HasFlag(SystemSleepInhibitor.ExecutionState.DisplayRequired));
    }

    [Fact]
    public void ResetExecutionState_ClearsSystemAndDisplayRequirements()
    {
        var state = SystemSleepInhibitor.ResetExecutionState;

        Assert.Equal(0x80000000u, (uint)state);
        Assert.True(state.HasFlag(SystemSleepInhibitor.ExecutionState.Continuous));
        Assert.False(state.HasFlag(SystemSleepInhibitor.ExecutionState.SystemRequired));
        Assert.False(state.HasFlag(SystemSleepInhibitor.ExecutionState.DisplayRequired));
    }
}
