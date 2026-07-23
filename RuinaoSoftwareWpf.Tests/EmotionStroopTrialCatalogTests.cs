namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class EmotionStroopTrialCatalogTests
{
    [Fact]
    public void Trials_ShouldContainContinuousSixtyTrialSequence()
    {
        var trials = EmotionStroopTrialCatalog.Trials;

        Assert.Equal(60, trials.Count);
        Assert.Equal(Enumerable.Range(1, 60), trials.Select(trial => trial.TrialIndex));
        Assert.Equal(60, trials.Select(trial => trial.ImageFileName).Distinct().Count());
    }

    [Fact]
    public void Trials_ShouldRespectImageAndResponseRules()
    {
        foreach (var trial in EmotionStroopTrialCatalog.Trials)
        {
            Assert.EndsWith(".png", trial.ImageFileName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(trial.ImageType, new[] { 1, 2 });
            Assert.Contains(trial.WordType, new[] { 11, 20 });
            Assert.False(string.IsNullOrWhiteSpace(trial.WordText));
            Assert.Equal(
                trial.ImageType == 1 ? EmotionStroopResponse.Positive : EmotionStroopResponse.Negative,
                trial.CorrectResponse);
        }

        Assert.Equal(30, EmotionStroopTrialCatalog.Trials.Count(trial => trial.ImageType == 1));
        Assert.Equal(30, EmotionStroopTrialCatalog.Trials.Count(trial => trial.ImageType == 2));
    }

    [Fact]
    public void Trials_ShouldReferenceExistingBundledImages()
    {
        var projectDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RuinaoSoftwareWpf"));
        var assetDirectory = Path.Combine(projectDirectory, "Assets", "CaptureWorkbench", "EmotionStroop");

        foreach (var trial in EmotionStroopTrialCatalog.Trials)
        {
            Assert.True(
                File.Exists(Path.Combine(assetDirectory, trial.ImageFileName)),
                $"Missing emotion Stroop image: {trial.ImageFileName}");
        }
    }
}
