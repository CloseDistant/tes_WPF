namespace RuinaoHardwareDebugWpf;

using System.Diagnostics;

public sealed class RuntimeTelemetryService : IRuntimeTelemetryService
{
    private readonly object syncRoot = new();
    private readonly Process process = Process.GetCurrentProcess();
    private DateTimeOffset lastCpuSampleAt = DateTimeOffset.UtcNow;
    private TimeSpan lastCpuTime;
    private double processCpuPercent;
    private int eegQueueDepth;
    private int eegQueueCapacity;
    private long eegRejectedBatches;
    private double eegQueueDelayMs;
    private double databaseCommitDelayMs;
    private double diskWriteBytesPerSecond;
    private long packetLossCount;
    private double uiFrameTimeMs;

    public RuntimeTelemetrySnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            process.Refresh();
            var now = DateTimeOffset.UtcNow;
            var cpuTime = process.TotalProcessorTime;
            var elapsedMs = (now - lastCpuSampleAt).TotalMilliseconds;
            if (elapsedMs > 0)
            {
                var cpuMs = (cpuTime - lastCpuTime).TotalMilliseconds;
                processCpuPercent = Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100.0, 0, 100);
                lastCpuSampleAt = now;
                lastCpuTime = cpuTime;
            }

            return new RuntimeTelemetrySnapshot(
                now,
                processCpuPercent,
                process.WorkingSet64,
                GC.GetTotalMemory(false),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                eegQueueDepth,
                eegQueueCapacity,
                eegRejectedBatches,
                eegQueueDelayMs,
                databaseCommitDelayMs,
                diskWriteBytesPerSecond,
                packetLossCount,
                uiFrameTimeMs);
        }
    }

    public void SetEegQueue(int depth, int capacity)
    {
        lock (syncRoot)
        {
            eegQueueDepth = depth;
            eegQueueCapacity = capacity;
        }
    }

    public void RecordEegQueueDelay(TimeSpan delay)
    {
        lock (syncRoot)
        {
            eegQueueDelayMs = Smooth(eegQueueDelayMs, delay.TotalMilliseconds);
        }
    }

    public void RecordEegRejectedBatch()
    {
        Interlocked.Increment(ref eegRejectedBatches);
    }

    public void RecordDatabaseCommitDelay(TimeSpan delay)
    {
        lock (syncRoot)
        {
            databaseCommitDelayMs = Smooth(databaseCommitDelayMs, delay.TotalMilliseconds);
        }
    }

    public void RecordDiskWrite(long bytes, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        lock (syncRoot)
        {
            diskWriteBytesPerSecond = Smooth(diskWriteBytesPerSecond, bytes / elapsed.TotalSeconds);
        }
    }

    public void RecordPacketLoss(long count = 1) => Interlocked.Add(ref packetLossCount, Math.Max(0, count));

    public void RecordUiFrame(TimeSpan frameTime)
    {
        lock (syncRoot)
        {
            uiFrameTimeMs = Smooth(uiFrameTimeMs, frameTime.TotalMilliseconds);
        }
    }

    private static double Smooth(double previous, double current) => previous <= 0 ? current : previous * 0.8 + current * 0.2;
}
