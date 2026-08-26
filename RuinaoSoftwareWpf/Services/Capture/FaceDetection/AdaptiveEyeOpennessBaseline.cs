namespace RuinaoSoftwareWpf;

/// <summary>
/// 在每次摄像头会话内根据用户睁眼状态建立个人基线，避免使用单一固定阈值误判。
/// 未形成基线前使用保守阈值；闭眼、遮挡和大角度帧不会污染基线。
/// </summary>
internal sealed class AdaptiveEyeOpennessBaseline
{
    private const int MinimumSamples = 8;
    private const int MaximumSamples = 30;
    private const double BootstrapClosedThreshold = 0.10;
    private const double BaselineThresholdRatio = 0.55;
    private readonly Queue<double> samples = new(MaximumSamples);

    public double? OpenEyeBaseline { get; private set; }

    public double ClosedEyeThreshold => OpenEyeBaseline.HasValue
        ? Math.Clamp(OpenEyeBaseline.Value * BaselineThresholdRatio, 0.07, 0.16)
        : BootstrapClosedThreshold;

    public void Reset()
    {
        samples.Clear();
        OpenEyeBaseline = null;
    }

    public void Observe(FaceQualityEvaluation evaluation)
    {
        if (evaluation.State is not CameraFaceState.Normal
            || evaluation.LeftEyeAspectRatio <= ClosedEyeThreshold * 1.10
            || evaluation.RightEyeAspectRatio <= ClosedEyeThreshold * 1.10)
        {
            return;
        }

        var openness = (evaluation.LeftEyeAspectRatio + evaluation.RightEyeAspectRatio) / 2d;
        if (!double.IsFinite(openness) || openness is < 0.05 or > 0.80)
        {
            return;
        }

        samples.Enqueue(openness);
        while (samples.Count > MaximumSamples)
        {
            samples.Dequeue();
        }

        if (samples.Count < MinimumSamples)
        {
            return;
        }

        var ordered = samples.OrderBy(static value => value).ToArray();
        var upperHalf = ordered.Skip(ordered.Length / 2).ToArray();
        OpenEyeBaseline = upperHalf.Average();
    }
}
