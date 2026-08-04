namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.Features.Exhibition.Services;
using Xunit;

public sealed class ExhibitionHardwareServiceTests
{
    [Fact]
    public void ModeState_ExhibitionBuild_IsExplicitlyEnabled()
    {
        var state = new ExhibitionModeState();

        Assert.True(state.IsEnabled);
    }

    [Fact]
    public async Task ConnectionAndHandshake_AreDelegatedToRealHardwareBoundary()
    {
        var inner = new CapturingHardwareService();
        using var service = CreateService(inner);

        await service.ConnectAsync(TestContext.Current.CancellationToken);
        await service.HandshakeAsync(TestContext.Current.CancellationToken);

        Assert.True(service.IsConnected);
        Assert.Equal(1, inner.ConnectCount);
        Assert.Equal(1, inner.HandshakeCount);
    }

    [Fact]
    public async Task StimulationOperations_AreRecordedWithoutCallingInnerStimulationMethods()
    {
        var inner = new CapturingHardwareService();
        inner.SetConnected(true);
        var records = new CapturingStimulationRecordService();
        using var service = CreateService(inner, records);
        var group = CreateGroup();

        var cancellationToken = TestContext.Current.CancellationToken;
        var start = await service.StartGroupAsync(group, "CH 1 + CH 2", CreatePrescription(), cancellationToken);
        var stop = await service.StopGroupAsync(group, "CH 1 + CH 2", "tDCS", cancellationToken);
        var emergencyStop = await service.EmergencyStopGroupAsync(group, "用户点击急停", "tDCS", cancellationToken);
        var complete = await service.CompleteGroupAsync(group, "CH 1 + CH 2", "tDCS", cancellationToken);

        Assert.Contains("tDCS 运行中", start.FooterStatus, StringComparison.Ordinal);
        Assert.Contains("tDCS 已停止", stop.FooterStatus, StringComparison.Ordinal);
        Assert.Contains("tDCS 已急停", emergencyStop.FooterStatus, StringComparison.Ordinal);
        Assert.Contains("tDCS 已完成", complete.FooterStatus, StringComparison.Ordinal);
        Assert.All(
            new[] { start, stop, emergencyStop, complete },
            result =>
            {
                Assert.DoesNotContain("展览", result.FooterStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("模拟", result.FooterStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("模拟", result.UserMessage ?? string.Empty, StringComparison.Ordinal);
            });
        Assert.Equal(0, inner.StartGroupCount);
        Assert.Equal(0, inner.StopGroupCount);
        Assert.Equal(0, inner.EmergencyStopGroupCount);
        Assert.Equal(0, inner.CompleteGroupCount);
        Assert.Single(records.Starts);
        Assert.Equal(3, records.Ends.Count);
    }

    [Fact]
    public async Task ImpedanceMonitoring_ProvidesSixteenNormalChannelsWithoutRealRegisterRead()
    {
        var inner = new CapturingHardwareService();
        inner.SetConnected(true);
        using var service = CreateService(inner);

        service.SetStimulationImpedanceMonitoringEnabled(true);
        var first = Assert.IsType<StimulationImpedanceSnapshot>(service.CurrentStimulationImpedance);
        await service.CheckImpedanceAsync(TestContext.Current.CancellationToken);
        var second = Assert.IsType<StimulationImpedanceSnapshot>(service.CurrentStimulationImpedance);

        Assert.Equal(16, first.Channels.Count);
        Assert.Equal(500.18m, first.Channels[0].ImpedanceOhms);
        Assert.Equal(799.63m, first.Channels[15].ImpedanceOhms);
        Assert.Equal(500.67m, second.Channels[0].ImpedanceOhms);
        Assert.All(
            first.Channels,
            channel => Assert.NotEqual(
                decimal.Truncate(channel.ImpedanceOhms!.Value),
                channel.ImpedanceOhms.Value));
        Assert.All(
            first.Channels.Zip(second.Channels),
            pair => Assert.NotEqual(pair.First.ImpedanceOhms, pair.Second.ImpedanceOhms));
        Assert.All(
            second.Channels,
            channel => Assert.Equal(
                StimulationImpedanceStatus.Normal,
                StimulationImpedancePresentation.GetStatus(channel.ImpedanceOhms)));
        Assert.Equal(0, inner.CheckImpedanceCount);
        Assert.Equal(0, inner.SetImpedanceMonitoringCount);
    }

    [Fact]
    public void HardwareDisconnect_ClearsSimulatedImpedance()
    {
        var inner = new CapturingHardwareService();
        inner.SetConnected(true);
        using var service = CreateService(inner);
        service.SetStimulationImpedanceMonitoringEnabled(true);
        Assert.NotNull(service.CurrentStimulationImpedance);

        inner.SetConnected(false);

        Assert.Null(service.CurrentStimulationImpedance);
    }

    [Fact]
    public async Task HiddenTestConnection_EnablesSixteenChannelsWithoutCallingRealHardware()
    {
        var inner = new CapturingHardwareService();
        var localConnection = new DebugHardwareSimulationService();
        using var service = CreateService(inner, localConnection: localConnection);
        service.SetStimulationImpedanceMonitoringEnabled(true);

        var result = localConnection.Connect(realHardwareConnected: false);
        var start = await service.StartGroupAsync(
            CreateGroup(),
            "CH 1 + CH 2",
            CreatePrescription(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(service.IsConnected);
        Assert.Equal(16, Assert.IsType<StimulationImpedanceSnapshot>(service.CurrentStimulationImpedance).Channels.Count);
        Assert.Contains("tDCS 运行中", start.FooterStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("模拟", start.FooterStatus, StringComparison.Ordinal);
        Assert.Equal(0, inner.ConnectCount);
        Assert.Equal(0, inner.StartGroupCount);
    }

    private static ExhibitionHardwareService CreateService(
        CapturingHardwareService inner,
        CapturingStimulationRecordService? records = null,
        IDebugHardwareSimulationService? localConnection = null) =>
        new(
            inner,
            records ?? new CapturingStimulationRecordService(),
            new NoopLoggingService(),
            new ExhibitionModeState(),
            localConnection);

    private static TiGroup CreateGroup()
    {
        var group = new TiGroup { Title = "tDCS展览模拟" };
        group.Channels.Add(new ChannelConfig { Name = "CH 1", CurrentMA = "1", DurationS = "60" });
        group.Channels.Add(new ChannelConfig { Name = "CH 2", CurrentMA = "1", DurationS = "60" });
        return group;
    }

    private static PrescriptionDefinition CreatePrescription() => new(
        "exhibition",
        "展览模拟",
        "展览",
        "tDCS",
        1,
        PrescriptionDeliveryModes.Continuous,
        1,
        null,
        null,
        "CH1+CH2",
        0,
        0,
        "展览",
        false);

    private sealed class CapturingHardwareService : IHardwareService
    {
        public event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged;
        public event EventHandler<DeviceTopologyChangedEventArgs>? DeviceTopologyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<StimulationImpedanceChangedEventArgs>? StimulationImpedanceChanged
        {
            add { }
            remove { }
        }

        public int ConnectCount { get; private set; }
        public int HandshakeCount { get; private set; }
        public int CheckImpedanceCount { get; private set; }
        public int SetImpedanceMonitoringCount { get; private set; }
        public int StartGroupCount { get; private set; }
        public int StopGroupCount { get; private set; }
        public int EmergencyStopGroupCount { get; private set; }
        public int CompleteGroupCount { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsConnecting => false;
        public DeviceTopologySnapshot? CurrentDeviceTopology => null;
        public StimulationImpedanceSnapshot? CurrentStimulationImpedance => null;

        public Task<HardwareOperationResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            SetConnected(true);
            return Success();
        }

        public Task<HardwareOperationResult> HandshakeAsync(CancellationToken cancellationToken = default)
        {
            HandshakeCount++;
            return Success();
        }

        public Task<HardwareOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            SetConnected(false);
            return Success();
        }

        public Task<HardwareOperationResult> ReadProductModelAsync(CancellationToken cancellationToken = default) => Success();

        public Task<HardwareOperationResult> ReadBoardModelAsync(CancellationToken cancellationToken = default) => Success();

        public Task<HardwareOperationResult> CheckImpedanceAsync(CancellationToken cancellationToken = default)
        {
            CheckImpedanceCount++;
            return Success();
        }

        public void SetStimulationImpedanceMonitoringEnabled(bool enabled)
        {
            SetImpedanceMonitoringCount++;
        }

        public Task<DeviceTopologySnapshot> RefreshDeviceTopologyAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HardwareOperationResult> StartGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            PrescriptionDefinition parameterRecord,
            CancellationToken cancellationToken = default)
        {
            StartGroupCount++;
            return Success();
        }

        public Task<HardwareOperationResult> StopGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default)
        {
            StopGroupCount++;
            return Success();
        }

        public Task<HardwareOperationResult> EmergencyStopGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType = "TI",
            CancellationToken cancellationToken = default)
        {
            EmergencyStopGroupCount++;
            return Success();
        }

        public Task<HardwareOperationResult> CompleteGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default)
        {
            CompleteGroupCount++;
            return Success();
        }

        public Task ShutdownAsync() => Task.CompletedTask;

        public void SetConnected(bool connected)
        {
            IsConnected = connected;
            ConnectionChanged?.Invoke(
                this,
                new HardwareConnectionChangedEventArgs(
                    connected,
                    false,
                    connected
                        ? HardwareConnectionChangeReason.Connected
                        : HardwareConnectionChangeReason.Disconnected,
                    connected ? "仪器已联机。" : "仪器未联机。"));
        }

        private Task<HardwareOperationResult> Success() =>
            Task.FromResult(new HardwareOperationResult(IsConnected, "test"));
    }

    private sealed class CapturingStimulationRecordService : IStimulationRecordService
    {
        public List<StimulationRunStartRequest> Starts { get; } = [];
        public List<StimulationChannelsEndRequest> Ends { get; } = [];

        public Task<string> StartRunAsync(
            StimulationRunStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Starts.Add(request);
            return Task.FromResult("run");
        }

        public Task EndChannelsAsync(
            StimulationChannelsEndRequest request,
            CancellationToken cancellationToken = default)
        {
            Ends.Add(request);
            return Task.CompletedTask;
        }

        public Task<PageResult<StimulationTreatmentRecord>> GetTreatmentRecordsPageAsync(
            PageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<StimulationTreatmentRecord>([], false));
    }

    private sealed class NoopLoggingService : ILoggingService
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
