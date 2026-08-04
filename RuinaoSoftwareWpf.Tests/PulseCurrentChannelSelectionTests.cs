using Xunit;
using System.Windows;

namespace RuinaoSoftwareWpf.Tests;

public sealed class PulseCurrentChannelSelectionTests
{
    [Fact]
    public void ImpedanceDisplay_UsesTwoDecimalPlaces()
    {
        var channel = new PulseCurrentChannelConfig();

        channel.UpdateImpedance(520m);

        Assert.Equal("520.00", channel.ImpedanceOhm);
    }

    [Fact]
    public void Constructor_CreatesEightPairsAndSelectsOnlyFirstChannel()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(16, viewModel.Channels.Count);
        Assert.Equal(8, viewModel.ChannelPairs.Count);
        Assert.Same(viewModel.ChannelPairs[0], viewModel.SelectedChannelPair);
        Assert.Same(viewModel.Channels[0], viewModel.SelectedChannel);
        Assert.True(viewModel.Channels[0].IsSelected);
        Assert.False(viewModel.Channels[1].IsSelected);
        Assert.Equal(["CH 1", "CH 2"], viewModel.SelectedChannels.Select(channel => channel.Name));
        Assert.Equal(["CH 15", "CH 16"], viewModel.ChannelPairs[7].Channels.Select(channel => channel.Name));
        Assert.All(
            viewModel.Channels,
            channel => Assert.Equal(PulseCurrentPolarities.NotReversed, channel.Polarity));
    }

    [Fact]
    public void SelectChannelCommand_SelectsOnlyClickedChannelAndDisplaysItsPair()
    {
        var viewModel = CreateViewModel();
        var targetPair = viewModel.ChannelPairs[3];

        viewModel.SelectChannelCommand.Execute(targetPair.SecondChannel);

        Assert.Same(targetPair, viewModel.SelectedChannelPair);
        Assert.Same(targetPair.SecondChannel, viewModel.SelectedChannel);
        Assert.False(targetPair.FirstChannel.IsSelected);
        Assert.True(targetPair.SecondChannel.IsSelected);
        Assert.Equal(1, viewModel.Channels.Count(channel => channel.IsSelected));
        Assert.Equal(["CH 7", "CH 8"], viewModel.SelectedChannels.Select(channel => channel.Name));
    }

    [Fact]
    public void SwitchingPairs_PreservesEachChannelsIndependentParameters()
    {
        var viewModel = CreateViewModel();
        viewModel.Channels[0].CurrentMilliamp = "1";
        viewModel.Channels[2].CurrentMilliamp = "2";

        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[1].FirstChannel);
        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[0].FirstChannel);

        Assert.Equal("1", viewModel.SelectedChannels[0].CurrentMilliamp);
        Assert.Equal("2", viewModel.ChannelPairs[1].FirstChannel.CurrentMilliamp);
    }

    [Fact]
    public void SynchronizedStart_StartsAllSixteenChannels()
    {
        using var viewModel = CreateViewModel();
        ConfigureValidPulseParameters(viewModel.Channels);

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.All(viewModel.Channels, channel => Assert.True(channel.Waveform.IsRunning));
        Assert.All(viewModel.Channels, channel => Assert.True(channel.IsStimulating));

        viewModel.EmergencyStopCommand.Execute(null);
        Assert.All(viewModel.Channels, channel => Assert.False(channel.IsStimulating));
    }

    [Fact]
    public void SynchronizedStart_ShowsConciseConfirmationBeforeStarting()
    {
        var dialogs = new TestUserDialogService();
        using var viewModel = CreateViewModel(dialogs);
        ConfigureValidPulseParameters(viewModel.Channels);

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.Equal("同步开始确认", dialogs.LastConfirmationTitle);
        Assert.Contains("16个通道", dialogs.LastConfirmationMessage, StringComparison.Ordinal);
        Assert.Contains("经颅脉冲电流刺激", dialogs.LastConfirmationMessage, StringComparison.Ordinal);
        Assert.Null(dialogs.LastPulseCurrentStartConfirmation);
    }

    [Fact]
    public void SynchronizedStart_WhenAnyChannelIsInvalid_StartsNoChannels()
    {
        using var viewModel = CreateViewModel();
        ConfigureValidPulseParameters(viewModel.Channels);
        viewModel.Channels[15].CurrentMilliamp = string.Empty;

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.All(viewModel.Channels, channel => Assert.False(channel.Waveform.IsRunning));
        Assert.All(viewModel.Channels, channel => Assert.False(channel.IsStimulating));
    }

    [Fact]
    public void StartChannel_ChangesOnlyTargetIndicatorToRunning()
    {
        using var viewModel = CreateViewModel();
        var target = viewModel.Channels[0];
        ConfigureValidPulseParameters([target]);

        viewModel.StartChannelCommand.Execute(target);

        Assert.True(target.IsStimulating);
        Assert.All(viewModel.Channels.Skip(1), channel => Assert.False(channel.IsStimulating));

        viewModel.EmergencyStopCommand.Execute(null);
        Assert.False(target.IsStimulating);
    }

    [Fact]
    public void StartChannel_WhenConfirmationIsCancelled_DoesNotStart()
    {
        var dialogs = new TestUserDialogService { ConfirmationResult = false };
        using var viewModel = CreateViewModel(dialogs);
        var target = viewModel.Channels[0];
        ConfigureValidPulseParameters([target]);

        viewModel.StartChannelCommand.Execute(target);

        var request = Assert.IsType<PulseCurrentStartConfirmationRequest>(
            dialogs.LastPulseCurrentStartConfirmation);
        Assert.False(request.IsSynchronized);
        Assert.Single(request.Channels);
        Assert.False(target.IsStimulating);
    }

    [Fact]
    public void EmergencyStop_WhenConnectedWithoutRunningChannels_RemainsAvailable()
    {
        var engine = new CapturingPulseStimulationEngine();
        using var viewModel = CreateViewModel(stimulationEngine: engine);

        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));

        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal(1, engine.PulseEmergencyStopCount);
        Assert.Empty(engine.LastEmergencyStoppedChannels);
        Assert.False(viewModel.EmergencyStopCommand.CanExecute(null));
        Assert.False(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.All(viewModel.Channels, channel => Assert.False(channel.IsParameterEditingEnabled));
    }

    [Fact]
    public void StopChannelCommand_StopsOnlyTargetAndRestoresStartState()
    {
        using var viewModel = CreateViewModel();
        var first = viewModel.Channels[0];
        var second = viewModel.Channels[1];
        ConfigureValidPulseParameters([first, second]);

        viewModel.StartChannelCommand.Execute(first);
        viewModel.StartChannelCommand.Execute(second);

        Assert.True(viewModel.StopChannelCommand.CanExecute(first));
        viewModel.StopChannelCommand.Execute(first);

        Assert.False(first.IsStimulating);
        Assert.True(first.IsParameterEditingEnabled);
        Assert.Equal("00:00:00", first.RemainingTime);
        Assert.True(viewModel.StartChannelCommand.CanExecute(first));
        Assert.False(viewModel.StopChannelCommand.CanExecute(first));
        Assert.True(second.IsStimulating);
    }

    [Fact]
    public void TryApplyPrescription_AppliesPulseCurrentParametersToAllChannels()
    {
        var viewModel = CreateViewModel();
        viewModel.Channels[0].Polarity = PulseCurrentPolarities.Reversed;
        var prescription = new PrescriptionDefinition(
            Id: "tpcs",
            Name: "pulse",
            Indication: "tPCS测试",
            StimulationType: PrescriptionDefinition.PulseCurrentStimulationType,
            CurrentMilliamp: 2,
            DeliveryMode: PrescriptionDeliveryModes.Interval,
            TotalDurationMinutes: 0,
            IntervalMinutes: null,
            SessionDurationMinutes: null,
            Course: "10次",
            RampUpSeconds: 0,
            RampDownSeconds: 0,
            EvidenceGrade: "A级",
            IsBuiltin: false,
            PulseTreatmentDurationSeconds: 1200,
            PulseWidthMilliseconds: 10,
            PulseRiseWidthMilliseconds: 5,
            PulseIntervalWidthMilliseconds: 20);

        var applied = viewModel.TryApplyPrescription(prescription, out var error);

        Assert.True(applied, error);
        Assert.All(
            viewModel.Channels,
            channel =>
            {
            Assert.Equal("2.00", channel.CurrentMilliamp);
                Assert.Equal("1200.0", channel.TreatmentDurationSeconds);
                  Assert.Equal("10", channel.PulseWidthMilliseconds);
                  Assert.Equal("5", channel.RiseWidthMilliseconds);
                  Assert.Equal("20", channel.IntervalWidthMilliseconds);
                  Assert.Equal("00:00:00", channel.RemainingTime);
              });
        Assert.Equal(PulseCurrentPolarities.Reversed, viewModel.Channels[0].Polarity);
        Assert.All(
            viewModel.Channels.Skip(1),
            channel => Assert.Equal(PulseCurrentPolarities.NotReversed, channel.Polarity));
    }

    [Fact]
    public void PrescriptionCommands_DisableGlobalAndRunningTargetButAllowOtherChannel()
    {
        var viewModel = CreateViewModel();
        var running = viewModel.Channels[0];
        running.CurrentMilliamp = "2";
        running.PulseWidthMilliseconds = "10";
        running.RiseWidthMilliseconds = "5";
        running.IntervalWidthMilliseconds = "20";
        running.TreatmentDurationSeconds = "1200";

        viewModel.StartChannelCommand.Execute(running);

        Assert.False(viewModel.UsePrescriptionCommand.CanExecute(null));
        Assert.False(viewModel.UseChannelPrescriptionCommand.CanExecute(running));
        Assert.True(viewModel.UseChannelPrescriptionCommand.CanExecute(viewModel.Channels[1]));
    }

    [Fact]
    public void RealUsbDisconnect_StopsPulseSimulationAndWritesDeviceDisconnectedRecord()
    {
        var connection = new MutableHardwareConnectionState();
        connection.SetConnected(true);
        var records = new CapturingStimulationRecordService();
        using var viewModel = new PulseCurrentControlViewModel(
            connection,
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService(),
            new NoopLoggingService(),
            new TestUserDialogService(),
            records);
        var channel = viewModel.Channels[0];
        channel.UpdateImpedance(500m);
        ConfigureValidPulseParameters([channel]);

        viewModel.StartChannelCommand.Execute(channel);
        Assert.True(channel.IsStimulating);

        connection.SetConnected(false);

        Assert.False(channel.IsStimulating);
        Assert.Equal("00:00:00", channel.RemainingTime);
        var end = Assert.Single(records.Ends);
        Assert.Equal(StimulationEndReasonCodes.DeviceDisconnected, end.EndReasonCode);
        Assert.Equal(StimulationEndType.AbnormalTermination, end.EndType);
        Assert.False(viewModel.StartChannelCommand.CanExecute(channel));
    }

    private static PulseCurrentControlViewModel CreateViewModel(
        TestUserDialogService? dialogs = null,
        IStimulationEngine? stimulationEngine = null)
    {
        var viewModel = new PulseCurrentControlViewModel(
            new ConnectedHardwareState(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService(),
            new NoopLoggingService(),
            dialogs ?? new TestUserDialogService(),
            stimulationEngine: stimulationEngine);
        foreach (var channel in viewModel.Channels)
        {
            channel.UpdateImpedance(500m);
        }

        return viewModel;
    }

    private static void ConfigureValidPulseParameters(
        IEnumerable<PulseCurrentChannelConfig> channels)
    {
        foreach (var channel in channels)
        {
            channel.CurrentMilliamp = "2";
            channel.PulseWidthMilliseconds = "10";
            channel.RiseWidthMilliseconds = "5";
            channel.IntervalWidthMilliseconds = "20";
            channel.TreatmentDurationSeconds = "1200";
        }
    }

    private sealed class ConnectedHardwareState : IHardwareConnectionState
    {
        public event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged
        {
            add { }
            remove { }
        }

        public bool IsConnected => true;
    }

    private sealed class MutableHardwareConnectionState : IHardwareConnectionState
    {
        public event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged;

        public bool IsConnected { get; private set; }

        public void SetConnected(bool isConnected)
        {
            IsConnected = isConnected;
            ConnectionChanged?.Invoke(
                this,
                new HardwareConnectionChangedEventArgs(
                    isConnected,
                    false,
                    isConnected
                        ? HardwareConnectionChangeReason.Connected
                        : HardwareConnectionChangeReason.Disconnected,
                    isConnected ? "仪器已联机。" : "仪器未联机。"));
        }
    }

    private sealed class CapturingStimulationRecordService : IStimulationRecordService
    {
        public List<StimulationChannelsEndRequest> Ends { get; } = [];

        public Task<string> StartRunAsync(
            StimulationRunStartRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("run");

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

    private sealed class CapturingPulseStimulationEngine : IStimulationEngine
    {
        public int PulseEmergencyStopCount { get; private set; }

        public IReadOnlyList<string> LastEmergencyStoppedChannels { get; private set; } = [];

        public StimulationExecutionState CurrentState => StimulationExecutionState.Idle;

        public Task<HardwareOperationResult> StartTiGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string prescriptionName,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> StartDirectCurrentGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string prescriptionName,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> StopGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> EmergencyStopTiGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> EmergencyStopDirectCurrentGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> EmergencyStopPulseCurrentGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default)
        {
            PulseEmergencyStopCount++;
            LastEmergencyStoppedChannels = group.Channels.Select(channel => channel.Name).ToArray();
            return Task.FromResult(new HardwareOperationResult(true, "test"));
        }

        public Task<HardwareOperationResult> CompleteGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default) => NotUsed();

        private static Task<HardwareOperationResult> NotUsed() =>
            throw new InvalidOperationException("Pulse-current tests must not execute this operation.");
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

    private sealed class NoopToastService : IToastService
    {
        public Visibility Visibility => Visibility.Collapsed;
        public string Title => string.Empty;
        public string Message => string.Empty;
        public string Icon => string.Empty;
        public string Accent => string.Empty;
        public void Show(ToastKind kind, string title, string message, TimeSpan? duration = null) { }
        public void ShowInformation(string message, string title = "提示") { }
        public void ShowSuccess(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}
