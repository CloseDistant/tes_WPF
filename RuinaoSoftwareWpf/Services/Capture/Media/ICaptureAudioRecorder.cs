namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

internal interface ICaptureAudioRecorder
{
    event EventHandler<CaptureAudioLevel>? LevelAvailable;

    bool IsActive { get; }
    void Start(string audioPath);
    Task StopAsync(CaptureTimingState? timing = null);
}
