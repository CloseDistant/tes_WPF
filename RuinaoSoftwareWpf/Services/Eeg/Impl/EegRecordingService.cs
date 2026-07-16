namespace RuinaoSoftwareWpf;

using System.IO;
using System.Text.Json;

public sealed class EegRecordingService : IEegRecordingService
{
    private const int SegmentSeconds = 60;

    private readonly ICaptureRecordingRepository captureRepository;
    private readonly IEegRecordingRepository eegRepository;
    private readonly IEegSegmentFileWriter segmentFileWriter;
    private readonly ILoggingService logger;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IEegWritePipeline writePipeline;
    private readonly IRunConfigurationSnapshotService configurationSnapshots;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly List<EegMarkerRecord> pendingMarkers = new();
    private EegRecordingInfo? currentRecording;
    private long totalSamples;
    private bool recoveryCompleted;

    public EegRecordingService(
        ICaptureRecordingRepository captureRepository,
        IEegRecordingRepository eegRepository,
        IEegSegmentFileWriter segmentFileWriter,
        ILoggingService logger,
        IUnifiedSessionService unifiedSessionService,
        IEegWritePipeline writePipeline,
        IRunConfigurationSnapshotService configurationSnapshots)
    {
        this.captureRepository = captureRepository;
        this.eegRepository = eegRepository;
        this.segmentFileWriter = segmentFileWriter;
        this.logger = logger;
        this.unifiedSessionService = unifiedSessionService;
        this.writePipeline = writePipeline;
        this.configurationSnapshots = configurationSnapshots;
    }

    public bool IsRecording => currentRecording is not null;

