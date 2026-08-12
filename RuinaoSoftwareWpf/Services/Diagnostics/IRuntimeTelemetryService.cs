namespace RuinaoSoftwareWpf;

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
