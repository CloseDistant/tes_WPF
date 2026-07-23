using System.Diagnostics;
using RuinaoTesProtocol.V14;

namespace RuinaoTesHardware;

/// <summary>
/// tES背板的业务入口。
/// 上层WPF只调用本类，不直接依赖libusbK，也不自行拼装协议字节。
/// </summary>
public sealed class BackplaneClient : IAsyncDisposable
{
    private readonly IUsbBackplaneDiscovery discovery;
    private readonly IBackplaneTransport transport;
    private readonly TesV14ProtocolApi protocolApi = new();
    private readonly object protocolLock = new();

    public BackplaneConnectionState State { get; private set; } = BackplaneConnectionState.Disconnected;
    public UsbBackplaneDevice? Device { get; private set; }

    public event EventHandler<HardwareLogEntry>? Log;
    public event EventHandler<BackplaneConnectionState>? StateChanged;
    public event EventHandler<UsbWriteCompletedEventArgs>? RawFrameSent;
    public event EventHandler<UsbFrameReceivedEventArgs>? RawFrameReceived;

    /// <summary>下一条命令将使用的发送序号，仅供工程师诊断显示。</summary>
    public ushort NextSequenceForDiagnostics
    {
        get
        {
            lock (protocolLock)
            {
                return protocolApi.NextSequenceForDiagnostics;
            }
        }
    }

    public BackplaneClient(IUsbBackplaneDiscovery discovery, IBackplaneTransport transport)
    {
        this.discovery = discovery;
        this.transport = transport;

        // libusbK传输层在UsbK_WritePipe完整成功后回报TX_OK。
        // 这条日志和组帧时的TX_BUILD含义不同，即使随后等待ACK超时也会保留下来。
        if (transport is IBackplaneTransferDiagnostics diagnostics)
        {
            diagnostics.WriteCompleted += Transport_WriteCompleted;
            diagnostics.FrameReceived += Transport_FrameReceived;
        }
    }

    /// <summary>设置下一发送序号，用于实机验证65535到1的循环。</summary>
    public void SetNextSequenceForDiagnostics(ushort sequence)
    {
        lock (protocolLock)
        {
            protocolApi.SetNextSequenceForDiagnostics(sequence);
        }
    }

    /// <summary>取得传输层只读诊断快照；不执行USB读写。</summary>
    public UsbTransportDiagnosticSnapshot? GetTransportDiagnosticSnapshot() =>
        transport is IBackplaneTransferDiagnostics diagnostics
            ? diagnostics.GetSnapshot()
            : null;

    public async Task<UsbBackplaneDevice?> RefreshDeviceAsync(CancellationToken cancellationToken = default)
    {
        // 这里只查询Windows当前是否枚举到目标VID/PID，同时检查驱动是否就绪；不会打开USB端点。
        Device = await discovery.FindAsync(cancellationToken);
        WriteLog("DEVICE", Device is null
            ? "未发现tES背板（VID_04B4&PID_00F1）。"
            : $"发现{Device.Description}：{Device.InstanceId}；{Device.DriverStatus}。");
        return Device;
    }

    public async Task ConnectAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (State == BackplaneConnectionState.Connected)
        {
            return;
        }