    public async Task StartAsync(string recordName, EegAcquisitionConfig config, CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (!recoveryCompleted)
            {
                var recovered = await eegRepository.RecoverIncompleteEegRecordingsAsync(cancellationToken);
                if (recovered > 0)
                {
                    logger.Warning($"已恢复 {recovered} 条上次未正常结束的 EEG 记录，并标记为 interrupted。");
                }

                recoveryCompleted = true;
            }

            if (currentRecording is not null)
            {
                return;
            }

            pendingMarkers.Clear();
            totalSamples = 0;

            var unifiedSession = await unifiedSessionService.GetOrStartAsync(cancellationToken);
            config = config with { ChannelNames = config.ChannelNames.ToArray() };
            configurationSnapshots.Capture(unifiedSession.SessionKey, SessionModuleCodes.Eeg, config);
            var sessionTimestamp = unifiedSessionService.GetCurrentTimestamp();
            var now = DateTimeOffset.FromUnixTimeMilliseconds(sessionTimestamp.EventTimeUnixMs);
            var sessionKey = unifiedSession.SessionKey;
            var outputRoot = Path.Combine(CaptureOutputPathProvider.GetOutputRoot(), sessionKey);
            var eegOutputDirectory = Path.Combine(outputRoot, "EEG");
            Directory.CreateDirectory(eegOutputDirectory);
            Directory.CreateDirectory(Path.Combine(eegOutputDirectory, "exports"));

            var placeholderPath = Path.Combine(eegOutputDirectory, "recording.eegref");
            var captureSession = await captureRepository.CreateModuleSessionAsync(
                outputRoot,
                sessionKey,
                "EEG",
                "脑电信号采集",
                string.Empty,
                placeholderPath,
                string.Empty,
                string.Empty,
                string.Empty,
                cancellationToken);

            currentRecording = await eegRepository.CreateEegRecordingAsync(
                captureSession,
                recordName,
                config,
                eegOutputDirectory,
                SegmentSeconds,
                cancellationToken);

            await WriteRecordingSnapshotAsync(currentRecording, config, cancellationToken);
            segmentFileWriter.Start(
                eegOutputDirectory,
                currentRecording.Id,
                config,
                SegmentSeconds,
                now.ToUnixTimeMilliseconds());
            writePipeline.Start(ProcessSampleBatchAsync);
            await unifiedSessionService.RecordEventAsync(
                SessionModuleCodes.Eeg,
                "recording_started",
                recordName,
                JsonSerializer.Serialize(new
                {
                    recordingId = currentRecording.Id,
                    recordName,
                    config.ChannelCount,
                    config.SampleRateHz
                }),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.Error("EEG 采集记录初始化失败", ex);
            await TryRecordTimelineEventAsync("recording_start_failed", ex.Message);
            if (currentRecording is { } failedRecording)
            {
                await TryMarkRecordingFailedAsync(failedRecording, "start_failed", ex.Message);
                currentRecording = null;
            }

            throw;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task AppendSamplesAsync(EegSampleBatch batch, CancellationToken cancellationToken = default)
    {
        if (!TryAppendSamples(batch))
        {
            throw new InvalidOperationException("EEG 写入队列已满或采集已停止，当前批次未被接收。");
        }

        await Task.CompletedTask;
    }

    public bool TryAppendSamples(EegSampleBatch batch)
    {
        return currentRecording is not null && writePipeline.TryEnqueue(batch);
    }

    public async Task AddMarkerAsync(EegMarkerRecord marker, CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (currentRecording is null)
            {
                return;
            }

            pendingMarkers.Add(marker);
            await unifiedSessionService.RecordEventAsync(
                SessionModuleCodes.Eeg,
                "marker",
                marker.Name,
                JsonSerializer.Serialize(new
                {
                    marker.Name,
                    marker.Shortcut,
                    marker.SampleIndex,
                    marker.PageIndex,
                    marker.PageSampleIndex,
                    marker.Source
                }),
                DateTimeOffset.FromUnixTimeMilliseconds(marker.AbsoluteTimestampMs),
                cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task StopAsync(string status = "completed", CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (currentRecording is null)
            {
                return;
            }

            var recording = currentRecording;
            await writePipeline.CompleteAsync(cancellationToken);
            var closedSegments = await segmentFileWriter.StopAsync(cancellationToken);
            foreach (var segment in closedSegments.Where(item => item.SampleCount > 0))
            {
                await eegRepository.AddEegDataSegmentAsync(recording, segment, cancellationToken);
            }

            await WriteMarkerSnapshotAsync(recording, pendingMarkers, cancellationToken);
            await eegRepository.AddEegMarkersAsync(recording, pendingMarkers.ToArray(), cancellationToken);
            await eegRepository.CompleteEegRecordingAsync(recording, totalSamples, status, cancellationToken);
            await captureRepository.CompleteSessionAsync(recording.CaptureSession, status, $"EEG 采集完成，样本数：{totalSamples}", cancellationToken);
            await unifiedSessionService.RecordEventAsync(
                SessionModuleCodes.Eeg,
                "recording_stopped",
                status,
                JsonSerializer.Serialize(new { recordingId = recording.Id, totalSamples, status }),
                cancellationToken: cancellationToken);
            pendingMarkers.Clear();
            currentRecording = null;
            configurationSnapshots.Clear(SessionModuleCodes.Eeg);
        }
        catch (Exception ex)
        {
            logger.Error("EEG 采集停止写入失败", ex);
            await TryRecordTimelineEventAsync("recording_stop_failed", ex.Message);
            if (currentRecording is { } failedRecording)
            {
                await TryMarkRecordingFailedAsync(failedRecording, "finalize_failed", ex.Message);
                pendingMarkers.Clear();
                currentRecording = null;
            }

            throw;
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task ProcessSampleBatchAsync(EegSampleBatch batch, CancellationToken cancellationToken)
    {
        var recording = currentRecording ?? throw new InvalidOperationException("EEG 记录已结束，不能继续写入样本。");
        var closedSegments = await segmentFileWriter.AppendBatchAsync(batch, cancellationToken);
        foreach (var segment in closedSegments)
        {
            await eegRepository.AddEegDataSegmentAsync(recording, segment, cancellationToken);
        }

        InterlockedExtensions.Max(ref totalSamples, batch.StartSampleIndex + batch.SampleCount);
    }

    private async Task TryRecordTimelineEventAsync(string eventType, string message)
    {
        try
        {
            if (unifiedSessionService.CurrentSession is not null)
            {
                await unifiedSessionService.RecordEventAsync(
                    SessionModuleCodes.Eeg,
                    eventType,
                    message,
                    cancellationToken: CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            logger.Error($"EEG 统一时间轴事件写入失败：eventType={eventType}", exception);
        }
    }

    private async Task TryMarkRecordingFailedAsync(EegRecordingInfo recording, string status, string message)
    {
        try
        {
            await eegRepository.CompleteEegRecordingAsync(recording, totalSamples, status, CancellationToken.None);
            await captureRepository.CompleteSessionAsync(recording.CaptureSession, status, message, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.Error($"EEG 失败状态写入数据库失败：recordingId={recording.Id}", exception);
        }
    }

    private static async Task WriteRecordingSnapshotAsync(EegRecordingInfo recording, EegAcquisitionConfig config, CancellationToken cancellationToken)
    {
        var snapshotPath = Path.Combine(recording.OutputDirectory, "recording.json");
        var snapshot = new
        {
            recording.Id,
            recording.RecordName,
            recording.SegmentSeconds,
            config.ChannelCount,
            config.SampleRateHz,
            config.PageSeconds,
            config.ChannelNames,
            CreatedAt = DateTimeOffset.Now
        };
        await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WriteMarkerSnapshotAsync(EegRecordingInfo recording, IReadOnlyList<EegMarkerRecord> markers, CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(recording.OutputDirectory, "markers.json");
        var snapshot = markers.Select(marker => new
        {
            marker.Name,
            marker.Shortcut,
            Color = marker.Color.ToString(),
            marker.AbsoluteTimestampMs,
            ExperimentElapsedMs = (long)marker.ExperimentTime.TotalMilliseconds,
            marker.PageIndex,
            marker.PageSampleIndex,
            marker.SampleIndex,
            marker.Source
        });
        await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
