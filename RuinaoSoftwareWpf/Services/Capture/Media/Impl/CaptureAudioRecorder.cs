namespace RuinaoSoftwareWpf;

using NAudio.Wave;

internal sealed class CaptureAudioRecorder : ICaptureAudioRecorder
{
    private readonly object syncRoot = new();
    private readonly ILoggingService logger;
    private WaveInEvent? capture;
    private WaveFileWriter? writer;
    private TaskCompletionSource<bool>? recordingStoppedSignal;

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
                recordingStoppedSignal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
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

    public async Task StopAsync(CaptureTimingState? timing = null)
    {
        timing?.RecordAudioStopped(DateTimeOffset.Now);
        WaveInEvent? activeCapture;
        TaskCompletionSource<bool>? stoppedSignal;
        lock (syncRoot)
        {
            activeCapture = capture;
            stoppedSignal = recordingStoppedSignal;
        }

        try
        {
            activeCapture?.StopRecording();
            if (activeCapture is not null
                && stoppedSignal is not null
                && !await WaitForRecordingStoppedAsync(stoppedSignal.Task).ConfigureAwait(false))
            {
                logger.Warning("等待麦克风停止回调超时，将强制释放录音资源。");
            }
        }
        finally
        {
            DisposeResources();
        }

        logger.Info("音频录制已停止");
    }

    private static async Task<bool> WaitForRecordingStoppedAsync(Task stoppedTask)
    {
        try
        {
            await stoppedTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (syncRoot)
        {
            writer?.Write(args.Buffer, 0, args.BytesRecorded);
            writer?.Flush();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        TaskCompletionSource<bool>? stoppedSignal;
        lock (syncRoot)
        {
            stoppedSignal = recordingStoppedSignal;
        }

        DisposeResources();
        stoppedSignal?.TrySetResult(true);
    }

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
            recordingStoppedSignal = null;
        }
    }
}
