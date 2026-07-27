using System.Windows;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class DirectCurrentChannelSelectionTests
{
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
    }

    [Fact]
    public void SelectChannelCommand_SelectsOnlyClickedChannelAndDisplaysItsPair()
    {
        var viewModel = CreateViewModel();
        var targetPair = viewModel.ChannelPairs[4];
        var targetChannel = targetPair.SecondChannel;

        viewModel.SelectChannelCommand.Execute(targetChannel);

        Assert.Same(targetPair, viewModel.SelectedChannelPair);
        Assert.Same(targetChannel, viewModel.SelectedChannel);
        Assert.False(targetPair.FirstChannel.IsSelected);
        Assert.True(targetPair.SecondChannel.IsSelected);
        Assert.Equal(1, viewModel.Channels.Count(channel => channel.IsSelected));
        Assert.Equal(["CH 9", "CH 10"], viewModel.SelectedChannels.Select(channel => channel.Name));
    }

    [Fact]
    public void SwitchingPairs_PreservesEachChannelsIndependentParameters()
    {
        var viewModel = CreateViewModel();
        viewModel.Channels[0].CurrentMA = "1.0";
        viewModel.Channels[2].CurrentMA = "1.5";

        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[1].FirstChannel);
        viewModel.SelectChannelCommand.Execute(viewModel.ChannelPairs[0].FirstChannel);

        Assert.Equal("1.0", viewModel.SelectedChannels[0].CurrentMA);
        Assert.Equal("1.5", viewModel.ChannelPairs[1].FirstChannel.CurrentMA);
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

    private static DirectCurrentControlViewModel CreateViewModel()
    {
        return new DirectCurrentControlViewModel(
            new NoopStimulationEngine(),
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
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> CompleteGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default) => NotUsed();

        private static Task<HardwareOperationResult> NotUsed() =>
            throw new InvalidOperationException("Selection tests must not execute stimulation commands.");
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
