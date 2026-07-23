using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using RuinaoTesProtocol.V14;

namespace RuinaoTesHardware;

/// <summary>
/// 按厂商usbtest的实际链路实现：libusbK驱动、libusbK.dll导出的WinUsb兼容API、
/// Bulk OUT 0x01发送、Bulk IN 0x81接收。
/// </summary>
public sealed class UsbTestCompatibleBackplaneTransport : IBackplaneTransport, IBackplaneTransferDiagnostics
{
    private readonly SemaphoreSlim exchangeGate = new(1, 1);
    private readonly object pendingLock = new();
    private readonly List<byte> receiveBuffer = new();
    private SafeFileHandle? deviceHandle;
    private IntPtr interfaceHandle;
    private uint timeoutMilliseconds;
    private byte[]? lastWrittenFrame;
    private CancellationTokenSource? receiveCancellation;
    private Task? receiveTask;
    private TaskCompletionSource<bool>? receiveLoopReady;
    private PendingExchange? pendingExchange;
    private long transmittedFrameCount;
    private long receivedFrameCount;
    private long receivedByteCount;
    private long matchedFrameCount;
    private long unmatchedFrameCount;
    private long intermediateAcknowledgementCount;
    private long invalidFrameCount;
    private long exchangeTimeoutCount;
    private int bufferedByteCount;
    private long lastTransmitUtcTicks;
    private long lastReceiveUtcTicks;

    public bool IsOpen => deviceHandle is { IsInvalid: false, IsClosed: false }
        && interfaceHandle != IntPtr.Zero;

    public byte[]? LastWrittenFrame => lastWrittenFrame?.ToArray();
    public event EventHandler<UsbWriteCompletedEventArgs>? WriteCompleted;
    public event EventHandler<UsbFrameReceivedEventArgs>? FrameReceived;

    public UsbTransportDiagnosticSnapshot GetSnapshot()
    {
        ushort? pendingSequence;
        lock (pendingLock)
        {
            pendingSequence = pendingExchange?.SendSequence;
        }

        return new UsbTransportDiagnosticSnapshot(
            IsOpen,
            receiveTask is { IsCompleted: false },
            BulkOutEndpoint,
            BulkInEndpoint,
            Interlocked.Read(ref transmittedFrameCount),
            Interlocked.Read(ref receivedFrameCount),
            Interlocked.Read(ref receivedByteCount),
            Interlocked.Read(ref matchedFrameCount),
            Interlocked.Read(ref unmatchedFrameCount),
            Interlocked.Read(ref intermediateAcknowledgementCount),
            Interlocked.Read(ref invalidFrameCount),
            Interlocked.Read(ref exchangeTimeoutCount),
            pendingSequence,
            Volatile.Read(ref bufferedByteCount),
            ReadTimestamp(ref lastTransmitUtcTicks),
            ReadTimestamp(ref lastReceiveUtcTicks));
    }

    public async Task OpenAsync(
        UsbBackplaneDevice device,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOpen)
        {
            return;
        }

        if (!device.DriverReady)
        {
            throw new BackplaneConnectionException(device.DriverStatus);
        }

        if (!string.Equals(device.DriverService, TesUsbIdentity.DriverService, StringComparison.OrdinalIgnoreCase))
        {
            throw new BackplaneConnectionException(
                $"tES当前绑定的驱动服务是{device.DriverService ?? "未知"}，usbtest兼容链路需要libusbK驱动。");
        }

        try
        {
            await Task.Run(() => OpenCore(timeout), cancellationToken);

            // 必须等后台Bulk IN循环真正进入运行状态后，才允许上层发送第一条握手。
            // 软件启动阶段线程池较忙，不能仅凭“任务已创建”就认为接收端已经就绪。
            var ready = receiveLoopReady
                ?? throw new BackplaneConnectionException("USB后台接收循环未创建。");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (DllNotFoundException exception)
        {
            throw new BackplaneConnectionException("未找到libusbK.dll。", exception);
        }
        catch (BadImageFormatException exception)
        {
            throw new BackplaneConnectionException("libusbK.dll位数不匹配，当前工程师软件需要64位DLL。", exception);
        }
        catch
        {
            // 如果接收线程未能按时就绪或打开过程被取消，不能遗留半打开的USB句柄。
            await CloseCoreAsync();
            throw;
        }
    }

