namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class EmotionStroopTrialCatalogTests
{
    [Fact]
    public void ExcelCatalog_ShouldContainTwoEightyTrialVersionsAndSixteenPracticeTrials()
    {
        Assert.Equal(80, EmotionStroopTrialCatalog.VersionATrials.Count);
        Assert.Equal(80, EmotionStroopTrialCatalog.VersionBTrials.Count);
        Assert.Equal(16, EmotionStroopTrialCatalog.PracticeTrials.Count);
        Assert.Equal(Enumerable.Range(1, 80), EmotionStroopTrialCatalog.VersionATrials.Select(t => t.TrialIndex));
        Assert.Equal(Enumerable.Range(1, 80), EmotionStroopTrialCatalog.VersionBTrials.Select(t => t.TrialIndex));
    }

    [Fact]
    public void FormalVersions_ShouldRemainBalanced()
    {
        foreach (var trials in new[] { EmotionStroopTrialCatalog.VersionATrials, EmotionStroopTrialCatalog.VersionBTrials })
        {
            Assert.Equal(40, trials.Select(t => t.FaceId).Distinct().Count());
            Assert.Equal(40, trials.Count(t => t.FaceValence == 1));
            Assert.Equal(40, trials.Count(t => t.FaceValence == 2));
            Assert.Equal(20, trials.Count(t => t.Condition == "PP"));
            Assert.Equal(20, trials.Count(t => t.Condition == "PN"));
            Assert.Equal(20, trials.Count(t => t.Condition == "NP"));
            Assert.Equal(20, trials.Count(t => t.Condition == "NN"));
            Assert.Equal(2, trials.GroupBy(t => t.FaceId).Min(g => g.Count()));
            Assert.Equal(2, trials.GroupBy(t => t.WordId).Min(g => g.Count()));
        }
    }

    [Fact]
    public void Assets_ShouldExistInFormalAndPracticeFolders()
    {
        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RuinaoSoftwareWpf"));
        foreach (var trial in EmotionStroopTrialCatalog.VersionATrials)
            Assert.True(File.Exists(Path.Combine(projectDirectory, "Assets", "CaptureWorkbench", "EmotionStroop", "Formal", trial.ImageFileName)), trial.ImageFileName);
        foreach (var trial in EmotionStroopTrialCatalog.PracticeTrials)
            Assert.True(File.Exists(Path.Combine(projectDirectory, "Assets", "CaptureWorkbench", "EmotionStroop", "Practice", trial.ImageFileName)), trial.ImageFileName);
    }
}
