namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using System.Collections.Concurrent;

internal interface ICaptureVideoFrameWriter
{
    Task<int> WriteAsync(
        string targetVideoPath,
        BlockingCollection<Mat> queue,
        CaptureTimingState timing,
        Action<DateTimeOffset> firstFrameWritten);
}

internal sealed class CaptureVideoFrameWriter : ICaptureVideoFrameWriter
{
    private readonly ILoggingService logger;
    private readonly IRuntimeTelemetryService telemetry;

    public CaptureVideoFrameWriter(ILoggingService logger, IRuntimeTelemetryService telemetry)
    {
        this.logger = logger;
        this.telemetry = telemetry;
    }

    public async Task<int> WriteAsync(
        string targetVideoPath,
        BlockingCollection<Mat> queue,
        CaptureTimingState timing,
        Action<DateTimeOffset> firstFrameWritten)
    {
        VideoWriter? writer = null;
        var writtenCount = 0;
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            foreach (var frame in queue.GetConsumingEnumerable())
            {
                using (frame)
                {
                    writer ??= new VideoWriter(targetVideoPath, FourCC.MJPG, 12.5, new Size(frame.Width, frame.Height));
                    if (!writer.IsOpened())
                    {
                        continue;
                    }

                    writer.Write(frame);
                    writtenCount++;
                    var writtenAt = DateTimeOffset.Now;
                    timing.RecordFrameWritten(writtenAt, writtenCount);
                    telemetry.RecordDiskWrite((long)frame.Width * frame.Height * frame.Channels(), System.Diagnostics.Stopwatch.GetElapsedTime(startedAt));
                    startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (writtenCount == 1)
                    {
                        logger.Info($"第一帧视频已写入：target={targetVideoPath}");
                        firstFrameWritten(writtenAt);
                    }
                }
            }
        }
        finally
        {
            writer?.Release();
            writer?.Dispose();
            queue.Dispose();
        }

        await Task.CompletedTask;
        return writtenCount;
    }
}
