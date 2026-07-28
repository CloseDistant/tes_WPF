using Xunit;
using System.Windows;

namespace RuinaoSoftwareWpf.Tests;

public sealed class PulseCurrentChannelSelectionTests
{
    [Fact]
    public void Constructor_CreatesSinglePairAndSelectsOnlyFirstChannel()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(2, viewModel.Channels.Count);
        Assert.Single(viewModel.ChannelPairs);
        Assert.Same(viewModel.ChannelPairs[0], viewModel.SelectedChannelPair);
        Assert.Same(viewModel.Channels[0], viewModel.SelectedChannel);
        Assert.True(viewModel.Channels[0].IsSelected);
        Assert.False(viewModel.Channels[1].IsSelected);
        Assert.Equal(["CH 1", "CH 2"], viewModel.SelectedChannels.Select(channel => channel.Name));
        Assert.All(
            viewModel.Channels,
            channel => Assert.Equal(PulseCurrentPolarities.NotReversed, channel.Polarity));
    }

    [Fact]
    public void SelectChannelCommand_SelectsOnlyClickedChannelAndDisplaysItsPair()
    {
        var viewModel = CreateViewModel();
        var targetPair = viewModel.ChannelPairs[0];

        viewModel.SelectChannelCommand.Execute(targetPair.SecondChannel);

        Assert.Same(targetPair, viewModel.SelectedChannelPair);
        Assert.Same(targetPair.SecondChannel, viewModel.SelectedChannel);
        Assert.False(targetPair.FirstChannel.IsSelected);
        Assert.True(targetPair.SecondChannel.IsSelected);
        Assert.Equal(1, viewModel.Channels.Count(channel => channel.IsSelected));
        Assert.Equal(["CH 1", "CH 2"], viewModel.SelectedChannels.Select(channel => channel.Name));
    }

    [Fact]
    public void SwitchingChannels_PreservesEachChannelsIndependentParameters()
    {
        var viewModel = CreateViewModel();
        viewModel.Channels[0].CurrentMilliamp = "1";
        viewModel.Channels[1].CurrentMilliamp = "2";

        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[0].SecondChannel);
        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[0].FirstChannel);

        Assert.Equal("1", viewModel.SelectedChannels[0].CurrentMilliamp);
        Assert.Equal("2", viewModel.ChannelPairs[0].SecondChannel.CurrentMilliamp);
    }

    [Fact]
    public void SynchronizedStart_StartsBothChannels()
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
    public void SynchronizedStart_WhenAnyChannelIsInvalid_StartsNoChannels()
    {
        using var viewModel = CreateViewModel();
        ConfigureValidPulseParameters(viewModel.Channels);
        viewModel.Channels[1].CurrentMilliamp = string.Empty;

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
                Assert.Equal("2", channel.CurrentMilliamp);
                Assert.Equal("1200", channel.TreatmentDurationSeconds);
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

    private static PulseCurrentControlViewModel CreateViewModel()
    {
        return new PulseCurrentControlViewModel(
            new NoopStimulationEngine(),
            new ConnectedHardwareState(),
            new ConnectedDebugSimulation(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService(),
            new NoopLoggingService());
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

    private sealed class NoopStimulationEngine : IStimulationEngine
    {
        public StimulationExecutionState CurrentState => StimulationExecutionState.Idle;

        public Task<HardwareOperationResult> StartPulseCurrentAsync(
            IReadOnlyList<PulseCurrentExecutionChannel> channels,
            string selectedChannelNames,
            string prescriptionName,
            CancellationToken cancellationToken = default) => Success();

        public Task<HardwareOperationResult> EmergencyStopPulseCurrentAsync(
            string reason,
            CancellationToken cancellationToken = default) => Success();

        public Task<HardwareOperationResult> CompletePulseCurrentAsync(
            IReadOnlyList<int> logicalChannelNumbers,
            string selectedChannelNames,
            CancellationToken cancellationToken = default) => Success();

        public Task<HardwareOperationResult> StartTiGroupAsync(TiGroup group, string selectedChannelNames, string prescriptionName, CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> StartDirectCurrentGroupAsync(TiGroup group, string selectedChannelNames, string prescriptionName, CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> PauseTiGroupAsync(TiGroup group, string selectedChannelNames, CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> EmergencyStopTiGroupAsync(TiGroup group, string reason, CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> EmergencyStopDirectCurrentGroupAsync(TiGroup group, string reason, CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> CompleteGroupAsync(TiGroup group, string selectedChannelNames, string stimulationType, CancellationToken cancellationToken = default) => NotUsed();

        private static Task<HardwareOperationResult> Success() =>
            Task.FromResult(new HardwareOperationResult(true, "test"));

        private static Task<HardwareOperationResult> NotUsed() =>
            throw new InvalidOperationException("tPCS selection tests must not call other stimulation modes.");
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

    private sealed class ConnectedDebugSimulation : IDebugHardwareSimulationService
    {
        public event EventHandler? ConnectionChanged
        {
            add { }
            remove { }
        }

        public bool IsAvailable => true;

        public bool IsConnected => true;

        public DebugHardwareSimulationResult Connect(bool realHardwareConnected) =>
            new(true, "已连接");
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
