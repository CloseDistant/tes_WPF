using System.Diagnostics;
using System.Windows.Media;

namespace RuinaoHardwareDebugWpf;

public sealed class MockEegAcquisitionService : IEegAcquisitionService, IDisposable
{
    private readonly object syncRoot = new();
    private readonly Random random = new();
    private readonly List<EegMarkerRecord> markerRecords = new();
    private readonly List<EegMarkerTag> markerTags = new();
    private Timer? renderTimer;
    private CancellationTokenSource? generationCts;
    private Task? generationTask;
    private EegAcquisitionConfig config = new();
    private double[][] pageSamples;
    private Stopwatch recordingClock = new();
    private int pageIndex;
    private int pageSampleIndex;
    private long totalSamples;
    private double pageGain = 1.0;
    private double pageNoiseScale = 7.0;
    private double pageSlowPhase;
    private int pageBurstStartSample;
    private int pageBurstDurationSamples;
    private int pageBurstChannelOffset;
    private bool pageHasBurst;
    private bool disposed;

    public MockEegAcquisitionService()
    {
        pageSamples = CreatePageBuffer(config);
        markerTags.AddRange(
        [
            new EegMarkerTag("刺激", "F8", Color.FromRgb(181, 61, 63)),
            new EegMarkerTag("发作", "F9", Color.FromRgb(174, 128, 45)),
            new EegMarkerTag("运动", "F10", Color.FromRgb(61, 156, 85)),
            new EegMarkerTag("睁眼", "F11", Color.FromRgb(92, 100, 118)),
            new EegMarkerTag("闭眼", "F12", Color.FromRgb(155, 105, 45))
        ]);
    }

    public EegAcquisitionState State { get; private set; } = EegAcquisitionState.Ready;

    public EegAcquisitionConfig Config => config;

    public IReadOnlyList<EegMarkerTag> MarkerTags
    {
        get
        {
            lock (syncRoot)
            {
                return markerTags.ToArray();
            }
        }
    }

    public event EventHandler<EegAcquisitionState>? StateChanged;

    public event EventHandler<EegWaveformRenderModel>? RenderModelUpdated;

    public event EventHandler<IReadOnlyList<EegMarkerRecord>>? MarkersChanged;

    public event EventHandler<EegSampleBatch>? SamplesGenerated;

    public void Configure(EegAcquisitionConfig nextConfig)
    {
        lock (syncRoot)
        {
            config = nextConfig;
            pageSamples = CreatePageBuffer(config);
            ResetLocked();
            State = EegAcquisitionState.Ready;
        }

        StateChanged?.Invoke(this, State);
        RenderModelUpdated?.Invoke(this, GetCurrentRenderModel());
    }

    public Task StartAsync(string recordName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            ResetLocked();
            recordingClock = Stopwatch.StartNew();
            State = EegAcquisitionState.Acquiring;
            renderTimer ??= new Timer(OnRenderTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            renderTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(66));
            generationCts = new CancellationTokenSource();
            generationTask = Task.Run(() => RunGenerationLoopAsync(generationCts.Token));
        }

        StateChanged?.Invoke(this, State);
        MarkersChanged?.Invoke(this, GetMarkers());
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        EegSampleBatch? batch = null;
        var cts = generationCts;
        var task = generationTask;
        cts?.Cancel();
        lock (syncRoot)
        {
            if (State != EegAcquisitionState.Acquiring)
            {
                return;
            }

            batch = AppendMissingSamplesLocked(recordingClock.Elapsed);
            recordingClock.Stop();
            State = EegAcquisitionState.Stopped;
            renderTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        try
        {
            if (task is not null)
            {
                await task.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(item => item is OperationCanceledException))
        {
        }

        cts?.Dispose();
        if (ReferenceEquals(generationCts, cts))
        {
            generationCts = null;
            generationTask = null;
        }

        if (batch is not null)
        {
            SamplesGenerated?.Invoke(this, batch);
        }

        StateChanged?.Invoke(this, State);
        RenderModelUpdated?.Invoke(this, GetCurrentRenderModel());
    }

    public void AddMarker(EegMarkerTag tag, string source)
    {
        EegMarkerRecord? record = null;
        lock (syncRoot)
        {
            if (State != EegAcquisitionState.Acquiring)
            {
                return;
            }

            record = new EegMarkerRecord(
                tag.Name,
                tag.KeyText,
                tag.Color,
                DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                recordingClock.Elapsed,
                pageIndex,
                pageSampleIndex,
                totalSamples,
                source);
            markerRecords.Add(record);
        }

        if (record is not null)
        {
            MarkersChanged?.Invoke(this, GetMarkers());
            RenderModelUpdated?.Invoke(this, GetCurrentRenderModel());
        }
    }

    public void ReplaceMarkerTags(IReadOnlyList<EegMarkerTag> nextMarkerTags)
    {
        lock (syncRoot)
        {
            markerTags.Clear();
            markerTags.AddRange(nextMarkerTags);
        }
    }

    public IReadOnlyList<EegMarkerRecord> GetMarkers()
    {
        lock (syncRoot)
        {
            return markerRecords.ToArray();
        }
    }

    public EegWaveformRenderModel GetCurrentRenderModel()
    {
        lock (syncRoot)
        {
            var samples = pageSamples.ToArray();
            var markers = markerRecords.ToArray();
            return new EegWaveformRenderModel(
                config,
                samples,
                pageIndex,
                pageSampleIndex,
                totalSamples,
                recordingClock.Elapsed,
                State == EegAcquisitionState.Acquiring,
                markers.Where(marker => marker.PageIndex == pageIndex).ToArray(),
                markers);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        generationCts?.Cancel();
        generationCts?.Dispose();
        renderTimer?.Dispose();
    }

    private static double[][] CreatePageBuffer(EegAcquisitionConfig config)
    {
        var samples = new double[config.ChannelCount][];
        for (var channel = 0; channel < config.ChannelCount; channel++)
        {
            samples[channel] = new double[config.PageSampleCount];
        }

        return samples;
    }

    private void OnRenderTimerTick(object? state)
    {
        if (State != EegAcquisitionState.Acquiring)
        {
            return;
        }

        RenderModelUpdated?.Invoke(this, GetCurrentRenderModel());
    }

    private async Task RunGenerationLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (State != EegAcquisitionState.Acquiring)
            {
                continue;
            }

            GenerateMissingSamples();
        }
    }

