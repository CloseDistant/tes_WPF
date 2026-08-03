using System.Text;
using RuinaoTesProtocol.V14;

namespace RuinaoTesHardware;

/// <summary>
/// 产品软件和工程师软件共用的硬件业务入口。
/// 上层只调用业务方法，不接触协议帧、寄存器地址、USB 端点或 libusbK。
/// </summary>
public sealed class TesHardwareDeviceClient
{
    private const ushort BackplaneSlotBitmapAddress = 0x0900;
    private static readonly ushort[] BoardIdentityAddresses = [0x0500, 0x0501, 0x0502, 0x0503];
    private static readonly ushort[] StimulationImpedanceAddresses =
        Enumerable.Range(0x1001, 8)
            .Select(address => checked((ushort)address))
            .ToArray();
    private static readonly TimeSpan MaximumTopologyProbeTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InitialLinkStabilizationDelay = TimeSpan.FromMilliseconds(500);
    private static readonly BackplaneConnectionOptions ProbeHandshakeOptions = new(
        ProtocolVersion: 0x01,
        Timeout: TimeSpan.FromMilliseconds(500),
        HandshakeAckRequired: false);
    private static readonly BackplaneConnectionOptions DefaultOptions = new(
        ProtocolVersion: 0x01,
        Timeout: TimeSpan.FromSeconds(2),
        HandshakeAckRequired: false);

    private readonly BackplaneClient backplaneClient;

    public TesHardwareDeviceClient(BackplaneClient backplaneClient)
    {
        this.backplaneClient = backplaneClient;
    }

    public BackplaneConnectionState State => backplaneClient.State;

    public event EventHandler<HardwareLogEntry>? Log
    {
        add => backplaneClient.Log += value;
        remove => backplaneClient.Log -= value;
    }

    /// <summary>
    /// 打开 USB 链路，执行一次不作为联机依据的预热握手，再执行正式握手。
    /// 只有正式握手收到并校验有效回复后才返回成功。
    /// </summary>
    public async Task<BackplaneHandshakeResult> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        var newlyOpened = await EnsureUsbLinkOpenAsync(cancellationToken);
        if (newlyOpened)
        {
            await Task.Delay(InitialLinkStabilizationDelay, cancellationToken);
        }

        try
        {
            _ = await backplaneClient.HandshakeAsync(ProbeHandshakeOptions, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 预热帧只用于规避部分固件忽略首次序号的现象，不作为联机成败依据。
        }

        return await backplaneClient.HandshakeAsync(DefaultOptions, cancellationToken);
    }

    /// <summary>发送一次握手；必要时先打开 USB 链路并等待其稳定。</summary>
    public async Task<BackplaneHandshakeResult> HandshakeAsync(
        CancellationToken cancellationToken = default)
    {
        var newlyOpened = await EnsureUsbLinkOpenAsync(cancellationToken);
        if (newlyOpened)
        {
            await Task.Delay(InitialLinkStabilizationDelay, cancellationToken);
        }

        return await backplaneClient.HandshakeAsync(DefaultOptions, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.DisconnectAsync(cancellationToken);

    public async Task<bool> IsDeviceReadyAsync(CancellationToken cancellationToken = default)
    {
        var device = await backplaneClient.RefreshDeviceAsync(cancellationToken);
        return device?.DriverReady == true;
    }

    public Task<uint> ReadProductModelAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.ReadProductModelAsync(DefaultOptions, cancellationToken);

    public Task<uint> ReadBoardModelAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.ReadBoardModelAsync(DefaultOptions, cancellationToken);

    public Task<uint> ReadImpedanceAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.ReadImpedanceAsync(DefaultOptions, cancellationToken);

    /// <summary>
    /// 一次读取指定电刺激业务板的8个通道阻抗寄存器（0x1001～0x1008）。
    /// 下位机自行约每2秒更新寄存器；本方法只读取当前快照，不发送采集启停命令。
    /// </summary>
    public async Task<TesStimulationImpedanceSnapshot> ReadStimulationBoardImpedanceAsync(
        byte boardAddress,
        CancellationToken cancellationToken = default)
    {
        if (boardAddress > 0x07)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boardAddress),
                "电刺激业务板地址必须在0x00～0x07之间。");
        }

