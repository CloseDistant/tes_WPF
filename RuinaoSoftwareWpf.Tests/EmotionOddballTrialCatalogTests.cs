namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class EmotionOddballTrialCatalogTests
{
    [Fact]
    public void Trials_ShouldContainContinuousSixtyFourTrialSequence()
    {
        var trials = EmotionOddballTrialCatalog.Trials;

        Assert.Equal(64, trials.Count);
        Assert.Equal(Enumerable.Range(1, 64), trials.Select(trial => trial.TrialIndex));
    }

    [Fact]
    public void Trials_ShouldUseValidImagesTypesAndResponseMappings()
    {
        foreach (var trial in EmotionOddballTrialCatalog.Trials)
        {
            Assert.EndsWith(".png", trial.ImageFileName, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(trial.ImageType, 1, 3);

            var expectedResponse = trial.Shape == EmotionOddballShape.Square
                ? EmotionOddballResponse.Square
                : EmotionOddballResponse.Circle;
            Assert.Equal(expectedResponse, trial.CorrectResponse);
        }
    }
}
