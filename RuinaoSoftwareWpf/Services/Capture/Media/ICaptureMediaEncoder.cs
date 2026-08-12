namespace RuinaoSoftwareWpf;

internal interface ICaptureMediaEncoder
{
    void WaitForFileReady(string filePath);
    Task<double?> CalculateAdjustedFrameRateAsync(string audioPath, int writtenFrameCount);
    Task NormalizeVideoDurationAsync(string rawVideoPath, string normalizedVideoPath, double? adjustedFrameRate);
    Task MergeAsync(string videoPath, string audioPath, string outputPath);
    void DeleteDiscardedRecording(CaptureSessionInfo session);
}
