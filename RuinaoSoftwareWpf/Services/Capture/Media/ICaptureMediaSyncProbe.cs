namespace RuinaoSoftwareWpf;

internal interface ICaptureMediaSyncProbe
{
    Task<MediaSyncProbeResult> ProbeAsync(
        CaptureSessionInfo session,
        CaptureTimingState timing,
        int writtenFrameCount);
}
