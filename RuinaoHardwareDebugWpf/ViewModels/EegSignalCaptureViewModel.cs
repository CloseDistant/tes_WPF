using System.Windows;
using System.Windows.Input;

namespace RuinaoHardwareDebugWpf;

using System.Text.Json;

public sealed class EegSignalCaptureViewModel : ObservableObject
{
    private readonly IEegAcquisitionService acquisitionService;
    private readonly IEegRecordingService recordingService;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IUserDialogService userDialogService;
    private readonly object markerWriteSync = new();
    private readonly AsyncRelayCommand startCommand;
    private readonly AsyncRelayCommand stopCommand;
    private EegAcquisitionState state;
    private EegWaveformRenderModel? renderModel;
    private TimeSpan elapsed;
    private IReadOnlyList<EegMarkerRecord> markers = Array.Empty<EegMarkerRecord>();
    private string recordName = $"EEG_{DateTime.Now:yyyyMMdd_HHmmss}";
    private Task markerWriteTask = Task.CompletedTask;
    private int persistedMarkerCount;
    private int backpressureNoticeShown;

    public EegSignalCaptureViewModel(
        IEegAcquisitionService acquisitionService,
        IEegRecordingService recordingService,
        IUnifiedSessionService unifiedSessionService,
        IUserDialogService userDialogService)
    {
        this.acquisitionService = acquisitionService;
        this.recordingService = recordingService;
        this.unifiedSessionService = unifiedSessionService;
        this.userDialogService = userDialogService;
        state = acquisitionService.State;

        startCommand = new AsyncRelayCommand(StartAsync, () => !IsRecording, HandleOperationError);
        stopCommand = new AsyncRelayCommand(StopAsync, () => IsRecording, HandleOperationError);
        StartCommand = startCommand;
        StopCommand = stopCommand;

        acquisitionService.StateChanged += (_, nextState) => OnUiThread(() =>
        {
            State = nextState;
            OnPropertyChanged(nameof(IsRecording));
            startCommand.RaiseCanExecuteChanged();
            stopCommand.RaiseCanExecuteChanged();
        });
        acquisitionService.RenderModelUpdated += (_, model) => OnUiThread(() =>
        {
            RenderModel = model;
            Elapsed = model.Elapsed;
            OnPropertyChanged(nameof(IsRecording));
            RenderModelUpdated?.Invoke(this, model);
        });
        acquisitionService.MarkersChanged += (_, nextMarkers) => OnUiThread(() =>
        {
            Markers = nextMarkers;
            PersistNewMarkers(nextMarkers);
            MarkersChanged?.Invoke(this, nextMarkers);
        });
        acquisitionService.SamplesGenerated += (_, batch) => EnqueueSampleWrite(batch);

        acquisitionService.Configure(new EegAcquisitionConfig());
    }

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    public IReadOnlyList<EegMarkerTag> MarkerTags => acquisitionService.MarkerTags;

    public event EventHandler<EegWaveformRenderModel>? RenderModelUpdated;

    public event EventHandler<IReadOnlyList<EegMarkerRecord>>? MarkersChanged;

    public EegAcquisitionState State
    {
        get => state;
        private set => SetProperty(ref state, value);
    }

    public EegWaveformRenderModel? RenderModel
    {
        get => renderModel;
        private set => SetProperty(ref renderModel, value);
    }

    public TimeSpan Elapsed
    {
        get => elapsed;
        private set
        {
            if (SetProperty(ref elapsed, value))
            {
                OnPropertyChanged(nameof(RecordingTime));
            }
        }
    }

    public IReadOnlyList<EegMarkerRecord> Markers
    {
        get => markers;
        private set => SetProperty(ref markers, value);
    }

    public string RecordName
    {
        get => recordName;
        set => SetProperty(ref recordName, value);
    }

    public bool IsRecording => State == EegAcquisitionState.Acquiring;

    public string RecordingTime => Elapsed.ToString(@"hh\:mm\:ss");

    public string DeviceStatus => "Mock 数据源";

    public string CaptureStatus => IsRecording ? "采集中" : "待采集";

    public string ChannelSummary => $"{acquisitionService.Config.ChannelCount} 通道";

