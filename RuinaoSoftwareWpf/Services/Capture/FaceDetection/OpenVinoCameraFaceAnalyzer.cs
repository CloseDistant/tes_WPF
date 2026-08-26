namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using OpenCvSharp.Dnn;
using Sdcb.OpenVINO;
using Sdcb.OpenVINO.Natives;
using System.Diagnostics;
using System.IO;

/// <summary>
/// 使用随软件部署的 Open Model Zoo 模型，在本地 CPU 完成人脸数量、98 点关键点和头部姿态分析。
/// 本服务只允许在独立分析线程调用，不能进入摄像头读取、录像写入或 UI 线程。
/// </summary>
public sealed class OpenVinoCameraFaceAnalyzer : ICameraFaceAnalyzer
{
    private const double FaceDetectionConfidence = 0.65;
    private const double PoseInputPaddingRatio = 0.12;
    private static readonly Size FaceDetectorInputSize = new(300, 300);
    private static readonly Size LandmarkInputSize = new(64, 64);
    private static readonly Size HeadPoseInputSize = new(60, 60);
    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromSeconds(10);

    private readonly ILoggingService logger;
    private readonly FaceQualityEvaluator qualityEvaluator;
    private readonly AdaptiveEyeOpennessBaseline eyeOpennessBaseline = new();
    private OVCore? inferenceCore;
    private ModelRunner? faceDetector;
    private ModelRunner? landmarkDetector;
    private ModelRunner? headPoseEstimator;
    private bool initializationAttempted;
    private Exception? initializationError;
    private long lastErrorLogTimestamp;

    public OpenVinoCameraFaceAnalyzer(ILoggingService logger)
    {
        this.logger = logger;
        qualityEvaluator = new FaceQualityEvaluator();
    }

    public void Reset() => eyeOpennessBaseline.Reset();

    public CameraFaceAnalysis Analyze(
        Mat frame,
        long sequence,
        long analyzedAtTimestamp,
        DateTimeOffset capturedAt)
    {
        if (frame.Empty())
        {
            return CameraFaceAnalysis.Unavailable(sequence, analyzedAtTimestamp, capturedAt);
        }

        try
        {
            EnsureInitialized();
            if (initializationError is not null)
            {
                LogFailureThrottled("人脸检测模型不可用。", initializationError);
                return CameraFaceAnalysis.Unavailable(sequence, analyzedAtTimestamp, capturedAt);
            }

            var detectedFaces = DetectFaces(frame);
            if (detectedFaces.Count == 0)
            {
                return new CameraFaceAnalysis(
                    sequence,
                    analyzedAtTimestamp,
                    capturedAt,
                    CameraFaceState.NoFace,
                    0,
                    null);
            }

            var primary = detectedFaces.MaxBy(static item => item.Bounds.Width * item.Bounds.Height);
            var landmarkInputBounds = CalculateLandmarkInputBounds(
                primary.Bounds,
                frame.Width,
                frame.Height);
            var poseInputBounds = ExpandAndClamp(
                primary.Bounds,
                frame.Width,
                frame.Height,
                PoseInputPaddingRatio);
            if (detectedFaces.Count > 1)
            {
                return new CameraFaceAnalysis(
                    sequence,
                    analyzedAtTimestamp,
                    capturedAt,
                    CameraFaceState.MultipleFaces,
                    detectedFaces.Count,
                    Normalize(primary.Bounds, frame.Width, frame.Height),
                    DetectorFaceBounds: Normalize(primary.Bounds, frame.Width, frame.Height),
                    LandmarkInputBounds: Normalize(landmarkInputBounds, frame.Width, frame.Height));
            }

            var landmarks = DetectLandmarks(frame, landmarkInputBounds);
            var pose = EstimateHeadPose(frame, poseInputBounds);
            var observation = new FaceQualityObservation(
                landmarks,
                pose.Yaw,
                pose.Pitch,
                pose.Roll);
            var evaluation = qualityEvaluator.Evaluate(
                observation,
                eyeOpennessBaseline.ClosedEyeThreshold);
            eyeOpennessBaseline.Observe(evaluation);
            evaluation = qualityEvaluator.Evaluate(
                observation,
                eyeOpennessBaseline.ClosedEyeThreshold);
            var detectedFaceBounds = CalculateDetectedFaceBounds(
                primary.Bounds,
                landmarks,
                frame.Width,
                frame.Height,
                FaceQualityThresholds.Default.LandmarkConfidence);

            return new CameraFaceAnalysis(
                sequence,
                analyzedAtTimestamp,
                capturedAt,
                evaluation.State,
                1,
                Normalize(detectedFaceBounds, frame.Width, frame.Height),
                pose.Yaw,
                pose.Pitch,
                pose.Roll,
                evaluation.LeftEyeAspectRatio,
                evaluation.RightEyeAspectRatio,
                Normalize(primary.Bounds, frame.Width, frame.Height),
                Normalize(landmarkInputBounds, frame.Width, frame.Height),
                NormalizeLandmarks(landmarks, frame.Width, frame.Height),
                eyeOpennessBaseline.OpenEyeBaseline,
                evaluation.ClosedEyeThreshold);
        }
        catch (Exception exception)
        {
            LogFailureThrottled("人脸状态分析失败，将输出检测器不可用状态。", exception);
            return CameraFaceAnalysis.Unavailable(sequence, analyzedAtTimestamp, capturedAt);
        }
    }

