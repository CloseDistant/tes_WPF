namespace RuinaoSoftwareWpf;

using System.IO;
using System.Text.Json;

/// <summary>
/// 保存工作站上已经成功打开过的摄像头能力档案。文件只包含设备能力，
/// 不包含患者、评估或音视频数据。
/// </summary>
internal sealed class JsonCameraCaptureProfileStore : ICameraCaptureProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object syncRoot = new();
    private readonly ILoggingService logger;
    private readonly string storagePath;

    public JsonCameraCaptureProfileStore(ILoggingService logger)
        : this(
            logger,
            Path.Combine(
                Path.GetDirectoryName(AppDatabasePathProvider.MainDatabasePath)!,
                "camera-capabilities.json"))
    {
    }

    internal JsonCameraCaptureProfileStore(ILoggingService logger, string storagePath)
    {
        this.logger = logger;
        this.storagePath = storagePath;
    }

    public CameraOpeningPreference? Find(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            return null;
        }

        lock (syncRoot)
        {
            return LoadCore()
                .FirstOrDefault(item => string.Equals(
                    item.DeviceKey,
                    deviceKey,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Save(CameraOpeningPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        lock (syncRoot)
        {
            try
            {
                var items = LoadCore();
                items.RemoveAll(item => string.Equals(
                    item.DeviceKey,
                    preference.DeviceKey,
                    StringComparison.OrdinalIgnoreCase));
                items.Add(preference);

                var directory = Path.GetDirectoryName(storagePath)
                    ?? throw new InvalidOperationException("摄像头能力档案路径无效。");
                Directory.CreateDirectory(directory);
                var temporaryPath = storagePath + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(items, JsonOptions));
                File.Move(temporaryPath, storagePath, overwrite: true);
            }
            catch (Exception exception)
            {
                logger.Warning($"保存摄像头能力档案失败：{exception.Message}");
            }
        }
    }

    private List<CameraOpeningPreference> LoadCore()
    {
        try
        {
            if (!File.Exists(storagePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<CameraOpeningPreference>>(
                File.ReadAllText(storagePath),
                JsonOptions) ?? [];
        }
        catch (Exception exception)
        {
            logger.Warning($"读取摄像头能力档案失败，将重新建立：{exception.Message}");
            return [];
        }
    }
}
