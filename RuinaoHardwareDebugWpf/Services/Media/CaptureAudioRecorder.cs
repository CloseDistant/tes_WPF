namespace RuinaoHardwareDebugWpf;

using NAudio.Wave;

internal interface ICaptureAudioRecorder
{
    bool IsActive { get; }
    void Start(string audioPath);
    void Stop(CaptureTimingState? timing = null);
}

internal sealed class CaptureAudioRecorder : ICaptureAudioRecorder
{
    private readonly object syncRoot = new();
    private readonly ILoggingService logger;
    private WaveInEvent? capture;
    private WaveFileWriter? writer;

    public CaptureAudioRecorder(ILoggingService logger)
    {
        this.logger = logger;
    }

    public bool IsActive
    {
        get { lock (syncRoot) { return capture is not null || writer is not null; } }
    }

    public void Start(string audioPath)
    {
        lock (syncRoot)
        {
            if (IsActive)
            {
                return;
            }

            try
            {
                capture = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(44100, 16, 1),
                    BufferMilliseconds = 100
                };
                writer = new WaveFileWriter(audioPath, capture.WaveFormat);
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();
                logger.Info($"音频录制已启动：audioPath={audioPath}");
            }
            catch
            {
                DisposeResources();
                throw;
            }
        }
    }

    public void Stop(CaptureTimingState? timing = null)
    {
        timing?.RecordAudioStopped(DateTimeOffset.Now);
        WaveInEvent? activeCapture;
        lock (syncRoot) { activeCapture = capture; }
        try
        {
            activeCapture?.StopRecording();
        }
        finally
        {
            DisposeResources();
        }

        logger.Info("音频录制已停止");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (syncRoot)
        {
            writer?.Write(args.Buffer, 0, args.BytesRecorded);
            writer?.Flush();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args) => DisposeResources();

    private void DisposeResources()
    {
        lock (syncRoot)
        {
            if (capture is not null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
            }

            writer?.Dispose();
            writer = null;
            capture?.Dispose();
            capture = null;
        }
    }
}
