using System.Windows;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class DirectCurrentChannelSelectionTests
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
    }

    [Fact]
    public void SelectChannelCommand_SelectsOnlyClickedChannelAndDisplaysItsPair()
    {
        var viewModel = CreateViewModel();
        var targetPair = viewModel.ChannelPairs[0];
        var targetChannel = targetPair.SecondChannel;

        viewModel.SelectChannelCommand.Execute(targetChannel);

        Assert.Same(targetPair, viewModel.SelectedChannelPair);
        Assert.Same(targetChannel, viewModel.SelectedChannel);
        Assert.False(targetPair.FirstChannel.IsSelected);
        Assert.True(targetPair.SecondChannel.IsSelected);
        Assert.Equal(1, viewModel.Channels.Count(channel => channel.IsSelected));
        Assert.Equal(["CH 1", "CH 2"], viewModel.SelectedChannels.Select(channel => channel.Name));
    }

    [Fact]
    public void SwitchingChannels_PreservesEachChannelsIndependentParameters()
    {
        var viewModel = CreateViewModel();
        viewModel.Channels[0].CurrentMA = "1.0";
        viewModel.Channels[1].CurrentMA = "1.5";

        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[0].SecondChannel);
        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[0].FirstChannel);

        Assert.Equal("1.0", viewModel.SelectedChannels[0].CurrentMA);
        Assert.Equal("1.5", viewModel.ChannelPairs[0].SecondChannel.CurrentMA);
    }

    [Fact]
    public void SynchronizedStart_StartsBothChannelsAfterEveryChannelPassesValidation()
    {
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine);
        foreach (var channel in viewModel.Channels)
        {
            channel.CurrentMA = "1";
        }

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.NotNull(engine.LastStartedDirectCurrentGroup);
        Assert.Equal(2, engine.LastStartedDirectCurrentGroup.Channels.Count);
        Assert.All(viewModel.Channels, channel => Assert.True(channel.DirectCurrentWaveform.IsRunning));
        Assert.All(viewModel.Channels, channel => Assert.True(channel.IsStimulating));

        viewModel.EmergencyStopCommand.Execute(null);
        Assert.All(viewModel.Channels, channel => Assert.False(channel.IsStimulating));
    }

    [Fact]
    public void SynchronizedStart_WhenAnyChannelIsInvalid_StartsNoChannels()
    {
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine);
        foreach (var channel in viewModel.Channels)
        {
            channel.CurrentMA = "1";
        }

        viewModel.Channels[1].CurrentMA = string.Empty;

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.Null(engine.LastStartedDirectCurrentGroup);
        Assert.All(viewModel.Channels, channel => Assert.False(channel.DirectCurrentWaveform.IsRunning));
        Assert.All(viewModel.Channels, channel => Assert.False(channel.IsStimulating));
    }

    [Fact]
    public void StartChannel_ChangesOnlyTargetIndicatorToRunning()
    {
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine);
        var target = viewModel.Channels[0];
        target.CurrentMA = "1";

        viewModel.StartChannelCommand.Execute(target);

        Assert.True(target.IsStimulating);
        Assert.All(viewModel.Channels.Skip(1), channel => Assert.False(channel.IsStimulating));

        viewModel.EmergencyStopCommand.Execute(null);
        Assert.False(target.IsStimulating);
    }

    [Fact]
    public void ApplyPrescription_PreservesEachChannelsPolarity()
    {
        var viewModel = CreateViewModel();
        viewModel.Channels[0].Polarity = "调转";
        viewModel.Channels[1].Polarity = "不掉转";
        var prescription = CreateDirectCurrentPrescription() with
        {
            ChannelPolarities = ["不掉转", "调转"]
        };

        viewModel.ApplyPrescription(prescription);

        Assert.Equal("调转", viewModel.Channels[0].Polarity);
        Assert.Equal("不掉转", viewModel.Channels[1].Polarity);
        Assert.All(viewModel.Channels.Skip(2), channel => Assert.Equal("不掉转", channel.Polarity));
    }

    private static PrescriptionDefinition CreateDirectCurrentPrescription() => new(
        Id: "tdcs",
        Name: "test",
        Indication: "test",
        StimulationType: "tDCS",
        CurrentMilliamp: 2,
        DeliveryMode: PrescriptionDeliveryModes.Continuous,
        TotalDurationMinutes: 20,
        IntervalMinutes: null,
        SessionDurationMinutes: null,
        Course: string.Empty,
        RampUpSeconds: 30,
        RampDownSeconds: 30,
        EvidenceGrade: string.Empty,
        IsBuiltin: false);

    private static DirectCurrentControlViewModel CreateViewModel(
        NoopStimulationEngine? stimulationEngine = null)
    {
        return new DirectCurrentControlViewModel(
            stimulationEngine ?? new NoopStimulationEngine(),
            new ConnectedHardwareState(),
            new DebugHardwareSimulationService(),
            new NoopLoggingService(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService());
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
        public TiGroup? LastStartedDirectCurrentGroup { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            LastStartedDirectCurrentGroup = group;
            return Success();
        }

        public Task<HardwareOperationResult> StartPulseCurrentAsync(
            IReadOnlyList<PulseCurrentExecutionChannel> channels,
            string selectedChannelNames,
            string prescriptionName,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> PauseTiGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> EmergencyStopTiGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> EmergencyStopDirectCurrentGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default) => Success();

        public Task<HardwareOperationResult> EmergencyStopPulseCurrentAsync(
            string reason,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> CompleteGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> CompletePulseCurrentAsync(
            IReadOnlyList<int> logicalChannelNumbers,
            string selectedChannelNames,
            CancellationToken cancellationToken = default) => NotUsed();

        private static Task<HardwareOperationResult> NotUsed() =>
            throw new InvalidOperationException("Selection tests must not execute stimulation commands.");

        private static Task<HardwareOperationResult> Success() =>
            Task.FromResult(new HardwareOperationResult(true, "test"));
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
