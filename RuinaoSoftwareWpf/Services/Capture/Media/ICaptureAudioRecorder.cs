namespace RuinaoSoftwareWpf;

internal interface ICaptureAudioRecorder
{
    bool IsActive { get; }
    void Start(string audioPath);
    Task StopAsync(CaptureTimingState? timing = null);
}
