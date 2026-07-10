namespace RuinaoHardwareDebugWpf;

using System.IO;
using System.Text;
using System.Diagnostics;

public sealed class EegSegmentFileWriter : IEegSegmentFileWriter
{
    private const int HeaderSize = 256;
    private const int Magic = 0x53454E52; // RNES, little-endian
    private const int Version = 1;
    private const long SampleCountOffset = 36;

    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly IRuntimeTelemetryService telemetry;
    private FileStream? stream;
    private BinaryWriter? writer;
    private string outputDirectory = string.Empty;
    private string segmentsDirectory = string.Empty;
    private long recordingId;
    private EegAcquisitionConfig? config;
    private int segmentSeconds;
    private int maxSamplesPerSegment;
    private int segmentIndex;
    private long segmentStartSampleIndex;
    private long segmentSampleCount;
    private long segmentStartedAtUnixMs;
    private long samplesSinceLastFlush;
    private string currentRelativePath = string.Empty;
    private string currentTemporaryPath = string.Empty;

    public EegSegmentFileWriter(IRuntimeTelemetryService telemetry)
    {
        this.telemetry = telemetry;
    }

    public void Start(string outputDirectory, long recordingId, EegAcquisitionConfig config, int segmentSeconds, long startedAtUnixMs)
    {
        writer?.Dispose();
        stream?.Dispose();
        writer = null;
        stream = null;

        this.outputDirectory = outputDirectory;
        this.recordingId = recordingId;
        this.config = config;
        this.segmentSeconds = segmentSeconds;
        maxSamplesPerSegment = config.SampleRateHz * segmentSeconds;
        segmentIndex = 0;
        segmentStartSampleIndex = 0;
        segmentSampleCount = 0;
        segmentStartedAtUnixMs = startedAtUnixMs;

        segmentsDirectory = Path.Combine(outputDirectory, "segments");
        Directory.CreateDirectory(segmentsDirectory);
        OpenNextSegment(segmentStartedAtUnixMs);
    }

    public async Task<IReadOnlyList<EegDataSegmentInfo>> AppendBatchAsync(EegSampleBatch batch, CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return Array.Empty<EegDataSegmentInfo>();
        }

        await writeGate.WaitAsync(cancellationToken);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var closedSegments = new List<EegDataSegmentInfo>();
            var writtenFromBatch = 0;
            while (writtenFromBatch < batch.SampleCount)
            {
                if (stream is null || writer is null)
                {
                    OpenNextSegment(batch.ReceivedAt.ToUnixTimeMilliseconds());
                }

                var remainingInSegment = maxSamplesPerSegment - (int)segmentSampleCount;
                if (remainingInSegment <= 0)
                {
                    closedSegments.Add(await CloseCurrentSegmentAsync("closed", cancellationToken));
                    OpenNextSegment(batch.ReceivedAt.ToUnixTimeMilliseconds());
                    continue;
                }

                var count = Math.Min(remainingInSegment, batch.SampleCount - writtenFromBatch);
                WriteSamples(batch.ChannelSamples, writtenFromBatch, count);
                segmentSampleCount += count;
                samplesSinceLastFlush += count;
                writtenFromBatch += count;

                if (segmentSampleCount >= maxSamplesPerSegment)
                {
                    closedSegments.Add(await CloseCurrentSegmentAsync("closed", cancellationToken));
                }
            }

            if (stream is not null && samplesSinceLastFlush >= config.SampleRateHz * 2L)
            {
                await stream.FlushAsync(cancellationToken);
                samplesSinceLastFlush = 0;
            }

            return closedSegments;
        }
        finally
        {
            var bytes = (long)batch.SampleCount * batch.ChannelSamples.Length * sizeof(double);
            telemetry.RecordDiskWrite(bytes, Stopwatch.GetElapsedTime(startedAt));
            writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<EegDataSegmentInfo>> StopAsync(CancellationToken cancellationToken = default)
    {
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            if (stream is null)
            {
                config = null;
                return Array.Empty<EegDataSegmentInfo>();
            }

            var closedSegment = await CloseCurrentSegmentAsync("closed", cancellationToken);
            config = null;
            return [closedSegment];
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        writer?.Dispose();
        writer = null;
        if (stream is not null)
        {
            await stream.DisposeAsync();
            stream = null;
        }

        config = null;
        writeGate.Dispose();
    }

    private void OpenNextSegment(long startedAtUnixMs)
    {
        if (config is null)
        {
            throw new InvalidOperationException("EEG segment writer is not started.");
        }

        segmentIndex++;
        segmentSampleCount = 0;
        samplesSinceLastFlush = 0;
        segmentStartedAtUnixMs = startedAtUnixMs;
        currentRelativePath = Path.Combine("segments", $"{segmentIndex:000000}.eegseg");
        var fullPath = Path.Combine(outputDirectory, currentRelativePath);
        currentTemporaryPath = fullPath + ".tmp";

        stream = new FileStream(currentTemporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 128 * 1024, useAsync: true);
        writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteHeader();
    }

    private async Task<EegDataSegmentInfo> CloseCurrentSegmentAsync(string status, CancellationToken cancellationToken)
    {
        if (stream is null || writer is null)
        {
            throw new InvalidOperationException("No active EEG segment.");
        }

        stream.Position = SampleCountOffset;
        writer.Write(segmentSampleCount);
        writer.Flush();
        await stream.FlushAsync(cancellationToken);

        var byteLength = stream.Length;
        writer.Dispose();
        await stream.DisposeAsync();
        stream = null;
        writer = null;

        var finalPath = Path.Combine(outputDirectory, currentRelativePath);
        File.Move(currentTemporaryPath, finalPath, overwrite: true);
        currentTemporaryPath = string.Empty;

        var segment = new EegDataSegmentInfo(
            segmentIndex,
            currentRelativePath,
            segmentStartSampleIndex,
            segmentSampleCount,
            segmentStartedAtUnixMs,
            DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            byteLength,
            status);

        segmentStartSampleIndex += segmentSampleCount;
        return segment;
    }

    private void WriteHeader()
    {
        if (config is null || stream is null || writer is null)
        {
            throw new InvalidOperationException("EEG segment writer is not started.");
        }

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(recordingId);
        writer.Write(segmentIndex);
        writer.Write(segmentStartSampleIndex);
        writer.Write(config.SampleRateHz);
        writer.Write(config.ChannelCount);
        writer.Write(0L);
        writer.Write(segmentStartedAtUnixMs);
        writer.Write(HeaderSize);
        writer.Write(segmentSeconds);
        writer.Write("float32");

        while (stream.Position < HeaderSize)
        {
            writer.Write((byte)0);
        }
    }

    private void WriteSamples(double[][] channelSamples, int batchOffset, int count)
    {
        if (config is null || writer is null)
        {
            throw new InvalidOperationException("EEG segment writer is not started.");
        }

        for (var sample = 0; sample < count; sample++)
        {
            var sourceIndex = batchOffset + sample;
            for (var channel = 0; channel < config.ChannelCount; channel++)
            {
                writer.Write((float)channelSamples[channel][sourceIndex]);
            }
        }
    }
}
