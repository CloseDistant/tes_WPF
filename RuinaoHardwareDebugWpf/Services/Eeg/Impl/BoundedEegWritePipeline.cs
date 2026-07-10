namespace RuinaoHardwareDebugWpf;

using System.Diagnostics;
using System.Threading.Channels;

public sealed class BoundedEegWritePipeline : IEegWritePipeline
{
    public const int DefaultCapacity = 64;

    private readonly IRuntimeTelemetryService telemetry;
    private readonly ILoggingService logger;
    private readonly object lifecycleLock = new();
    private Channel<QueuedBatch>? channel;
    private CancellationTokenSource? consumerCts;
    private Task? consumerTask;
    private int queueDepth;

    public BoundedEegWritePipeline(IRuntimeTelemetryService telemetry, ILoggingService logger)
    {
        this.telemetry = telemetry;
        this.logger = logger;
    }

    public bool IsRunning => consumerTask is { IsCompleted: false };

    public int QueueDepth => Volatile.Read(ref queueDepth);

    public int Capacity => DefaultCapacity;

    public void Start(Func<EegSampleBatch, CancellationToken, Task> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        lock (lifecycleLock)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("EEG 写入管线已经启动。");
            }

            channel = Channel.CreateBounded<QueuedBatch>(new BoundedChannelOptions(DefaultCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            consumerCts = new CancellationTokenSource();
            queueDepth = 0;
            telemetry.SetEegQueue(0, DefaultCapacity);
            consumerTask = ConsumeAsync(channel.Reader, consumer, consumerCts.Token);
        }
    }

    public bool TryEnqueue(EegSampleBatch batch)
    {
        var writer = channel?.Writer;
        if (writer is null)
        {
            telemetry.RecordEegRejectedBatch();
            logger.Error("EEG 写入队列已满或已停止，拒绝接收样本批次。采集端应停止并报告数据不完整。");
            return false;
        }

        var depth = Interlocked.Increment(ref queueDepth);
        if (!writer.TryWrite(new QueuedBatch(batch, Stopwatch.GetTimestamp())))
        {
            depth = Interlocked.Decrement(ref queueDepth);
            telemetry.SetEegQueue(Math.Max(0, depth), DefaultCapacity);
            telemetry.RecordEegRejectedBatch();
            logger.Error("EEG 写入队列已满或已停止，拒绝接收样本批次。采集端应停止并报告数据不完整。");
            return false;
        }

        telemetry.SetEegQueue(Math.Max(0, Volatile.Read(ref queueDepth)), DefaultCapacity);
        return true;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Task? completion;
        lock (lifecycleLock)
        {
            channel?.Writer.TryComplete();
            completion = consumerTask;
        }

        if (completion is not null)
        {
            await completion.WaitAsync(cancellationToken);
        }

        lock (lifecycleLock)
        {
            consumerCts?.Dispose();
            consumerCts = null;
            consumerTask = null;
            channel = null;
            queueDepth = 0;
            telemetry.SetEegQueue(0, DefaultCapacity);
        }
    }

    public async ValueTask DisposeAsync()
    {
        consumerCts?.Cancel();
        try
        {
            await CompleteAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ConsumeAsync(
        ChannelReader<QueuedBatch> reader,
        Func<EegSampleBatch, CancellationToken, Task> consumer,
        CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken))
        {
            var depth = Interlocked.Decrement(ref queueDepth);
            telemetry.SetEegQueue(Math.Max(0, depth), DefaultCapacity);
            telemetry.RecordEegQueueDelay(Stopwatch.GetElapsedTime(item.EnqueuedAtTicks));
            await consumer(item.Batch, cancellationToken);
        }
    }

    private sealed record QueuedBatch(EegSampleBatch Batch, long EnqueuedAtTicks);
}
