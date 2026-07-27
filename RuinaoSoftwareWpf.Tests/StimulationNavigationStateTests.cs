using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class StimulationNavigationStateTests
{
    [Fact]
    public void NewSession_DefaultsToTypeSelection()
    {
        var state = new StimulationNavigationState();

        Assert.Equal(StimulationSubpage.TypeSelection, state.CurrentSubpage);
    }

    [Theory]
    [InlineData(StimulationSubpage.TemporalInterference)]
    [InlineData(StimulationSubpage.DirectCurrent)]
    [InlineData(StimulationSubpage.PulseCurrent)]
    [InlineData(StimulationSubpage.TypeSelection)]
    public void Remember_PreservesLastStimulationSubpage(StimulationSubpage subpage)
    {
        var state = new StimulationNavigationState();

        state.Remember(subpage);

        Assert.Equal(subpage, state.CurrentSubpage);
    }

    [Fact]
    public void Reset_ReturnsToTypeSelectionForNextLogin()
    {
        var state = new StimulationNavigationState();
        state.Remember(StimulationSubpage.DirectCurrent);

        state.Reset();

        Assert.Equal(StimulationSubpage.TypeSelection, state.CurrentSubpage);
    }
}
