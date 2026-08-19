namespace RuinaoSoftwareWpf;

internal enum CalibrationFrameKind
{
    StartCross,
    Point,
    EndCross
}

internal sealed record CalibrationFrame(
    CalibrationFrameKind Kind,
    int TrialIndex,
    int? PointIndex,
    int? Region,
    string Text,
    string MarkerColor,
    double X,
    double Y,
    TimeSpan Duration,
    TimeSpan MoveDuration);

internal sealed record CalibrationTrial(
    int PointCount,
    int FirstCrossMs,
    int NumberMs,
    int LastCrossMs,
    int[] LayoutValues,
    bool IsFixedLayout);

/// <summary>
/// 根据眼动校准文档生成四轮执行帧。
/// 固定轮使用文档九宫格；随机轮按文档上下区域生成本次实际坐标。
/// </summary>
internal sealed class EyeCalibrationSequenceFactory(Random random)
{
    internal const double MoveDurationRatio = 0.75d;
    internal const string CrossColor = "#969DA8";

    private static readonly string[] PointColors =
    [
        "#2196F3", "#6F58E2", "#2DCA69", "#B245E2", "#FFA219",
        "#1CBEC3", "#EF5252", "#30B2E5", "#E36D4C", "#8F6DE8"
    ];

    internal static IReadOnlyList<CalibrationTrial> Trials { get; } =
    [
        new(8, 1200, 1000, 800, [1, 5, 3, 7, 8, 4, 6, 2], true),
        new(10, 800, 1000, 800, [1, 2, 2, 1, 2, 2, 1, 1, 2, 1], false),
        new(8, 800, 1000, 800, [4, 6, 1, 8, 7, 2, 5, 3], true),
        new(10, 800, 1000, 1200, [2, 1, 1, 2, 2, 2, 1, 1, 1, 2], false)
    ];

    internal IReadOnlyList<CalibrationFrame> Create()
    {
        var frames = new List<CalibrationFrame>(44);
        for (var trialIndex = 0; trialIndex < Trials.Count; trialIndex++)
        {
            var trial = Trials[trialIndex];
            frames.Add(new(
                CalibrationFrameKind.StartCross,
                trialIndex + 1,
                null,
                null,
                "+",
                CrossColor,
                50,
                50,
                TimeSpan.FromMilliseconds(trial.FirstCrossMs),
                TimeSpan.Zero));

            for (var pointNumber = 1; pointNumber <= trial.PointCount; pointNumber++)
            {
                var region = trial.IsFixedLayout ? (int?)null : trial.LayoutValues[pointNumber - 1];
                var (x, y) = trial.IsFixedLayout
                    ? PositionForFixedPoint(pointNumber, trial.LayoutValues)
                    : PositionForRegionPoint(region!.Value);
                frames.Add(new(
                    CalibrationFrameKind.Point,
                    trialIndex + 1,
                    pointNumber,
                    region,
                    pointNumber.ToString(),
                    PointColors[(pointNumber - 1) % PointColors.Length],
                    x,
                    y,
                    TimeSpan.FromMilliseconds(trial.NumberMs),
                    TimeSpan.FromMilliseconds(trial.NumberMs * MoveDurationRatio)));
            }

            frames.Add(new(
                CalibrationFrameKind.EndCross,
                trialIndex + 1,
                null,
                null,
                "+",
                CrossColor,
                50,
                50,
                TimeSpan.FromMilliseconds(trial.LastCrossMs),
                TimeSpan.FromMilliseconds(trial.LastCrossMs * MoveDurationRatio)));
        }

        return frames;
    }

    private static (double X, double Y) PositionForFixedPoint(
        int pointNumber,
        int[] numberAtPositions)
    {
        var positions = new (double X, double Y)[]
        {
            (18, 18), (50, 18), (82, 18),
            (18, 50), (82, 50),
            (18, 82), (50, 82), (82, 82)
        };

        var positionIndex = Array.IndexOf(numberAtPositions, pointNumber);
        return positionIndex >= 0 && positionIndex < positions.Length
            ? positions[positionIndex]
            : (50, 50);
    }

    private (double X, double Y) PositionForRegionPoint(int region)
    {
        var x = 15d + random.NextDouble() * 70d;
        var y = region == 1
            ? 18d + random.NextDouble() * 24d
            : 58d + random.NextDouble() * 24d;
        return (x, y);
    }
}
