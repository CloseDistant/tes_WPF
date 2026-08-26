namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using System.Collections.Concurrent;

internal interface ICaptureVideoFrameWriter
{
    Task<int> WriteAsync(
        string targetVideoPath,
        BlockingCollection<Mat> queue,
        CaptureTimingState timing);
}
