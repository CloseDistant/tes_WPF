namespace RuinaoSoftwareWpf;

using OpenCvSharp;

public interface ICameraCaptureService : IDisposable
{
    bool IsOpen { get; }

    bool Open(int preferredIndex);

    bool Read(Mat targetFrame);

    void Close();
}