    public string SampleRateSummary => $"{acquisitionService.Config.SampleRateHz} Hz";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(RecordName))
        {
            RecordName = $"EEG_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        persistedMarkerCount = 0;
        Volatile.Write(ref backpressureNoticeShown, 0);
        lock (markerWriteSync)
        {
            markerWriteTask = Task.CompletedTask;
        }

        await recordingService.StartAsync(RecordName, acquisitionService.Config, cancellationToken);
        try
        {
            await acquisitionService.StartAsync(RecordName, cancellationToken);
            await unifiedSessionService.RecordEventAsync(
                SessionModuleCodes.Eeg,
                "acquisition_started",
                RecordName,
                JsonSerializer.Serialize(new
                {
                    sampleIndex = 0,
                    acquisitionService.Config.SampleRateHz,
                    acquisitionService.Config.ChannelCount
                }),
                cancellationToken: cancellationToken);
        }
        catch
        {
            await recordingService.StopAsync("start_failed", CancellationToken.None);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await acquisitionService.StopAsync(cancellationToken);
        await unifiedSessionService.RecordEventAsync(
            SessionModuleCodes.Eeg,
            "acquisition_stopped",
            RecordName,
            JsonSerializer.Serialize(new
            {
                totalSamples = acquisitionService.GetCurrentRenderModel().TotalSamples,
                acquisitionService.Config.SampleRateHz
            }),
            cancellationToken: cancellationToken);
        Task pendingMarkers;
        lock (markerWriteSync)
        {
            pendingMarkers = markerWriteTask;
        }

        await pendingMarkers.WaitAsync(cancellationToken);
        await recordingService.StopAsync("completed", cancellationToken);
    }

    public void AddMarker(EegMarkerTag tag, string source)
    {
        acquisitionService.AddMarker(tag, source);
    }

    public void ReplaceMarkerTags(IReadOnlyList<EegMarkerTag> markerTags)
    {
        acquisitionService.ReplaceMarkerTags(markerTags);
        OnPropertyChanged(nameof(MarkerTags));
    }

    public void ApplyAcquisitionConfig(EegAcquisitionConfig config)
    {
        if (IsRecording)
        {
            userDialogService.ShowInformation("参数设置", "采集中不能修改参数设置，请先结束采集。");
            return;
        }

        acquisitionService.Configure(config);
        State = acquisitionService.State;
        OnPropertyChanged(nameof(SampleRateSummary));
        OnPropertyChanged(nameof(ChannelSummary));
        OnPropertyChanged(nameof(CaptureStatus));
    }

    public Task StopForNavigationAsync(CancellationToken cancellationToken = default)
    {
        return StopAsync(cancellationToken);
    }

    public EegWaveformRenderModel GetCurrentRenderModel()
    {
        return acquisitionService.GetCurrentRenderModel();
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private void EnqueueSampleWrite(EegSampleBatch batch)
    {
        if (!recordingService.TryAppendSamples(batch)
            && Interlocked.Exchange(ref backpressureNoticeShown, 1) == 0)
        {
            OnUiThread(() => userDialogService.ShowError(
                "EEG 采集",
                "EEG 写入队列已满，当前记录可能不完整。请停止采集并检查磁盘性能。"));
        }
    }

    private void PersistNewMarkers(IReadOnlyList<EegMarkerRecord> nextMarkers)
    {
        if (!recordingService.IsRecording || nextMarkers.Count <= persistedMarkerCount)
        {
            return;
        }

        for (var index = persistedMarkerCount; index < nextMarkers.Count; index++)
        {
            var marker = nextMarkers[index];
            lock (markerWriteSync)
            {
                markerWriteTask = RunSequentiallyAsync(markerWriteTask, () => recordingService.AddMarkerAsync(marker));
            }
        }

        persistedMarkerCount = nextMarkers.Count;
    }

    private static async Task RunSequentiallyAsync(Task previous, Func<Task> next)
    {
        await previous.ConfigureAwait(false);
        await next().ConfigureAwait(false);
    }

    private void HandleOperationError(Exception exception)
    {
        userDialogService.ShowError("EEG 采集", $"EEG 操作失败：{exception.Message}");
    }
}
