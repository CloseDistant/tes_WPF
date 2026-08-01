using RuinaoTesHardware;

namespace RuinaoHardwareEngineer.Features.DeviceTopology;

/// <summary>
/// 工程师工具拓扑发现服务。
/// 先读取背板0x0900槽位位图，再只访问实际插板的业务板地址，避免空槽位逐个超时。
/// 当前硬件阶段只接入电刺激业务板，因此通信正常的在线板统一按电刺激板呈现。
/// </summary>
public sealed class EngineerDeviceTopologyService
{
    private const ushort BackplaneSlotBitmapAddress = 0x0900;
    private static readonly ushort[] IdentityAddresses = [0x0500, 0x0501, 0x0502, 0x0503];
    public static readonly TimeSpan MaximumProbeTimeout = TimeSpan.FromMilliseconds(500);
    private readonly BackplaneClient client;

    public EngineerDeviceTopologyService(BackplaneClient client)
    {
        this.client = client;
    }

    public async Task<IReadOnlyList<EngineerBoardSlot>> ScanAsync(
        BackplaneConnectionOptions options,
        IProgress<EngineerBoardSlot>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var probeOptions = CreateProbeOptions(options);
        var slotBitmapResult = await client.ReadRegistersAsync(
            RuinaoTesProtocol.V14.TesV14ProtocolConstants.BackplaneAddress,
            [BackplaneSlotBitmapAddress],
            options,
            cancellationToken);
        var slotBitmap = slotBitmapResult.Registers[0].Value;
        var slots = new List<EngineerBoardSlot>(8);
        for (byte address = 0; address < 8; address++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isInserted = (slotBitmap & (1U << address)) != 0;
            if (!isInserted)
            {
                var emptySlot = new EngineerBoardSlot(
                    address,
                    address,
                    false,
                    EngineerBoardKind.Unknown,
                    string.Empty,
                    Array.Empty<uint>(),
                    null,
                    $"背板0x0900位图=0x{slotBitmap:X8}；bit{address}=0，槽位未插板");
                slots.Add(emptySlot);
                progress?.Report(emptySlot);
                continue;
            }

            EngineerBoardSlot slot;
            try
            {
                var result = await client.ReadRegistersAsync(
                    address,
                    IdentityAddresses,
                    probeOptions,
                    cancellationToken);
                var values = result.Registers.Select(register => register.Value).ToArray();
                var identity = EngineerBoardIdentityClassifier.Classify(values);
                slot = new EngineerBoardSlot(
                    address,
                    address,
                    true,
                    EngineerBoardKind.Stimulation,
                    identity.Text,
                    values,
                    result.Elapsed,
                    identity.Kind == EngineerBoardKind.Stimulation
                        ? "背板报告已插板；业务板通信正常；身份文本识别为电刺激板"
                        : "背板报告已插板；业务板通信正常；当前硬件阶段按电刺激板处理")
                {
                    IsInserted = true,
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                slot = new EngineerBoardSlot(
                    address,
                    address,
                    false,
                    EngineerBoardKind.Unknown,
                    string.Empty,
                    Array.Empty<uint>(),
                    null,
                    $"背板报告已插板，但业务板地址0x{address:X2}未返回有效回复：{exception.Message}")
                {
                    IsInserted = true,
                };
            }

            slots.Add(slot);
            progress?.Report(slot);
        }

        return slots;
    }

    public static BackplaneConnectionOptions CreateProbeOptions(BackplaneConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with
        {
            Timeout = options.Timeout <= MaximumProbeTimeout
                ? options.Timeout
                : MaximumProbeTimeout,
        };
    }
}
