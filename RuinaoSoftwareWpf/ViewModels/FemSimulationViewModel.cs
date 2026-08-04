using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace RuinaoSoftwareWpf;

public sealed class FemSimulationViewModel : ObservableObject
{
    private readonly IHardwareService hardwareService;
    private readonly IFemSampleResultLocator sampleResultLocator;
    private IMainUiContext? context;
    private NiftiVolume? volume;
    private FemSliceOverlay? sliceOverlay;
    private FemResultPackage? resultPackage;
    private int currentStep = 1, coronalSlice = 12, sagittalSlice = 12, axialSlice = 12;
    private bool is3D, isBusy;
    private bool bundledSampleLoadAttempted;
    private string? modelPath;
    private string modelStatus = "结果：尚未导入";
    private string simulationStatus = "显示：等待本地计算结果";
    private string volumeInformation = "尚未读取体数据";
    private BitmapSource? coronalImage, sagittalImage, axialImage;

    public FemSimulationViewModel(
        ISimulationService simulationService,
        IHardwareService hardwareService,
        IFemSampleResultLocator sampleResultLocator)
    {
        // Computation intentionally remains outside WPF. Keep the injected service
        // for composition compatibility while this view only imports finished results.
        _ = simulationService;
        this.hardwareService = hardwareService;
        this.sampleResultLocator = sampleResultLocator;
        PreviousStepCommand = new RelayCommand(_ => CurrentStep = 1);
        GenerateMontageCommand = new AsyncRelayCommand(
            ReloadCurrentResultAsync,
            () => resultPackage is not null && !IsBusy,
            HandleError);
        DecreaseSliceCommand = new RelayCommand(
            parameter => ChangeSlice(parameter, -1),
            _ => HasVolume && !IsBusy);
        IncreaseSliceCommand = new RelayCommand(
            parameter => ChangeSlice(parameter, 1),
            _ => HasVolume && !IsBusy);
        Select2DCommand = new RelayCommand(_ => Is3D = false);
        Select3DCommand = new RelayCommand(
            _ => Is3D = true,
            _ => HasCompatible3DResult);
        SendToDeviceCommand = new AsyncRelayCommand(
            SendToDeviceAsync,
            () => HasVolume && !IsBusy,
            HandleError);
    }

    public IMainUiContext Context =>
        context ?? throw new InvalidOperationException("有限元显示上下文尚未初始化。");

    public ICommand PreviousStepCommand { get; }
    public AsyncRelayCommand GenerateMontageCommand { get; }
    public ICommand DecreaseSliceCommand { get; }
    public ICommand IncreaseSliceCommand { get; }
    public ICommand Select2DCommand { get; }
    public RelayCommand Select3DCommand { get; }
    public AsyncRelayCommand SendToDeviceCommand { get; }

    public int CurrentStep
    {
        get => currentStep;
        set
        {
            if (!SetProperty(ref currentStep, value)) return;
            OnPropertyChanged(nameof(IsLoadStep));
            OnPropertyChanged(nameof(IsVisualizationStep));
        }
    }

    public bool IsLoadStep => CurrentStep == 1;
    public bool IsVisualizationStep => CurrentStep == 2;

    public bool Is3D
    {
        get => is3D;
        set
        {
            var requested = value && HasCompatible3DResult;
            SetProperty(ref is3D, requested);
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
                RefreshCommands();
        }
    }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
    public bool HasVolume => volume is not null;
    public bool HasFemOverlay => sliceOverlay is not null;
    public bool HasResultPackage => resultPackage is not null;
    public bool HasCompatible3DResult =>
        HasVolume &&
        HasFemOverlay &&
        resultPackage is not null &&
        File.Exists(resultPackage.Field3DPath);

    public string ThreeDimensionalStatus => HasCompatible3DResult
        ? $"受试者 {SubjectId} 的三维有限元结果已就绪"
        : "请先导入完整且校验通过的 result-manifest.json";

    public string? ModelPath
    {
        get => modelPath;
        private set => SetProperty(ref modelPath, value);
    }

    public string? ResultManifestPath => resultPackage?.ManifestPath;
    public string? ThreeDimensionalDataPath => resultPackage?.Field3DPath;
    public string ThreeDimensionalViewerMode =>
        resultPackage?.ViewerMode ?? "dynamic";
    public string SubjectId => resultPackage?.SubjectId ?? "--";
    public string ModelFileName => resultPackage is null
        ? "尚未选择本地结果包"
        : $"{resultPackage.SubjectId} · {Path.GetFileName(resultPackage.ManifestPath)}";

