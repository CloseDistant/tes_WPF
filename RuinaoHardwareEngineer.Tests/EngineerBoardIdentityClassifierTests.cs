using RuinaoHardwareEngineer.Features.DeviceTopology;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class EngineerBoardIdentityClassifierTests
{
    [Fact]
    public void Classify_WhenIdentityContainsTes_ReturnsStimulationBoard()
    {
        var result = EngineerBoardIdentityClassifier.Classify([0x74455300, 0, 0, 0]);

        Assert.Equal(EngineerBoardKind.Stimulation, result.Kind);
        Assert.Contains("tES", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_WhenIdentityContainsEeg_ReturnsEegBoard()
    {
        var result = EngineerBoardIdentityClassifier.Classify([0x45454700, 0, 0, 0]);

        Assert.Equal(EngineerBoardKind.Eeg, result.Kind);
        Assert.Equal("EEG", result.Text);
    }

    [Fact]
    public void Classify_WhenIdentityIsNumeric_DoesNotGuessBoardType()
    {
        var result = EngineerBoardIdentityClassifier.Classify([1, 2, 3, 4]);

        Assert.Equal(EngineerBoardKind.Unknown, result.Kind);
    }
}
