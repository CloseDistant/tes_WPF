namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;

public sealed class LocalCameraRecordingQualitySettingsService : ICameraRecordingQualitySettingsService
{
    private const string SettingKey = "camera_recording_quality_mode";

    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAccountService accountService;
    private readonly IAuthorizationService authorizationService;
    private readonly ILoggingService logger;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private volatile bool initialized;
    private CameraRecordingQualityMode selectedMode = CameraRecordingQualityMode.Balanced;

    public LocalCameraRecordingQualitySettingsService(
        IAppDatabaseInitializer databaseInitializer,
        IAccountService accountService,
        IAuthorizationService authorizationService,
        ILoggingService logger,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator)
    {
        this.databaseInitializer = databaseInitializer;
        this.accountService = accountService;
        this.authorizationService = authorizationService;
        this.logger = logger;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
    }

    public CameraRecordingQualityMode SelectedMode => selectedMode;

    public CameraCaptureProfile SelectedProfile => CameraCaptureProfile.ForMode(selectedMode);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var storedValue = await context.AppStates
                .AsNoTracking()
                .Where(item => item.Key == SettingKey)
                .Select(item => item.Value)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            selectedMode = Enum.TryParse<CameraRecordingQualityMode>(storedValue, true, out var parsed)
                && CameraRecordingQualityCatalog.All.Contains(parsed)
                    ? parsed
                    : CameraRecordingQualityMode.Balanced;
            initialized = true;
            logger.Info(
                $"摄像头录像质量设置已加载：mode={selectedMode}, "
                + $"profile={CameraRecordingQualityCatalog.Specification(selectedMode)}");
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task SaveAsync(
        CameraRecordingQualityMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!CameraRecordingQualityCatalog.All.Contains(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var currentUser = authorizationService.Demand(AppPermission.ManageCameraSettings);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var state = await context.AppStates.FirstOrDefaultAsync(
                item => item.Key == SettingKey,
                cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                state = new AppStateEntity { Key = SettingKey };
                context.AppStates.Add(state);
            }

            state.Value = mode.ToString();
            state.UpdatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            await databaseWriteCoordinator.ExecuteAsync(
                AppDatabasePathProvider.MainDatabasePath,
                () => context.SaveChangesAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            selectedMode = mode;

            await accountService.RecordAuditAsync(
                currentUser?.UserId,
                null,
                "update_camera_recording_quality",
                "success",
                $"正式录像质量：{mode}，{CameraRecordingQualityCatalog.Specification(mode)}",
                cancellationToken).ConfigureAwait(false);
            logger.Info(
                $"摄像头录像质量设置已更新：operator={currentUser?.UserId.ToString() ?? "none"}, "
                + $"mode={mode}, profile={CameraRecordingQualityCatalog.Specification(mode)}");
        }
        finally
        {
            writeGate.Release();
        }
    }
}