    public string ModelStatus
    {
        get => modelStatus;
        private set => SetProperty(ref modelStatus, value);
    }

    public string SimulationStatus
    {
        get => simulationStatus;
        private set => SetProperty(ref simulationStatus, value);
    }

    public string VolumeInformation
    {
        get => volumeInformation;
        private set => SetProperty(ref volumeInformation, value);
    }

    public BitmapSource? CoronalImage
    {
        get => coronalImage;
        private set => SetProperty(ref coronalImage, value);
    }

    public BitmapSource? SagittalImage
    {
        get => sagittalImage;
        private set => SetProperty(ref sagittalImage, value);
    }

    public BitmapSource? AxialImage
    {
        get => axialImage;
        private set => SetProperty(ref axialImage, value);
    }

    public string FieldLegendMaximumText =>
        sliceOverlay is null ? "--" : $"{sliceOverlay.DisplayMaximum:0.000}";
    public string FieldLegendThreeQuarterText =>
        sliceOverlay is null ? "--" : $"{sliceOverlay.DisplayMaximum * 0.75f:0.000}";
    public string FieldLegendHalfText =>
        sliceOverlay is null ? "--" : $"{sliceOverlay.DisplayMaximum * 0.5f:0.000}";
    public string FieldLegendQuarterText =>
        sliceOverlay is null ? "--" : $"{sliceOverlay.DisplayMaximum * 0.25f:0.000}";