    private void GenerateMissingSamples()
    {
        EegSampleBatch? batch;
        lock (syncRoot)
        {
            if (State != EegAcquisitionState.Acquiring)
            {
                return;
            }

            batch = AppendMissingSamplesLocked(recordingClock.Elapsed);
        }

        if (batch is not null)
        {
            SamplesGenerated?.Invoke(this, batch);
        }
    }

    private void ResetLocked()
    {
        pageIndex = 0;
        pageSampleIndex = 0;
        totalSamples = 0;
        markerRecords.Clear();
        RefreshPageProfileLocked();
        foreach (var channel in pageSamples)
        {
            Array.Clear(channel);
        }
    }

    private EegSampleBatch? AppendMissingSamplesLocked(TimeSpan elapsed)
    {
        var targetTotalSamples = Math.Max(totalSamples, (long)(elapsed.TotalSeconds * config.SampleRateHz));
        var samplesToAppend = (int)Math.Min(int.MaxValue, targetTotalSamples - totalSamples);
        if (samplesToAppend <= 0)
        {
            return null;
        }

        var batchStartSampleIndex = totalSamples;
        var batchSamples = new double[config.ChannelCount][];
        for (var channel = 0; channel < config.ChannelCount; channel++)
        {
            batchSamples[channel] = new double[samplesToAppend];
        }

        var batchSampleIndex = 0;
        while (totalSamples < targetTotalSamples)
        {
            AppendMockSampleLocked(batchSamples, batchSampleIndex);
            batchSampleIndex++;
        }

        return new EegSampleBatch(batchSamples, batchStartSampleIndex, samplesToAppend, DateTimeOffset.Now);
    }

    private void AppendMockSampleLocked(double[][] batchSamples, int batchSampleIndex)
    {
        if (pageSampleIndex >= config.PageSampleCount)
        {
            pageIndex++;
            pageSampleIndex = 0;
            RefreshPageProfileLocked();
        }

        var t = totalSamples / (double)config.SampleRateHz;
        var pageT = pageSampleIndex / (double)config.SampleRateHz;
        for (var channel = 0; channel < config.ChannelCount; channel++)
        {
            var channelPhase = channel * 0.11 + pageSlowPhase;
            var alpha = 12 * Math.Sin(2 * Math.PI * (7.0 + channel * 0.025 + pageIndex * 0.03) * t + channelPhase);
            var beta = 5 * Math.Sin(2 * Math.PI * (15.5 + channel * 0.05) * t + pageSlowPhase * 0.5);
            var slow = 3 * Math.Sin(2 * Math.PI * (0.18 + channel % 5 * 0.015) * pageT + pageSlowPhase);
            var noise = (random.NextDouble() - 0.5) * pageNoiseScale;
            var burst = GetBurstValue(channel);
            var value = (alpha + beta + slow) * pageGain + noise + burst;
            pageSamples[channel][pageSampleIndex] = value;
            batchSamples[channel][batchSampleIndex] = value;
        }

        pageSampleIndex++;
        totalSamples++;
    }

    private void RefreshPageProfileLocked()
    {
        pageGain = 0.75 + random.NextDouble() * 0.65;
        pageNoiseScale = 5.0 + random.NextDouble() * 12.0;
        pageSlowPhase = random.NextDouble() * Math.PI * 2;
        pageHasBurst = random.NextDouble() > 0.35;
        pageBurstStartSample = random.Next(config.SampleRateHz * 3, Math.Max(config.SampleRateHz * 4, config.PageSampleCount - config.SampleRateHz * 3));
        pageBurstDurationSamples = random.Next(config.SampleRateHz / 3, config.SampleRateHz);
        pageBurstChannelOffset = random.Next(0, Math.Max(1, config.ChannelCount - 8));
    }

    private double GetBurstValue(int channel)
    {
        if (!pageHasBurst ||
            channel < pageBurstChannelOffset ||
            channel >= pageBurstChannelOffset + 8 ||
            pageSampleIndex < pageBurstStartSample ||
            pageSampleIndex > pageBurstStartSample + pageBurstDurationSamples)
        {
            return 0;
        }

        var progress = (pageSampleIndex - pageBurstStartSample) / (double)Math.Max(1, pageBurstDurationSamples);
        var envelope = Math.Sin(Math.PI * progress);
        return envelope * 18 * Math.Sin(2 * Math.PI * 4 * progress + channel * 0.3);
    }
}