    public void Dispose()
    {
        headPoseEstimator?.Dispose();
        landmarkDetector?.Dispose();
        faceDetector?.Dispose();
        inferenceCore?.Dispose();
        headPoseEstimator = null;
        landmarkDetector = null;
        faceDetector = null;
        inferenceCore = null;
    }

    private void EnsureInitialized()
    {
        if (initializationAttempted)
        {
            return;
        }

        initializationAttempted = true;
        try
        {
            var modelRoot = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "CaptureWorkbench",
                "FaceDetectionModels");
            inferenceCore = new OVCore();
            var deviceOptions = new DeviceOptions("CPU")
            {
                InferenceNumThreads = Math.Clamp(Environment.ProcessorCount / 4, 1, 2),
                PerformanceMode = PerformanceMode.Latency,
                EnableHyperThreading = false
            };
            faceDetector = ModelRunner.Create(
                inferenceCore,
                modelRoot,
                "face-detection-retail-0004",
                deviceOptions);
            landmarkDetector = ModelRunner.Create(
                inferenceCore,
                modelRoot,
                "facial-landmarks-98-detection-0001",
                deviceOptions);
            headPoseEstimator = ModelRunner.Create(
                inferenceCore,
                modelRoot,
                "head-pose-estimation-adas-0001",
                deviceOptions);
            logger.Info(
                $"本地人脸检测模型加载完成：modelRoot={modelRoot}, "
                + $"inferenceThreads={deviceOptions.InferenceNumThreads}");
        }
        catch (Exception exception)
        {
            initializationError = exception;
            Dispose();
        }
    }

    private List<DetectedFace> DetectFaces(Mat frame)
    {
        var output = faceDetector!.Run(frame, FaceDetectorInputSize);
        var rowCount = output.Length / 7;
        var faces = new List<DetectedFace>(rowCount);
        for (var row = 0; row < rowCount; row++)
        {
            var offset = row * 7;
            var confidence = output[offset + 2];
            if (confidence < FaceDetectionConfidence)
            {
                continue;
            }

            var left = (int)Math.Round(output[offset + 3] * frame.Width);
            var top = (int)Math.Round(output[offset + 4] * frame.Height);
            var right = (int)Math.Round(output[offset + 5] * frame.Width);
            var bottom = (int)Math.Round(output[offset + 6] * frame.Height);
            var bounds = ClampRect(left, top, right, bottom, frame.Width, frame.Height);
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                faces.Add(new DetectedFace(bounds, confidence));
            }
        }

        return faces;
    }

    private IReadOnlyList<FaceLandmarkPoint> DetectLandmarks(Mat frame, Rect faceBounds)
    {
        using var face = new Mat(frame, faceBounds);
        var output = landmarkDetector!.Run(face, LandmarkInputSize);
        const int landmarkCount = 98;
        const int heatmapWidth = 16;
        var heatmapArea = output.Length / landmarkCount;
        var heatmapHeight = heatmapArea / heatmapWidth;
        if (heatmapHeight <= 0 || output.Length != landmarkCount * heatmapWidth * heatmapHeight)
        {
            throw new InvalidOperationException(
                $"关键点模型输出尺寸异常：elements={output.Length}");
        }

        var landmarks = new FaceLandmarkPoint[landmarkCount];
        for (var index = 0; index < landmarks.Length; index++)
        {
            var heatmapOffset = index * heatmapArea;
            var maximum = float.MinValue;
            var maximumIndex = 0;
            for (var cell = 0; cell < heatmapArea; cell++)
            {
                var value = output[heatmapOffset + cell];
                if (value <= maximum)
                {
                    continue;
                }

                maximum = value;
                maximumIndex = cell;
            }

            var maximumX = maximumIndex % heatmapWidth;
            var maximumY = maximumIndex / heatmapWidth;
            landmarks[index] = new FaceLandmarkPoint(
                faceBounds.X + (maximumX + 0.5) / heatmapWidth * faceBounds.Width,
                faceBounds.Y + (maximumY + 0.5) / heatmapHeight * faceBounds.Height,
                maximum);
        }

        return landmarks;
    }

    private HeadPose EstimateHeadPose(Mat frame, Rect faceBounds)
    {
        using var face = new Mat(frame, faceBounds);
        var outputs = headPoseEstimator!.RunNamedOutputs(face, HeadPoseInputSize, "fc_y", "fc_p", "fc_r");
        return new HeadPose(
            outputs[0][0],
            outputs[1][0],
            outputs[2][0]);
    }

    private void LogFailureThrottled(string message, Exception exception)
    {
        var now = Stopwatch.GetTimestamp();
        var last = Volatile.Read(ref lastErrorLogTimestamp);
        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < ErrorLogInterval)
        {
            return;
        }

        Volatile.Write(ref lastErrorLogTimestamp, now);
        logger.Error(message, exception);
    }

    private static Rect ExpandAndClamp(
        Rect bounds,
        int width,
        int height,
        double paddingRatio)
    {
        var horizontalPadding = (int)Math.Round(bounds.Width * paddingRatio);
        var verticalPadding = (int)Math.Round(bounds.Height * paddingRatio);
        return ClampRect(
            bounds.Left - horizontalPadding,
            bounds.Top - verticalPadding,
            bounds.Right + horizontalPadding,
            bounds.Bottom + verticalPadding,
            width,
            height);
    }

    internal static Rect CalculateLandmarkInputBounds(
        Rect detectorBounds,
        int frameWidth,
        int frameHeight)
    {
        // 98点模型按官方定义接收已经检测出的人脸区域并缩放到64×64。
        // 不再人为扩大或下移输入框，避免脸部在模型输入中缩小、偏移后把下巴预测到嘴部附近。
        return ClampRect(
            detectorBounds.Left,
            detectorBounds.Top,
            detectorBounds.Right,
            detectorBounds.Bottom,
            frameWidth,
            frameHeight);
    }

    internal static Rect CalculateDetectedFaceBounds(
        Rect detectorBounds,
        IReadOnlyList<FaceLandmarkPoint> landmarks,
        int frameWidth,
        int frameHeight,
        double minimumConfidence)
    {
        FaceLandmarkPoint? previous = null;
        var minimumX = (double)detectorBounds.Left;
        var maximumX = (double)detectorBounds.Right;
        var spacingTotal = 0d;
        var spacingCount = 0;
        var reliablePointCount = 0;
        var reliableChinPointCount = 0;
        var detectedChinBottom = double.MinValue;

        foreach (var landmarkIndex in FaceLandmarkIndices.FaceContour)
        {
            if (landmarkIndex >= landmarks.Count
                || landmarks[landmarkIndex].Confidence < minimumConfidence)
            {
                previous = null;
                continue;
            }

            var current = landmarks[landmarkIndex];
            minimumX = Math.Min(minimumX, current.X);
            maximumX = Math.Max(maximumX, current.X);
            reliablePointCount++;

            if (FaceLandmarkIndices.ChinCenter.Contains(landmarkIndex))
            {
                detectedChinBottom = Math.Max(detectedChinBottom, current.Y);
                reliableChinPointCount++;
            }

            if (previous is { } adjacent)
            {
                spacingTotal += Distance(adjacent, current);
                spacingCount++;
            }

            previous = current;
        }

        if (reliablePointCount == 0
            || reliableChinPointCount < FaceLandmarkIndices.MinimumReliableChinCenterPointCount)
        {
            return detectorBounds;
        }

        var contourMargin = spacingCount > 0
            ? Math.Clamp((int)Math.Ceiling(spacingTotal / spacingCount), 2, 16)
            : 2;
        return ClampRect(
            Math.Min(detectorBounds.Left, (int)Math.Floor(minimumX) - contourMargin),
            detectorBounds.Top,
            Math.Max(detectorBounds.Right, (int)Math.Ceiling(maximumX) + contourMargin),
            (int)Math.Ceiling(detectedChinBottom) + contourMargin,
            frameWidth,
            frameHeight);
    }

    private static double Distance(FaceLandmarkPoint left, FaceLandmarkPoint right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static Rect ClampRect(int left, int top, int right, int bottom, int width, int height)
    {
        left = Math.Clamp(left, 0, Math.Max(0, width - 1));
        top = Math.Clamp(top, 0, Math.Max(0, height - 1));
        right = Math.Clamp(right, left + 1, width);
        bottom = Math.Clamp(bottom, top + 1, height);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static NormalizedCameraRect Normalize(Rect rect, int width, int height) => new(
        rect.X / (double)width,
        rect.Y / (double)height,
        rect.Width / (double)width,
        rect.Height / (double)height);

    private static IReadOnlyList<NormalizedCameraLandmark> NormalizeLandmarks(
        IReadOnlyList<FaceLandmarkPoint> landmarks,
        int width,
        int height) => landmarks
        .Select((point, index) => new NormalizedCameraLandmark(
            index,
            point.X / width,
            point.Y / height,
            point.Confidence))
        .ToArray();

    private sealed class ModelRunner : IDisposable
    {
        private readonly CompiledModel compiledModel;
        private readonly InferRequest inferRequest;

        private ModelRunner(CompiledModel compiledModel)
        {
            this.compiledModel = compiledModel;
            inferRequest = compiledModel.CreateInferRequest();
        }

        public static ModelRunner Create(
            OVCore core,
            string modelRoot,
            string modelName,
            DeviceOptions deviceOptions)
        {
            var xmlPath = Path.Combine(modelRoot, $"{modelName}.xml");
            var binPath = Path.Combine(modelRoot, $"{modelName}.bin");
            if (!File.Exists(xmlPath) || !File.Exists(binPath))
            {
                throw new FileNotFoundException($"人脸检测模型文件不完整：{modelName}");
            }

            using var model = core.ReadModel(xmlPath, binPath);
            return new ModelRunner(core.CompileModel(model, deviceOptions));
        }

        public float[] Run(Mat image, Size inputSize)
        {
            using var blob = CreateInputBlob(image, inputSize);
            using var tensor = CreateInputTensor(blob, inputSize);
            inferRequest.Inputs.Primary = tensor;
            inferRequest.Run();
            using var output = inferRequest.Outputs.Primary;
            return output.GetData<float>().ToArray();
        }

        public float[][] RunNamedOutputs(Mat image, Size inputSize, params string[] outputNames)
        {
            using var blob = CreateInputBlob(image, inputSize);
            using var tensor = CreateInputTensor(blob, inputSize);
            inferRequest.Inputs.Primary = tensor;
            inferRequest.Run();
            var outputs = new float[outputNames.Length][];
            for (var index = 0; index < outputNames.Length; index++)
            {
                using var output = inferRequest.GetTensorByPort(compiledModel.Outputs[outputNames[index]]);
                outputs[index] = output.GetData<float>().ToArray();
            }

            return outputs;
        }

        public void Dispose()
        {
            inferRequest.Dispose();
            compiledModel.Dispose();
        }

        private static Mat CreateInputBlob(Mat image, Size inputSize) =>
            CvDnn.BlobFromImage(
                image,
                1,
                inputSize,
                Scalar.All(0),
                swapRB: false,
                crop: false);

        private static Tensor CreateInputTensor(Mat blob, Size inputSize) =>
            Tensor.FromRaw(
                blob.Data,
                new Shape([1, 3, inputSize.Height, inputSize.Width]),
                ov_element_type_e.F32);
    }

    private readonly record struct DetectedFace(Rect Bounds, double Confidence);

    private readonly record struct HeadPose(double Yaw, double Pitch, double Roll);
}