        var result = await backplaneClient.ReadRegistersAsync(
            boardAddress,
            StimulationImpedanceAddresses,
            DefaultOptions,
            cancellationToken);
        var channels = result.Registers
            .Select((register, index) => new TesStimulationImpedanceChannel(
                index + 1,
                register.Address,
                register.Value))
            .ToArray();

        return new TesStimulationImpedanceSnapshot(
            boardAddress,
            channels,
            result.Elapsed,
            DateTimeOffset.Now,
            result.RequestSequence);
    }

    /// <summary>
    /// 读取背板槽位位图，并且只探测背板报告为已插板的业务板地址。
    /// 空槽位不会发送业务板命令；单个已插板槽位最多等待500ms，避免拓扑扫描长期占用通信链路。
    /// </summary>
    public async Task<TesDeviceTopologySnapshot> ReadDeviceTopologyAsync(
        CancellationToken cancellationToken = default)
    {
        var bitmapResult = await backplaneClient.ReadRegistersAsync(
            TesV14ProtocolConstants.BackplaneAddress,
            [BackplaneSlotBitmapAddress],
            DefaultOptions,
            cancellationToken);
        var slotBitmap = bitmapResult.Registers[0].Value;
        var slots = new List<TesBusinessBoardSlot>(8);
        var probeOptions = DefaultOptions with { Timeout = MaximumTopologyProbeTimeout };

        for (byte address = 0; address < 8; address++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isInserted = (slotBitmap & (1U << address)) != 0;
            if (!isInserted)
            {
                slots.Add(new TesBusinessBoardSlot(
                    address,
                    address,
                    false,
                    false,
                    TesBusinessBoardKind.Unknown,
                    string.Empty,
                    Array.Empty<uint>(),
                    null,
                    $"背板0x0900位图=0x{slotBitmap:X8}，bit{address}=0，槽位未插板。"));
                continue;
            }

            try
            {
                var result = await backplaneClient.ReadRegistersAsync(
                    address,
                    BoardIdentityAddresses,
                    probeOptions,
                    cancellationToken);
                var values = result.Registers.Select(register => register.Value).ToArray();
                var identity = DecodeBoardIdentity(values);

                // 当前硬件阶段尚未提供稳定的板类型编码；按已确认策略，在线业务板暂按电刺激板使用。
                slots.Add(new TesBusinessBoardSlot(
                    address,
                    address,
                    true,
                    true,
                    TesBusinessBoardKind.Stimulation,
                    identity.Text,
                    values,
                    result.Elapsed,
                    identity.Kind == TesBusinessBoardKind.Stimulation
                        ? "背板报告已插板，业务板通信正常，身份文本识别为电刺激板。"
                        : "背板报告已插板，业务板通信正常；当前阶段暂按电刺激板处理。"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                slots.Add(new TesBusinessBoardSlot(
                    address,
                    address,
                    true,
                    false,
                    TesBusinessBoardKind.Unknown,
                    string.Empty,
                    Array.Empty<uint>(),
                    null,
                    $"背板报告已插板，但业务板地址0x{address:X2}未返回有效回复：{exception.Message}"));
            }
        }

        return new TesDeviceTopologySnapshot(slotBitmap, DateTimeOffset.Now, slots);
    }

    private async Task<bool> EnsureUsbLinkOpenAsync(CancellationToken cancellationToken)
    {
        if (backplaneClient.State is BackplaneConnectionState.Disconnected
            or BackplaneConnectionState.Faulted)
        {
            await backplaneClient.ConnectAsync(DefaultOptions, cancellationToken);
            return true;
        }

        return false;
    }

    private static (TesBusinessBoardKind Kind, string Text) DecodeBoardIdentity(
        IReadOnlyList<uint> values)
    {
        var bytes = new byte[values.Count * sizeof(uint)];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var offset = index * sizeof(uint);
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        var text = Encoding.UTF8.GetString(bytes).TrimEnd('\0', '\uFFFF', ' ');
        var normalized = text.ToUpperInvariant();
        var kind = normalized.Contains("EEG", StringComparison.Ordinal)
            ? TesBusinessBoardKind.Eeg
            : normalized.Contains("TES", StringComparison.Ordinal)
                || normalized.Contains("STIM", StringComparison.Ordinal)
                ? TesBusinessBoardKind.Stimulation
                : TesBusinessBoardKind.Unknown;

        return (kind, string.IsNullOrWhiteSpace(text) ? "未提供可识别文本" : text);
    }
}
