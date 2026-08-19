using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class EyeCalibrationSequenceFactoryTests
{
    [Fact]
    public void Create_UsesDocumentedFourTrialTimingAndPointCounts()
    {
        var frames = new EyeCalibrationSequenceFactory(new Random(20260818)).Create();

        Assert.Equal(44, frames.Count);
        var expectedPointCounts = new[] { 8, 10, 8, 10 };
        var expectedFirstCrossMs = new[] { 1200, 800, 800, 800 };
        var expectedLastCrossMs = new[] { 800, 800, 800, 1200 };

        for (var trialIndex = 1; trialIndex <= 4; trialIndex++)
        {
            var trialFrames = frames.Where(frame => frame.TrialIndex == trialIndex).ToArray();
            var startCross = Assert.Single(trialFrames, frame => frame.Kind == CalibrationFrameKind.StartCross);
            var endCross = Assert.Single(trialFrames, frame => frame.Kind == CalibrationFrameKind.EndCross);
            var points = trialFrames.Where(frame => frame.Kind == CalibrationFrameKind.Point).ToArray();

            Assert.Equal(expectedPointCounts[trialIndex - 1], points.Length);
            Assert.Equal(expectedFirstCrossMs[trialIndex - 1], startCross.Duration.TotalMilliseconds);
            Assert.Equal(expectedLastCrossMs[trialIndex - 1], endCross.Duration.TotalMilliseconds);
            Assert.Equal(0, startCross.MoveDuration.TotalMilliseconds);
            Assert.Equal(
                expectedLastCrossMs[trialIndex - 1] * EyeCalibrationSequenceFactory.MoveDurationRatio,
                endCross.MoveDuration.TotalMilliseconds);
            Assert.All(points, point =>
            {
                Assert.Equal(1000, point.Duration.TotalMilliseconds);
                Assert.Equal(750, point.MoveDuration.TotalMilliseconds);
            });
        }
    }

    [Fact]
    public void Create_UsesDocumentedFixedLayouts()
    {
        var frames = new EyeCalibrationSequenceFactory(new Random(1)).Create();

        AssertFixedTrial(
            frames,
            1,
            new Dictionary<int, (double X, double Y)>
            {
                [1] = (18, 18), [2] = (82, 82), [3] = (82, 18), [4] = (18, 82),
                [5] = (50, 18), [6] = (50, 82), [7] = (18, 50), [8] = (82, 50)
            });
        AssertFixedTrial(
            frames,
            3,
            new Dictionary<int, (double X, double Y)>
            {
                [1] = (82, 18), [2] = (18, 82), [3] = (82, 82), [4] = (18, 18),
                [5] = (50, 82), [6] = (50, 18), [7] = (82, 50), [8] = (18, 50)
            });
    }

    [Fact]
    public void Create_GeneratesRandomTrialCoordinatesInsideDocumentedRegions()
    {
        var frames = new EyeCalibrationSequenceFactory(new Random(42)).Create();
        var expectedRegions = new Dictionary<int, int[]>
        {
            [2] = [1, 2, 2, 1, 2, 2, 1, 1, 2, 1],
            [4] = [2, 1, 1, 2, 2, 2, 1, 1, 1, 2]
        };

        foreach (var (trialIndex, regions) in expectedRegions)
        {
            var points = frames
                .Where(frame => frame.TrialIndex == trialIndex && frame.Kind == CalibrationFrameKind.Point)
                .OrderBy(frame => frame.PointIndex)
                .ToArray();

            Assert.Equal(regions.Length, points.Length);
            for (var index = 0; index < points.Length; index++)
            {
                var point = points[index];
                Assert.Equal(regions[index], point.Region);
                Assert.InRange(point.X, 15d, 85d);
                if (point.Region == 1)
                {
                    Assert.InRange(point.Y, 18d, 42d);
                }
                else
                {
                    Assert.InRange(point.Y, 58d, 82d);
                }
            }
        }
    }

    private static void AssertFixedTrial(
        IReadOnlyList<CalibrationFrame> frames,
        int trialIndex,
        IReadOnlyDictionary<int, (double X, double Y)> expectedPositions)
    {
        var points = frames
            .Where(frame => frame.TrialIndex == trialIndex && frame.Kind == CalibrationFrameKind.Point)
            .ToArray();

        Assert.Equal(expectedPositions.Count, points.Length);
        foreach (var point in points)
        {
            Assert.Null(point.Region);
            var expected = expectedPositions[point.PointIndex!.Value];
            Assert.Equal(expected.X, point.X);
            Assert.Equal(expected.Y, point.Y);
        }
    }
}