    public int CoronalSlice
    {
        get => coronalSlice;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, (volume?.Height ?? 1000) - 1));
            if (SetProperty(ref coronalSlice, clamped)) RefreshCoronal();
        }
    }

    public int SagittalSlice
    {
        get => sagittalSlice;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, (volume?.Width ?? 1000) - 1));
            if (SetProperty(ref sagittalSlice, clamped)) RefreshSagittal();
        }
    }

    public int AxialSlice
    {
        get => axialSlice;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, (volume?.Depth ?? 1000) - 1));
            if (SetProperty(ref axialSlice, clamped)) RefreshAxial();
        }
    }

    public void Initialize(IMainUiContext uiContext)
    {
        context = uiContext;
        OnPropertyChanged(nameof(Context));
    }

    public async Task<bool> EnsureBundledSampleLoadedAsync(
        CancellationToken cancellationToken = default)
    {
        if (HasResultPackage)
            return true;

        if (bundledSampleLoadAttempted)
            return false;

        bundledSampleLoadAttempted = true;
        var manifestPath = sampleResultLocator.FindBundledManifest();
        if (manifestPath is null)
        {
            ModelStatus = "结果：未找到内置示例";
            SimulationStatus = "显示：可使用“选择清单”手动导入 result-manifest.json";
            return false;
        }

        try
        {
            await LoadResultPackageAsync(manifestPath, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bundledSampleLoadAttempted = false;
            throw;
        }
    }

    public async Task LoadResultPackageAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            ModelStatus = "结果：正在校验清单和文件完整性…";
            SimulationStatus = "显示：正在读取自研 CHARM/FEM 结果";
            var candidatePackage = await FemResultPackage.LoadAsync(
                manifestPath,
                cancellationToken);
            var candidateVolume = await NiftiVolume.LoadAsync(
                candidatePackage.T1Path,
                cancellationToken);
            var candidateOverlay = await FemSliceOverlay.LoadAsync(
                candidatePackage.Field2DPath,
                cancellationToken);

            if (!candidateOverlay.Matches(candidateVolume))
            {
                throw new InvalidDataException(
                    "二维有限元场与结果包中的 T1 网格不匹配，已拒绝加载。");
            }

            resultPackage = candidatePackage;
            ModelPath = candidatePackage.T1Path;
            volume = candidateVolume;
            sliceOverlay = candidateOverlay;

            var defaultSlices = sliceOverlay.GetDefaultSlices(volume);
            CoronalSlice = defaultSlices.Coronal;
            SagittalSlice = defaultSlices.Sagittal;
            AxialSlice = defaultSlices.Axial;
            RefreshAllSlices();

            Is3D = false;
            CurrentStep = 2;
            ModelStatus = $"结果：{candidatePackage.SubjectId} 已校验并加载";
            SimulationStatus = "显示：二维场与三维场均已就绪；未在 WPF 内启动计算";
            VolumeInformation =
                $"{candidatePackage.SubjectId} | {volume.DimensionsText} | " +
                $"体素 {volume.VoxelSizeText} | {candidatePackage.CoordinateSystem}";
            NotifyResultPropertiesChanged();
        }
        catch (Exception exception)
        {
            ModelStatus = "结果：加载失败";
            SimulationStatus = $"错误：{exception.Message}";
            throw;
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    public object CreateExportData() => new
    {
        schemaVersion = 2,
        subjectId = SubjectId,
        resultManifest = ResultManifestPath,
        source = ModelPath,
        dimensions = volume is null
            ? null
            : new[] { volume.Width, volume.Height, volume.Depth },
        voxelSizeMm = volume is null
            ? null
            : new[] { volume.VoxelX, volume.VoxelY, volume.VoxelZ },
        slices = new
        {
            coronal = CoronalSlice,
            sagittal = SagittalSlice,
            axial = AxialSlice
        },
        viewMode = Is3D ? "subject-fem-3d-preview" : "subject-fem-2d",
        generatedUtc = DateTimeOffset.UtcNow
    };

    public void MarkExported(string path) =>
        SimulationStatus = $"显示：当前结果和切片配置已导出至 {Path.GetDirectoryName(path)}";

    private Task ReloadCurrentResultAsync(CancellationToken cancellationToken)
    {
        var manifest = resultPackage?.ManifestPath;
        return manifest is null
            ? Task.CompletedTask
            : LoadResultPackageAsync(manifest, cancellationToken);
    }

    private async Task SendToDeviceAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            SimulationStatus = "设备：正在检查刺激设备链路…";
            var result = await hardwareService.HandshakeAsync(cancellationToken);
            SimulationStatus = result.IsConnected
                ? "设备：链路正常；当前仅显示服务器/本地计算结果，未下发刺激参数"
                : "设备：未连接；有限元结果未发送";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ChangeSlice(object? parameter, int delta)
    {
        switch (parameter as string)
        {
            case "Coronal": CoronalSlice += delta; break;
            case "Sagittal": SagittalSlice += delta; break;
            case "Axial": AxialSlice += delta; break;
        }
    }

    private void RefreshAllSlices()
    {
        RefreshCoronal();
        RefreshSagittal();
        RefreshAxial();
    }

    private void RefreshCoronal()
    {
        if (volume is not null)
            CoronalImage = sliceOverlay?.CreateCoronalSlice(volume, CoronalSlice)
                ?? volume.CreateCoronalSlice(CoronalSlice);
    }

    private void RefreshSagittal()
    {
        if (volume is not null)
            SagittalImage = sliceOverlay?.CreateSagittalSlice(volume, SagittalSlice)
                ?? volume.CreateSagittalSlice(SagittalSlice);
    }

    private void RefreshAxial()
    {
        if (volume is not null)
            AxialImage = sliceOverlay?.CreateAxialSlice(volume, AxialSlice)
                ?? volume.CreateAxialSlice(AxialSlice);
    }

    private void NotifyResultPropertiesChanged()
    {
        OnPropertyChanged(nameof(ModelFileName));
        OnPropertyChanged(nameof(HasModel));
        OnPropertyChanged(nameof(HasVolume));
        OnPropertyChanged(nameof(HasFemOverlay));
        OnPropertyChanged(nameof(HasResultPackage));
        OnPropertyChanged(nameof(HasCompatible3DResult));
        OnPropertyChanged(nameof(ThreeDimensionalStatus));
        OnPropertyChanged(nameof(ResultManifestPath));
        OnPropertyChanged(nameof(ThreeDimensionalDataPath));
        OnPropertyChanged(nameof(ThreeDimensionalViewerMode));
        OnPropertyChanged(nameof(SubjectId));
        OnPropertyChanged(nameof(FieldLegendMaximumText));
        OnPropertyChanged(nameof(FieldLegendThreeQuarterText));
        OnPropertyChanged(nameof(FieldLegendHalfText));
        OnPropertyChanged(nameof(FieldLegendQuarterText));
    }

    private void HandleError(Exception exception)
    {
        IsBusy = false;
        Is3D = false;
        ModelStatus = "结果：加载失败";
        SimulationStatus = $"错误：{exception.Message}";
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        GenerateMontageCommand.RaiseCanExecuteChanged();
        SendToDeviceCommand.RaiseCanExecuteChanged();
        Select3DCommand.RaiseCanExecuteChanged();
    }
}
