namespace RuinaoHardwareDebugWpf;

public sealed record RuntimeTelemetrySnapshot(
    DateTimeOffset CapturedAt,
    double ProcessCpuPercent,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int EegQueueDepth,
    int EegQueueCapacity,
    long EegRejectedBatches,
    double EegQueueDelayMs,
    double DatabaseCommitDelayMs,
    double DiskWriteBytesPerSecond,
    long PacketLossCount,
    double UiFrameTimeMs);

public interface IRuntimeTelemetryService
{
    RuntimeTelemetrySnapshot GetSnapshot();

    void SetEegQueue(int depth, int capacity);
    void RecordEegQueueDelay(TimeSpan delay);
    void RecordEegRejectedBatch();
    void RecordDatabaseCommitDelay(TimeSpan delay);
    void RecordDiskWrite(long bytes, TimeSpan elapsed);
    void RecordPacketLoss(long count = 1);
    void RecordUiFrame(TimeSpan frameTime);
}
