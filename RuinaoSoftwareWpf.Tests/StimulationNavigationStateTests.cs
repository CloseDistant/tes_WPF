using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class StimulationNavigationStateTests
{
    [Fact]
    public void NewSession_DefaultsToTypeSelection()
    {
        var state = new StimulationNavigationState();

        Assert.True(state.IsTypeSelection);
        Assert.Null(state.CurrentModeCode);
    }

    [Theory]
    [InlineData(StimulationModeCodes.TemporalInterference)]
    [InlineData(StimulationModeCodes.DirectCurrent)]
    [InlineData(StimulationModeCodes.PulseCurrent)]
    [InlineData("future-mode")]
    public void RememberMode_PreservesAnyRegisteredOrFutureModeCode(string modeCode)
    {
        var state = new StimulationNavigationState();

        state.RememberMode(modeCode);

        Assert.False(state.IsTypeSelection);
        Assert.Equal(modeCode, state.CurrentModeCode);
    }

    [Fact]
    public void RememberTypeSelection_ReturnsToSelectionWithoutAddingAnEnumValue()
    {
        var state = new StimulationNavigationState();
        state.RememberMode(StimulationModeCodes.DirectCurrent);

        state.RememberTypeSelection();

        Assert.True(state.IsTypeSelection);
        Assert.Null(state.CurrentModeCode);
    }

    [Fact]
    public void Reset_ReturnsToTypeSelectionForNextLogin()
    {
        var state = new StimulationNavigationState();
        state.RememberMode(StimulationModeCodes.DirectCurrent);

        state.Reset();

        Assert.True(state.IsTypeSelection);
        Assert.Null(state.CurrentModeCode);
    }
}
