namespace RuinaoSoftwareWpf;

using OpenCvSharp;

/// <summary>
/// Presentation 与 OpenCV 录帧实现之间的专用边界。
/// 应用层媒体控制契约不得引用该接口或 Mat。
/// </summary>
public interface ICaptureVideoFrameSink
{
    void ConfigureCaptureProfile(CameraCaptureProfileSnapshot profile);

    int RecordFrame(Mat frame);
}
