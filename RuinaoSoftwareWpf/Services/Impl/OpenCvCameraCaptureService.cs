namespace RuinaoSoftwareWpf;

using OpenCvSharp;

public sealed class OpenCvCameraCaptureService : ICameraCaptureService
{
    private VideoCapture? capture;

    public bool IsOpen => capture?.IsOpened() == true;

    public bool Open(int preferredIndex)
    {
        Close();
        var indices = Enumerable.Range(0, 6)
            .Prepend(preferredIndex)
            .Distinct()
            .Where(index => index >= 0);

        foreach (var index in indices)
        {
            foreach (var api in new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY })
            {
                var candidate = new VideoCapture(index, api);
                if (candidate.IsOpened() && HasReadableFrame(candidate))
                {
                    capture = candidate;
                    return true;
                }

                candidate.Release();
                candidate.Dispose();
            }
        }

        return false;
    }

    public bool Read(Mat targetFrame)
    {
        return capture is not null
            && capture.IsOpened()
            && capture.Read(targetFrame)
            && !targetFrame.Empty();
    }

    public void Close()
    {
        capture?.Release();
        capture?.Dispose();
        capture = null;
    }

    public void Dispose() => Close();

    private static bool HasReadableFrame(VideoCapture candidate)
    {
        using var frame = new Mat();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (candidate.Read(frame) && !frame.Empty())
            {
                return true;
            }
        }

        return false;
    }
}
