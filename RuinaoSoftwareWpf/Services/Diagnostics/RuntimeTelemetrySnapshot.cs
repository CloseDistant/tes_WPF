namespace RuinaoSoftwareWpf;

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
