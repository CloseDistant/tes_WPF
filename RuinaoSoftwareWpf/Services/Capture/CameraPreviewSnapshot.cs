namespace RuinaoSoftwareWpf;

using System.Buffers;

public enum CameraFaceState
{
    NotDetected,
    InsideGuide,
    OutsideGuide
}

public readonly record struct NormalizedCameraRect(
    double X,
    double Y,
    double Width,
    double Height);

/// <summary>
/// 后台摄像头管线生成的不可变预览快照。
/// 像素采用 BGRA32，界面只负责创建并显示 BitmapSource。
/// </summary>
public sealed class CameraPreviewSnapshot : IDisposable
{
    private byte[]? bgraPixels;

    internal CameraPreviewSnapshot(
        long sequence,
        DateTimeOffset capturedAt,
        int width,
        int height,
        int stride,
        byte[] bgraPixels,
        int pixelLength,
        NormalizedCameraRect guideBounds,
        NormalizedCameraRect? faceBounds,
        CameraFaceState faceState,
        int recordedFrameCount)
    {
        Sequence = sequence;
        CapturedAt = capturedAt;
        Width = width;
        Height = height;
        Stride = stride;
        this.bgraPixels = bgraPixels;
        PixelLength = pixelLength;
        GuideBounds = guideBounds;
        FaceBounds = faceBounds;
        FaceState = faceState;
        RecordedFrameCount = recordedFrameCount;
    }

    public long Sequence { get; }

    public DateTimeOffset CapturedAt { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public byte[] BgraPixels => bgraPixels
        ?? throw new ObjectDisposedException(nameof(CameraPreviewSnapshot));

    public int PixelLength { get; }

    public NormalizedCameraRect GuideBounds { get; }

    public NormalizedCameraRect? FaceBounds { get; }

    public CameraFaceState FaceState { get; }

    public int RecordedFrameCount { get; }

    public void Dispose()
    {
        var pixels = Interlocked.Exchange(ref bgraPixels, null);
        if (pixels is not null)
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }
}