        MoveTo(BackplaneConnectionState.Connecting);
        try
        {
            // 第一步：通过Windows设备管理接口找到04B4:00F1，并确认绑定的是libusbK驱动。
            var device = await RefreshDeviceAsync(cancellationToken)
                ?? throw new BackplaneConnectionException("未发现tES背板，请检查USB连接和供电。");
            if (!device.DriverReady)
            {
                throw new BackplaneConnectionException(
                    $"发现背板，但{device.DriverStatus}。请先安装tES libusbK驱动。");
            }

            // 第二步：打开libusbK设备句柄并寻找Bulk OUT/Bulk IN端点。
            // 到这里仅说明USB通道可用，还没有证明硬件能识别tES V1.4协议。
            await transport.OpenAsync(device, options.Timeout, cancellationToken);
            MoveTo(BackplaneConnectionState.Connected);
            WriteLog("LINK", "usbtest兼容链路已打开且后台接收循环已就绪：libusbK/WinUsb API，OUT=0x01，IN=0x81。握手成功后才代表协议联机成功。");
        }
        catch
        {
            MoveTo(BackplaneConnectionState.Faulted);
            throw;
        }
    }

    public async Task<BackplaneHandshakeResult> HandshakeAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!transport.IsOpen)
        {
            throw new BackplaneConnectionException("请先执行联机并成功打开libusbK链路。");
        }

        MoveTo(BackplaneConnectionState.Handshaking);
        try
        {
            // 第一步：按V1.4大端序生成握手帧。默认完全复现usbtest：版本0x01、控制域0x0000。
            byte[] request;
            ushort requestSequence;
            lock (protocolLock)
            {
                // 协议对象在整个客户端生命周期中复用，序列号与usbtest一样持续递增，
                // 避免每次握手都从seq=1开始而被硬件当成重复请求。
                protocolApi.ProtocolVersion = options.ProtocolVersion;
                protocolApi.DestinationAddress = TesV14ProtocolConstants.BackplaneAddress;
                request = protocolApi.BuildBackplaneHandshake(
                    out requestSequence,
                    options.HandshakeAckRequired);
            }
            WriteLog(
                "TX_BUILD",
                $"V1.4/usbtest握手帧已生成：seq={requestSequence} version=0x{options.ProtocolVersion:X2} "
                    + $"ackFlag={(options.HandshakeAckRequired ? 1 : 0)} bytes={request.Length}",
                request);

            // 第二步：硬件DLL将请求写到Bulk OUT，然后阻塞等待Bulk IN返回一帧。
            var stopwatch = Stopwatch.StartNew();
            var response = await transport.ExchangeAsync(request, cancellationToken);
            stopwatch.Stop();
            WriteLog("RX", $"HANDSHAKE response bytes={response.Length}", response);

            // 第三步：先验证帧头、帧尾、声明长度和CRC，再提取命令、地址、序列号等字段。
            if (!TesV14ProtocolCodec.TryParseFrame(response, out var frame, out var error) || frame is null)
            {
                throw new BackplaneConnectionException($"握手回复帧解析失败：{error}");
            }

            // usbtest只显示回包，未定义严格成功条件。共享DLL接受ACK或从机握手回复，拒绝无关命令。
            if (frame.Command is not (TesV14Command.Acknowledgement or TesV14Command.Handshake))
            {
                throw new BackplaneConnectionException(
                    $"握手期望ACK(0x01)或握手回复(0x00)，实际命令为0x{(byte)frame.Command:X2}。");
            }

            // 兼容usbtest未设置ACK控制位的情况：硬件若填写应答序列则必须匹配；填0时暂时接受并记录。
            if (frame.AckSequence != 0 && frame.AckSequence != requestSequence)
            {
                throw new BackplaneConnectionException(
                    $"ACK序列不匹配：expected={requestSequence}, actual={frame.AckSequence}。");
            }

            // 第六步：回复方向必须是背板F1 -> 主机F0，避免接受来源错误的帧。
            if (frame.SourceAddress != TesV14ProtocolConstants.BackplaneAddress
                || frame.DestinationAddress != TesV14ProtocolConstants.HostAddress)
            {
                throw new BackplaneConnectionException(
                    $"ACK地址错误：source=0x{frame.SourceAddress:X2}, destination=0x{frame.DestinationAddress:X2}。");
            }

            // 上述所有检查均通过，才向上层报告“握手成功”。
            MoveTo(BackplaneConnectionState.Connected);
            WriteLog(
                "DECISION",
                $"背板握手成功：command=0x{(byte)frame.Command:X2} ackSeq={frame.AckSequence} "
                    + $"耗时={stopwatch.Elapsed.TotalMilliseconds:F1}ms version=0x{frame.Version:X2}。");
            return new BackplaneHandshakeResult(
                requestSequence,
                stopwatch.Elapsed,
                frame.Version,
                request,
                response,
                (byte)frame.Command,
                frame.AckSequence);
        }
        catch
        {
            // 握手失败不关闭已经打开的USB句柄，状态退回“USB已联机”，方便工程师直接重试。
            MoveTo(BackplaneConnectionState.Connected);
            throw;
        }
    }

    /// <summary>读取一个或多个V1.4普通寄存器。</summary>
    public async Task<BackplaneRegisterOperationResult> ReadRegistersAsync(
        byte targetAddress,
        IReadOnlyList<ushort> addresses,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        byte[] request;
        ushort requestSequence;
        lock (protocolLock)
        {
            protocolApi.ProtocolVersion = options.ProtocolVersion;
            protocolApi.DestinationAddress = targetAddress;
            request = protocolApi.BuildReadRegisters(addresses, out requestSequence);
        }

        var result = await ExchangeRegistersAsync(
            request, requestSequence, targetAddress, false, cancellationToken);
        if (result.Registers.Count != addresses.Count)
        {
            throw new BackplaneConnectionException(
                $"读取回复数量不一致：expected={addresses.Count}, actual={result.Registers.Count}。");
        }

        Dictionary<ushort, TesV14RegisterValue> byAddress;
        try
        {
            byAddress = result.Registers.ToDictionary(item => item.Address);
        }
        catch (ArgumentException exception)
        {
            throw new BackplaneConnectionException("读取回复包含重复的寄存器地址。", exception);
        }

        var ordered = new TesV14RegisterValue[addresses.Count];
        for (var index = 0; index < addresses.Count; index++)
        {
            if (!byAddress.TryGetValue(addresses[index], out ordered[index]))
            {
                throw new BackplaneConnectionException($"读取回复缺少寄存器0x{addresses[index]:X4}。");
            }
        }

        return result with { Registers = ordered };
    }

    /// <summary>写入一个或多个V1.4普通寄存器。</summary>
    public async Task<BackplaneRegisterOperationResult> WriteRegistersAsync(
        byte targetAddress,
        IReadOnlyList<TesV14RegisterValue> registers,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registers);
        byte[] request;
        ushort requestSequence;
        lock (protocolLock)
        {
            protocolApi.ProtocolVersion = options.ProtocolVersion;
            protocolApi.DestinationAddress = targetAddress;
            request = protocolApi.BuildWriteRegisters(registers, out requestSequence);
        }

        return await ExchangeRegistersAsync(
            request, requestSequence, targetAddress, true, cancellationToken, registers);
    }

    /// <summary>按16组或32组布局读取一个字符串组，内部每批固定读取8个寄存器。</summary>
    public async Task<BackplaneProductInfoTextResult> ReadProductInfoTextAsync(
        TesV14ProductInfoGrouping grouping,
        int groupIndex,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var addresses = TesV14ProductInfoTextCodec.GetAddresses(grouping, groupIndex);
        var registers = new List<TesV14RegisterValue>(addresses.Count);
        var batches = new List<BackplaneRegisterOperationResult>();
        var stopwatch = Stopwatch.StartNew();
        for (var offset = 0; offset < addresses.Count; offset += TesV14ProductInfoTextCodec.ReadBatchSize)
        {
            var batchAddresses = addresses
                .Skip(offset)
                .Take(TesV14ProductInfoTextCodec.ReadBatchSize)
                .ToArray();
            var batch = await ReadRegistersAsync(
                TesV14ProtocolConstants.BackplaneAddress,
                batchAddresses,
                options,
                cancellationToken);
            batches.Add(batch);
            registers.AddRange(batch.Registers);
        }
        stopwatch.Stop();

        var rawBytes = TesV14ProductInfoTextCodec.CombineRegisterBytes(grouping, groupIndex, registers);
        WriteLog("TEXT_RX_RAW", $"背板字符串组{groupIndex}完整原始内容：bytes={rawBytes.Length}。", rawBytes);
        var text = TesV14ProductInfoTextCodec.Decode(grouping, groupIndex, registers);
        var byteCount = TesV14ProductInfoTextCodec.GetUtf8ByteCount(text);
        var startAddress = TesV14ProductInfoTextCodec.GetGroupStartAddress(grouping, groupIndex);
        var endAddress = TesV14ProductInfoTextCodec.GetGroupEndAddress(grouping, groupIndex);
        WriteLog("TEXT_READ", $"背板字符串组{groupIndex}读取成功：layout={(int)grouping}组 "
            + $"range=0x{startAddress:X4}～0x{endAddress:X4} UTF-8 bytes={byteCount} batches={batches.Count}。");
        return new BackplaneProductInfoTextResult(
            grouping, groupIndex, startAddress, endAddress, text, byteCount, stopwatch.Elapsed, batches);
    }

    /// <summary>
    /// 按16组或32组布局写入一个字符串组。字符串内容和0结束符必须处于同一请求中，
    /// 避免结束符跨批后固件不应答；只发送实际占用的寄存器，旧尾部由结束符隔离。
    /// </summary>
    public async Task<BackplaneProductInfoTextWriteResult> WriteProductInfoTextAsync(
        TesV14ProductInfoGrouping grouping,
        int groupIndex,
        string text,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var registers = TesV14ProductInfoTextCodec.Encode(grouping, groupIndex, text);
        var expectedRawBytes = TesV14ProductInfoTextCodec.CombineRegisterBytes(grouping, groupIndex, registers);
        WriteLog("TEXT_TX_RAW", $"背板字符串组{groupIndex}预期写入内容：bytes={expectedRawBytes.Length}。", expectedRawBytes);
        var batches = new List<BackplaneRegisterOperationResult>(1);
        var stopwatch = Stopwatch.StartNew();
        var requiredRegisterCount = TesV14ProductInfoTextCodec.GetRequiredRegisterCount(text);
        var usedRegisters = registers.Take(requiredRegisterCount).ToArray();
        try
        {
            batches.Add(await WriteRegistersAsync(
                TesV14ProtocolConstants.BackplaneAddress,
                usedRegisters,
                options,
                cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BackplaneConnectionException(
                $"背板字符串组{groupIndex}写入失败：0x{usedRegisters[0].Address:X4}～0x{usedRegisters[^1].Address:X4}，"
                    + $"一次写入{usedRegisters.Length}个寄存器（含字符串结束符）。",
                exception);
        }
        stopwatch.Stop();

        var startAddress = TesV14ProductInfoTextCodec.GetGroupStartAddress(grouping, groupIndex);
        var endAddress = TesV14ProductInfoTextCodec.GetGroupEndAddress(grouping, groupIndex);
        var byteCount = TesV14ProductInfoTextCodec.GetUtf8ByteCount(text);
        WriteLog("TEXT_WRITE", $"背板字符串组{groupIndex}分批写入完成：layout={(int)grouping}组 "
            + $"range=0x{usedRegisters[0].Address:X4}～0x{usedRegisters[^1].Address:X4} "
            + $"UTF-8 bytes={byteCount} registers={usedRegisters.Length} requests={batches.Count}。"
            + "必须回读一致后才算成功。");
        return new BackplaneProductInfoTextWriteResult(
            grouping, groupIndex, startAddress, endAddress, byteCount, stopwatch.Elapsed, batches);
    }

    public Task<BackplaneProductInfoTextResult> ReadProductInfoText16Async(
        int groupIndex, BackplaneConnectionOptions options, CancellationToken cancellationToken = default) =>
        ReadProductInfoTextAsync(TesV14ProductInfoGrouping.Groups16, groupIndex, options, cancellationToken);

    public Task<BackplaneProductInfoTextResult> ReadProductInfoText32Async(
        int groupIndex, BackplaneConnectionOptions options, CancellationToken cancellationToken = default) =>
        ReadProductInfoTextAsync(TesV14ProductInfoGrouping.Groups32, groupIndex, options, cancellationToken);

    public Task<BackplaneProductInfoTextWriteResult> WriteProductInfoText16Async(
        int groupIndex, string text, BackplaneConnectionOptions options, CancellationToken cancellationToken = default) =>
        WriteProductInfoTextAsync(TesV14ProductInfoGrouping.Groups16, groupIndex, text, options, cancellationToken);

    public Task<BackplaneProductInfoTextWriteResult> WriteProductInfoText32Async(
        int groupIndex, string text, BackplaneConnectionOptions options, CancellationToken cancellationToken = default) =>
        WriteProductInfoTextAsync(TesV14ProductInfoGrouping.Groups32, groupIndex, text, options, cancellationToken);

    private async Task<BackplaneRegisterOperationResult> ExchangeRegistersAsync(
        byte[] request,
        ushort requestSequence,
        byte targetAddress,
        bool isWrite,
        CancellationToken cancellationToken,
        IReadOnlyList<TesV14RegisterValue>? requestedValues = null)
    {
        if (!transport.IsOpen)
        {
            throw new BackplaneConnectionException("请先联机并完成背板握手，再执行寄存器操作。");
        }

        var operation = isWrite ? "写寄存器" : "读寄存器";
        WriteLog(isWrite ? "REG_WRITE" : "REG_READ",
            $"{operation}帧已生成：target=0x{targetAddress:X2} seq={requestSequence} bytes={request.Length}", request);

        var stopwatch = Stopwatch.StartNew();
        var response = await transport.ExchangeAsync(request, cancellationToken);
        stopwatch.Stop();
        WriteLog("RX", $"{operation} response bytes={response.Length}", response);

        if (!TesV14ProtocolCodec.TryParseFrame(response, out var frame, out var error) || frame is null)
        {
            throw new BackplaneConnectionException($"{operation}回复帧解析失败：{error}");
        }

        var validResponseCommand = isWrite
            ? frame.Command is TesV14Command.Response or TesV14Command.Acknowledgement
            : frame.Command == TesV14Command.Response;
        if (!validResponseCommand)
        {
            throw new BackplaneConnectionException(
                isWrite
                    ? $"{operation}期望RESPONSE(0x04)或ACK(0x01)，实际为0x{(byte)frame.Command:X2}。"
                    : $"{operation}期望包含数据的RESPONSE(0x04)，实际为0x{(byte)frame.Command:X2}。");
        }

        if (frame.AckSequence != 0 && frame.AckSequence != requestSequence)
        {
            throw new BackplaneConnectionException(
                $"{operation}ACK序列不匹配：expected={requestSequence}, actual={frame.AckSequence}。");
        }

        if (frame.SourceAddress != targetAddress
            || frame.DestinationAddress != TesV14ProtocolConstants.HostAddress)
        {
            throw new BackplaneConnectionException(
                $"{operation}回复地址错误：source=0x{frame.SourceAddress:X2}, destination=0x{frame.DestinationAddress:X2}。");
        }

        IReadOnlyList<TesV14RegisterValue> registers;
        if (frame.Payload.Length == 0 && isWrite && frame.Command == TesV14Command.Acknowledgement)
        {
            // 有些固件写成功只返回空ACK；保留请求值用于界面显示，但不冒充“回读验证”。
            registers = requestedValues ?? Array.Empty<TesV14RegisterValue>();
        }
        else if (!TesV14RegisterPayloadCodec.TryDecode(frame.Payload, out registers, out error))
        {
            throw new BackplaneConnectionException($"{operation}寄存器内容解析失败：{error}");
        }

        WriteLog("DECISION",
            $"{operation}回复有效：target=0x{targetAddress:X2} count={registers.Count} "
                + $"ackSeq={frame.AckSequence} 耗时={stopwatch.Elapsed.TotalMilliseconds:F1}ms。");
        return new BackplaneRegisterOperationResult(
            requestSequence,
            stopwatch.Elapsed,
            targetAddress,
            isWrite,
            registers,
            request,
            response,
            (byte)frame.Command,
            frame.AckSequence);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await transport.CloseAsync(cancellationToken);
        MoveTo(BackplaneConnectionState.Disconnected);
        WriteLog("LINK", "libusbK链路已关闭。");
    }

    public async ValueTask DisposeAsync()
    {
        if (transport is IBackplaneTransferDiagnostics diagnostics)
        {
            diagnostics.WriteCompleted -= Transport_WriteCompleted;
            diagnostics.FrameReceived -= Transport_FrameReceived;
        }

        await transport.DisposeAsync();
    }

    private void Transport_WriteCompleted(object? sender, UsbWriteCompletedEventArgs entry)
    {
        WriteLog(
            "TX_OK",
            $"USB完整写入成功：bytes={entry.BytesWritten}。",
            entry.Frame);
        try
        {
            RawFrameSent?.Invoke(this, entry);
        }
        catch
        {
            // 工程师界面的诊断订阅异常不能影响USB发送链路。
        }
    }

    private void Transport_FrameReceived(object? sender, UsbFrameReceivedEventArgs entry)
    {
        try
        {
            RawFrameReceived?.Invoke(this, entry);
        }
        catch
        {
            // 工程师界面的诊断订阅异常不能终止USB后台接收线程。
        }

        if (entry.IntermediateAcknowledgement)
        {
            WriteLog(
                "RX_ACK",
                $"读取请求已被硬件受理：sendSeq={entry.SendSequence} ackSeq={entry.AckSequence}；继续等待0x04数据响应。",
                entry.Frame);
            return;
        }

        WriteLog(
            entry.MatchedRequest ? "RX_MATCH" : "RX_LATE",
            entry.MatchedRequest
                ? $"收到并匹配本次回复：sendSeq={entry.SendSequence} ackSeq={entry.AckSequence} bytes={entry.Frame.Length}。"
                : $"收到未匹配帧（可能是迟到回复或主动上报）：sendSeq={entry.SendSequence} ackSeq={entry.AckSequence} bytes={entry.Frame.Length}。",
            entry.Frame);
    }

    private void MoveTo(BackplaneConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void WriteLog(string category, string message, byte[]? bytes = null)
    {
        Log?.Invoke(this, new HardwareLogEntry(DateTimeOffset.Now, category, message, bytes));
    }
}
