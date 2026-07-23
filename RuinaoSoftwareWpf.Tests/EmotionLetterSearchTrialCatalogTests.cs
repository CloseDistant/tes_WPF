namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class EmotionLetterSearchTrialCatalogTests
{
    [Fact]
    public void Trials_ShouldContainContinuousFortyEightTrialSequence()
    {
        var trials = EmotionLetterSearchTrialCatalog.Trials;

        Assert.Equal(48, trials.Count);
        Assert.Equal(Enumerable.Range(1, 48), trials.Select(trial => trial.TrialIndex));
    }

    [Fact]
    public void Trials_ShouldRespectBusinessClassificationAndResponseRules()
    {
        foreach (var trial in EmotionLetterSearchTrialCatalog.Trials)
        {
            Assert.EndsWith(".png", trial.ImageFileName, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(trial.ImageType, 1, 3);
            Assert.InRange(trial.LetterPosition, 1, 6);
            Assert.Contains(trial.LoadCategory, new[] { 5, 6 });

            var containsX = trial.Letters.Contains('X');
            var containsN = trial.Letters.Contains('N');
            Assert.NotEqual(containsX, containsN);
            Assert.Equal(containsX ? 3 : 4, trial.LetterType);
            Assert.Equal(
                containsX ? EmotionLetterSearchResponse.ContainsX : EmotionLetterSearchResponse.ContainsN,
                trial.CorrectResponse);
        }
    }

    [Fact]
    public void Trials_ShouldReferenceExistingBundledImages()
    {
        var projectDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RuinaoSoftwareWpf"));
        var assetDirectory = Path.Combine(projectDirectory, "Assets", "CaptureWorkbench", "EmotionLetterSearch");

        foreach (var trial in EmotionLetterSearchTrialCatalog.Trials)
        {
            Assert.True(
                File.Exists(Path.Combine(assetDirectory, trial.ImageFileName)),
                $"Missing emotion letter search image: {trial.ImageFileName}");
        }
    }
}