    public async Task<byte[]> ExchangeAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            throw new BackplaneConnectionException("usbtest兼容USB链路尚未打开。");
        }

        await exchangeGate.WaitAsync(cancellationToken);
        try
        {
            var requestBytes = request.ToArray();
            var pending = CreatePendingExchange(requestBytes);
            RegisterPending(pending);
            try
            {
                await Task.Run(() => WriteCore(requestBytes), cancellationToken);
                try
                {
                    return await pending.Completion.Task.WaitAsync(
                        TimeSpan.FromMilliseconds(timeoutMilliseconds),
                        cancellationToken);
                }
                catch (TimeoutException)
                {
                    Interlocked.Increment(ref exchangeTimeoutCount);
                    throw new TimeoutException(
                        $"Endpoint 0x01已发送成功，但在{timeoutMilliseconds}ms内未收到ackSeq={pending.SendSequence}的匹配回复。");
                }
            }
            finally
            {
                ClearPending(pending);
            }
        }
        finally
        {
            exchangeGate.Release();
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await CloseCoreAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        exchangeGate.Dispose();
    }

    private void OpenCore(TimeSpan timeout)
    {
        var path = FindDevicePath(TesUsbIdentity.VendorId, TesUsbIdentity.ProductId)
            ?? throw new BackplaneConnectionException(
                $"WinUSB兼容接口中未发现tES（VID_{TesUsbIdentity.VendorId:X4}&PID_{TesUsbIdentity.ProductId:X4}）。");

        var rawHandle = CreateFile(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);
        if (rawHandle == InvalidHandleValue)
        {
            ThrowLastWin32("CreateFile");
        }

        var openedDeviceHandle = new SafeFileHandle(rawHandle, ownsHandle: true);
        if (!WinUsbInitialize(rawHandle, out var openedInterfaceHandle))
        {
            var error = Marshal.GetLastWin32Error();
            openedDeviceHandle.Dispose();
            throw new BackplaneConnectionException(
                $"libusbK WinUsb_Initialize失败：{new Win32Exception(error).Message}（{error}）。");
        }

        try
        {
            timeoutMilliseconds = checked((uint)Math.Clamp(timeout.TotalMilliseconds, 100, 60_000));
            // 后台接收循环使用较短的驱动读取超时，以便断联时能及时退出；
            // 一次请求的真正应答期限仍由timeoutMilliseconds统一控制。
            var receivePollTimeout = Math.Min(timeoutMilliseconds, 250u);
            SetPipeTimeout(openedInterfaceHandle, BulkInEndpoint, receivePollTimeout);
            SetPipeTimeout(openedInterfaceHandle, BulkOutEndpoint, timeoutMilliseconds);

            deviceHandle = openedDeviceHandle;
            interfaceHandle = openedInterfaceHandle;
            receiveCancellation = new CancellationTokenSource();
            var loopReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            receiveLoopReady = loopReady;
            receiveTask = Task.Factory.StartNew(
                () => ReceiveLoop(openedInterfaceHandle, receiveCancellation.Token, loopReady),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        catch
        {
            WinUsbFree(openedInterfaceHandle);
            openedDeviceHandle.Dispose();
            throw;
        }
    }

    private void WriteCore(byte[] request)
    {
        // 与usbtest的SendProtocolFrame一致，协议帧固定发到Endpoint 0x01。
        if (!WinUsbWritePipe(
                interfaceHandle,
                BulkOutEndpoint,
                request,
                (uint)request.Length,
                out var written,
                IntPtr.Zero))
        {
            ThrowLastWin32("libusbK WinUsb_WritePipe(0x01)");
        }

        if (written != request.Length)
        {
            throw new BackplaneConnectionException(
                $"USB写入不完整：expected={request.Length}, actual={written}。");
        }

        lastWrittenFrame = request.ToArray();
        Interlocked.Increment(ref transmittedFrameCount);
        Interlocked.Exchange(ref lastTransmitUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        WriteCompleted?.Invoke(
            this,
            new UsbWriteCompletedEventArgs(DateTimeOffset.Now, lastWrittenFrame.ToArray(), checked((int)written)));
    }

    private void ReceiveLoop(
        IntPtr openedInterfaceHandle,
        CancellationToken cancellationToken,
        TaskCompletionSource<bool> ready)
    {
        // 到达这里才代表专用接收线程已经启动，OpenAsync可以安全返回。
        ready.TrySetResult(true);
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!WinUsbReadPipe(
                    openedInterfaceHandle,
                    BulkInEndpoint,
                    buffer,
                    (uint)buffer.Length,
                    out var read,
                    IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorSemTimeout)
                {
                    continue;
                }

                if (cancellationToken.IsCancellationRequested || error == ErrorOperationAborted)
                {
                    break;
                }

                CompletePendingWithException(new BackplaneConnectionException(
                    $"libusbK WinUsb_ReadPipe(0x81)失败：{new Win32Exception(error).Message}（{error}）。"));
                break;
            }

            if (read == 0)
            {
                // USB零长度包不代表断联，也不代表请求失败。继续等待到请求总超时。
                Thread.Sleep(1);
                continue;
            }

            Interlocked.Add(ref receivedByteCount, read);
            AppendAndDispatchFrames(buffer.AsSpan(0, checked((int)read)));
        }
    }

    private void AppendAndDispatchFrames(ReadOnlySpan<byte> chunk)
    {
        for (var index = 0; index < chunk.Length; index++)
        {
            receiveBuffer.Add(chunk[index]);
        }
        Volatile.Write(ref bufferedByteCount, receiveBuffer.Count);

        while (true)
        {
            var markerIndex = FindFrameMarker(receiveBuffer);
            if (markerIndex < 0)
            {
                // 保留末尾单独的0xA5，它可能是下一次USB读取中帧头的第一个字节。
                var keepTrailingMarkerByte = receiveBuffer.Count > 0 && receiveBuffer[^1] == 0xA5;
                receiveBuffer.Clear();
                if (keepTrailingMarkerByte)
                {
                    receiveBuffer.Add(0xA5);
                }

                Volatile.Write(ref bufferedByteCount, receiveBuffer.Count);

                return;
            }

            if (markerIndex > 0)
            {
                receiveBuffer.RemoveRange(0, markerIndex);
                Volatile.Write(ref bufferedByteCount, receiveBuffer.Count);
            }

            if (receiveBuffer.Count < TesV14ProtocolConstants.HeaderLength)
            {
                return;
            }

            var payloadLength = (receiveBuffer[16] << 8) | receiveBuffer[17];
            var frameLength = TesV14ProtocolConstants.HeaderLength
                + payloadLength
                + TesV14ProtocolConstants.CrcLength
                + TesV14ProtocolConstants.FooterLength;
            if (receiveBuffer.Count < frameLength)
            {
                return;
            }

            var candidate = receiveBuffer.GetRange(0, frameLength).ToArray();
            if (!TesV14ProtocolCodec.TryParseFrame(candidate, out var frame, out _) || frame is null)
            {
                // 当前A5 5A不是有效帧头或帧已损坏，右移一字节重新同步。
                receiveBuffer.RemoveAt(0);
                Interlocked.Increment(ref invalidFrameCount);
                Volatile.Write(ref bufferedByteCount, receiveBuffer.Count);
                continue;
            }

            receiveBuffer.RemoveRange(0, frameLength);
            Volatile.Write(ref bufferedByteCount, receiveBuffer.Count);
            DispatchFrame(frame, candidate);
        }
    }

    private void DispatchFrame(TesV14Frame frame, byte[] frameBytes)
    {
        PendingExchange? pending;
        var matched = false;
        var intermediateAcknowledgement = false;
        lock (pendingLock)
        {
            pending = pendingExchange;
            if (pending is not null
                && !pending.Completion.Task.IsCompleted
                && frame.SourceAddress == pending.ExpectedSourceAddress
                && frame.DestinationAddress == pending.ExpectedDestinationAddress)
            {
                var terminalCommand = IsTerminalResponse(pending.RequestCommand, frame.Command);
                var sequenceMatches = frame.AckSequence == pending.SendSequence
                    || (frame.AckSequence == 0 && terminalCommand);
                if (terminalCommand && sequenceMatches)
                {
                    matched = pending.Completion.TrySetResult(frameBytes);
                }
                else if (pending.RequestCommand == TesV14Command.Read
                    && frame.Command == TesV14Command.Acknowledgement
                    && frame.AckSequence == pending.SendSequence)
                {
                    // 固件会先用ACK表示“读取请求已受理”，随后再返回包含寄存器内容的0x04响应。
                    // 这帧不能结束Exchange，否则4字节ACK状态会被误当成寄存器载荷。
                    intermediateAcknowledgement = true;
                }
            }
        }

        try
        {
            Interlocked.Increment(ref receivedFrameCount);
            Interlocked.Exchange(ref lastReceiveUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
            if (matched)
            {
                Interlocked.Increment(ref matchedFrameCount);
            }
            else if (intermediateAcknowledgement)
            {
                Interlocked.Increment(ref intermediateAcknowledgementCount);
            }
            else
            {
                Interlocked.Increment(ref unmatchedFrameCount);
            }

            FrameReceived?.Invoke(
                this,
                new UsbFrameReceivedEventArgs(
                    DateTimeOffset.Now,
                    frameBytes.ToArray(),
                    frame.SendSequence,
                    frame.AckSequence,
                    matched,
                    intermediateAcknowledgement));
        }
        catch
        {
            // 诊断日志订阅者异常不能终止USB后台接收循环。
        }
    }

    private static bool IsTerminalResponse(TesV14Command request, TesV14Command response) =>
        request switch
        {
            TesV14Command.Handshake => response is TesV14Command.Handshake or TesV14Command.Acknowledgement,
            TesV14Command.Read => response == TesV14Command.Response,
            TesV14Command.Write => response is TesV14Command.Response or TesV14Command.Acknowledgement,
            _ => response is TesV14Command.Response or TesV14Command.Acknowledgement,
        };

    private async Task CloseCoreAsync()
    {
        var openedInterfaceHandle = interfaceHandle;
        var cancellation = receiveCancellation;
        var runningReceiveTask = receiveTask;

        cancellation?.Cancel();
        if (openedInterfaceHandle != IntPtr.Zero)
        {
            _ = WinUsbAbortPipe(openedInterfaceHandle, BulkInEndpoint);
            _ = WinUsbAbortPipe(openedInterfaceHandle, BulkOutEndpoint);
        }

        if (runningReceiveTask is not null)
        {
            try
            {
                await runningReceiveTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                // AbortPipe后通常会立即退出；超时仍继续释放，避免关闭软件被永久阻塞。
            }
            catch
            {
                // 接收任务异常不应阻止USB句柄释放。
            }
        }

        CompletePendingWithException(new BackplaneConnectionException("USB链路已关闭。"));
        if (openedInterfaceHandle != IntPtr.Zero)
        {
            _ = WinUsbFree(openedInterfaceHandle);
        }

        interfaceHandle = IntPtr.Zero;
        deviceHandle?.Dispose();
        deviceHandle = null;
        lastWrittenFrame = null;
        receiveTask = null;
        receiveCancellation = null;
        receiveLoopReady = null;
        cancellation?.Dispose();
        receiveBuffer.Clear();
        Volatile.Write(ref bufferedByteCount, 0);
    }

    private static DateTimeOffset? ReadTimestamp(ref long utcTicks)
    {
        var ticks = Interlocked.Read(ref utcTicks);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static PendingExchange CreatePendingExchange(byte[] request)
    {
        if (!TesV14ProtocolCodec.TryParseFrame(request, out var frame, out var error) || frame is null)
        {
            throw new BackplaneConnectionException($"待发送V1.4帧无效：{error}");
        }

        return new PendingExchange(
            frame.SendSequence,
            frame.Command,
            frame.DestinationAddress,
            frame.SourceAddress,
            new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private void RegisterPending(PendingExchange pending)
    {
        lock (pendingLock)
        {
            if (pendingExchange is not null)
            {
                throw new InvalidOperationException("已有USB请求正在等待回复。");
            }

            pendingExchange = pending;
        }
    }

    private void ClearPending(PendingExchange pending)
    {
        lock (pendingLock)
        {
            if (ReferenceEquals(pendingExchange, pending))
            {
                pendingExchange = null;
            }
        }
    }

    private void CompletePendingWithException(Exception exception)
    {
        lock (pendingLock)
        {
            pendingExchange?.Completion.TrySetException(exception);
        }
    }

    private static int FindFrameMarker(List<byte> bytes)
    {
        for (var index = 0; index + 1 < bytes.Count; index++)
        {
            if (bytes[index] == 0xA5 && bytes[index + 1] == 0x5A)
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record PendingExchange(
        ushort SendSequence,
        TesV14Command RequestCommand,
        byte ExpectedSourceAddress,
        byte ExpectedDestinationAddress,
        TaskCompletionSource<byte[]> Completion);

    private static string? FindDevicePath(ushort vid, ushort pid)
    {
        var searchText = $"VID_{vid:X4}&PID_{pid:X4}";
        var interfaceGuid = UsbDeviceInterfaceGuid;
        var set = SetupDiGetClassDevs(
            ref interfaceGuid,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandleValue)
        {
            ThrowLastWin32("SetupDiGetClassDevs");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    CbSize = Marshal.SizeOf<SpDeviceInterfaceData>(),
                };

                if (!SetupDiEnumDeviceInterfaces(
                        set,
                        IntPtr.Zero,
                        ref interfaceGuid,
                        index,
                        ref interfaceData))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems)
                    {
                        return null;
                    }

                    ThrowLastWin32("SetupDiEnumDeviceInterfaces");
                }

                _ = SetupDiGetDeviceInterfaceDetail(
                    set,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    IntPtr.Zero);
                if (requiredSize == 0)
                {
                    continue;
                }

                var detail = Marshal.AllocHGlobal(checked((int)requiredSize));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(
                            set,
                            ref interfaceData,
                            detail,
                            requiredSize,
                            out _,
                            IntPtr.Zero))
                    {
                        ThrowLastWin32("SetupDiGetDeviceInterfaceDetail");
                    }

                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (!string.IsNullOrWhiteSpace(path)
                        && path.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static void SetPipeTimeout(IntPtr handle, byte endpoint, uint timeout)
    {
        if (!WinUsbSetPipePolicy(handle, endpoint, PipeTransferTimeout, sizeof(uint), ref timeout))
        {
            ThrowLastWin32($"libusbK WinUsb_SetPipePolicy(0x{endpoint:X2})");
        }
    }

    private static void ThrowLastWin32(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        throw new BackplaneConnectionException(
            $"{operation}失败：{new Win32Exception(error).Message}（{error}）。");
    }

    private const string LibUsbKLibrary = "libusbK.dll";
    private const byte BulkOutEndpoint = 0x01;
    private const byte BulkInEndpoint = 0x81;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint PipeTransferTimeout = 3;
    private const int ErrorSemTimeout = 121;
    private const int ErrorOperationAborted = 995;
    private const int ErrorNoMoreItems = 259;
    private static readonly IntPtr InvalidHandleValue = new(-1);
    private static readonly Guid UsbDeviceInterfaceGuid = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(LibUsbKLibrary, EntryPoint = "WinUsb_Initialize", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsbInitialize(IntPtr deviceHandle, out IntPtr interfaceHandle);

    [DllImport(LibUsbKLibrary, EntryPoint = "WinUsb_Free", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsbFree(IntPtr interfaceHandle);

    [DllImport(LibUsbKLibrary, EntryPoint = "WinUsb_SetPipePolicy", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsbSetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);

    [DllImport(LibUsbKLibrary, EntryPoint = "WinUsb_WritePipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsbWritePipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport(LibUsbKLibrary, EntryPoint = "WinUsb_ReadPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsbReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport(LibUsbKLibrary, EntryPoint = "WinUsb_AbortPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsbAbortPipe(IntPtr interfaceHandle, byte pipeId);
}
