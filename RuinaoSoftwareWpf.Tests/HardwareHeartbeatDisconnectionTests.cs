using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class HardwareHeartbeatDisconnectionTests
{
    [Fact]
    public async Task HeartbeatTimeout_DisconnectsAndAllowsManualReconnectWithNewUsbSession()
    {
        var transport = new HeartbeatTimeoutTransport();
        await using var backplaneClient = new BackplaneClient(new AvailableDeviceDiscovery(), transport);
        var hardwareClient = new TesHardwareDeviceClient(backplaneClient);
        var service = new HardwareService(
            new RuinaoTesHardwareBridge(hardwareClient, new NullLoggingService()),
            new NullLoggingService(),
            new RecordingDeviceStateMachine(),
            new NullAuditLogService(),
            new NullStimulationRecordService(),
            new DebugHardwareSimulationService());
        var heartbeatLost = new TaskCompletionSource<HardwareConnectionChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.ConnectionChanged += (_, entry) =>
        {
            if (entry.Reason == HardwareConnectionChangeReason.HeartbeatLost)
            {
                heartbeatLost.TrySetResult(entry);
            }
        };

        _ = await service.ConnectAsync(TestContext.Current.CancellationToken);
        var lostEntry = await heartbeatLost.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.False(lostEntry.IsConnected);
        Assert.False(service.IsConnected);
        Assert.Null(service.CurrentDeviceTopology);
        Assert.Null(service.CurrentStimulationImpedance);
        Assert.False(transport.IsOpen);
        Assert.Equal(1, transport.CloseCount);
        var exchangeCountAfterDisconnect = transport.ExchangeCount;
        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        Assert.Equal(exchangeCountAfterDisconnect, transport.ExchangeCount);

        _ = await service.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.True(service.IsConnected);
        Assert.True(transport.IsOpen);
        Assert.Equal(2, transport.OpenCount);
        await service.ShutdownAsync();
    }

    private sealed class AvailableDeviceDiscovery : IUsbBackplaneDiscovery
    {
        private static readonly UsbBackplaneDevice Device = new(
            "USB\\VID_04B4&PID_00F1\\TEST",
            "tES",
            "Ruinao",
            "libusbK",
            0,
            true);

        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(Device);
    }

    private sealed class HeartbeatTimeoutTransport : IBackplaneTransport
    {
        private int exchangeCountInCurrentSession;

        public bool IsOpen { get; private set; }
        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public int ExchangeCount { get; private set; }

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsOpen = true;
            OpenCount++;
            exchangeCountInCurrentSession = 0;
            return Task.CompletedTask;
        }

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExchangeCount++;
            exchangeCountInCurrentSession++;
            if (OpenCount == 1 && exchangeCountInCurrentSession == 4)
            {
                throw new TimeoutException("测试：心跳未收到匹配回复。");
            }

            Assert.True(
                TesV14ProtocolCodec.TryParseFrame(request.Span, out var requestFrame, out var error),
                error);
            Assert.NotNull(requestFrame);
            return Task.FromResult(requestFrame.Command == TesV14Command.Handshake
                ? BuildHandshakeResponse(requestFrame)
                : BuildRegisterResponse(requestFrame));
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsOpen)
            {
                CloseCount++;
            }

            IsOpen = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        private static byte[] BuildHandshakeResponse(TesV14Frame request) =>
            TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Handshake,
                request.DestinationAddress,
                request.SourceAddress,
                1,
                request.SendSequence,
                [],
                request.Version);

        private static byte[] BuildRegisterResponse(TesV14Frame request)
        {
            Assert.True(
                TesV14RegisterPayloadCodec.TryDecode(request.Payload, out var registers, out var error),
                error);
            var responseRegisters = registers
                .Select(register => new TesV14RegisterValue(register.Address, 0))
                .ToArray();
            return TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                request.DestinationAddress,
                request.SourceAddress,
                1,
                request.SendSequence,
                TesV14RegisterPayloadCodec.Encode(responseRegisters),
                request.Version);
        }
    }

    private sealed class RecordingDeviceStateMachine : IDeviceStateMachine
    {
        public DeviceConnectionState CurrentState { get; private set; } = DeviceConnectionState.Disconnected;
        public event EventHandler<StateTransition<DeviceConnectionState>>? StateChanged;

        public void MoveTo(DeviceConnectionState nextState, string trigger, string operatorId = "system")
        {
            var transition = new StateTransition<DeviceConnectionState>(
                CurrentState,
                nextState,
                trigger,
                DateTimeOffset.Now,
                operatorId);
            CurrentState = nextState;
            StateChanged?.Invoke(this, transition);
        }
    }

    private sealed class NullAuditLogService : IAuditLogService
    {
        public void RecordStateTransition<TState>(StateTransition<TState> transition) { }
        public void RecordUserAction(string action, string operatorId = "system") { }
        public void RecordHardwareCommunication(string direction, string command, string details) { }
        public void RecordSafetyEvent(SafetyEvaluationResult result, string operatorId = "system") { }
    }

    private sealed class NullStimulationRecordService : IStimulationRecordService
    {
        public Task<string> StartRunAsync(
            StimulationRunStartRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        public Task EndChannelsAsync(
            StimulationChannelsEndRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PageResult<StimulationTreatmentRecord>> GetTreatmentRecordsPageAsync(
            PageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<StimulationTreatmentRecord>([], false, 0));
    }

    private sealed class NullLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }
}
