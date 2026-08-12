namespace RuinaoSoftwareWpf;

internal interface ICaptureAudioRecorder
{
    bool IsActive { get; }
    void Start(string audioPath);
    void Stop(CaptureTimingState? timing = null);
}
