using System.Windows;
using RuinaoSoftwareWpf.Features.Exhibition.Services;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class StimulationConnectionCommandTests
{
    [Fact]
    public void DirectCurrentStartCommands_FollowHardwareConnectionState()
    {
        var connection = new MutableHardwareConnectionState();
        var viewModel = new DirectCurrentControlViewModel(
            new NoopStimulationEngine(),
            connection,
            new DebugHardwareSimulationService(),
            new NoopLoggingService(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService(),
            new TestUserDialogService());

        Assert.False(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.False(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));

        connection.SetConnected(true);

        Assert.False(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.False(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));

        foreach (var channel in viewModel.Channels)
        {
            channel.UpdateImpedance(500m);
        }

        Assert.True(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.True(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));
    }

    [Fact]
    public void TiStartCommands_FollowHardwareConnectionState()
    {
        var connection = new MutableHardwareConnectionState();
        var viewModel = new TiControlViewModel(
            new NoopStimulationEngine(),
            connection,
            new DebugHardwareSimulationService(),
            new NoopLoggingService(),
            new DemoTiGroupFactory(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService());

        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StartChannelCommand.CanExecute(viewModel.Groups[0].Channels[0]));

        connection.SetConnected(true);

        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StartChannelCommand.CanExecute(viewModel.Groups[0].Channels[0]));
    }

    [Fact]
    public void TiApplyPrescription_PreservesChannelPolarity()
    {
        var viewModel = new TiControlViewModel(
            new NoopStimulationEngine(),
            new MutableHardwareConnectionState(),
            new DebugHardwareSimulationService(),
            new NoopLoggingService(),
            new DemoTiGroupFactory(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService());
        var firstChannel = viewModel.Groups[0].Channels[0];
        var secondChannel = viewModel.Groups[0].Channels[1];
        firstChannel.Polarity = "调转";
        secondChannel.Polarity = "不掉转";
        var prescription = new PrescriptionDefinition(
            "ti",
            "test",
            "test",
            "TI",
            2,
            PrescriptionDeliveryModes.Continuous,
            20,
            null,
            null,
            string.Empty,
            30,
            30,
            string.Empty,
            false,
            ["不掉转", "调转"]);

        viewModel.ApplyPrescription(prescription);

        Assert.Equal("调转", firstChannel.Polarity);
        Assert.Equal("不掉转", secondChannel.Polarity);
    }

    [Fact]
    public void TiApplyPrescription_PreservesEveryChannelsCarrierFrequency()
    {
        var viewModel = CreateTiViewModel();
        var channels = viewModel.Groups.SelectMany(group => group.Channels).ToArray();
        var originalFrequencies = channels
            .Select((channel, index) => (index + 900).ToString())
            .ToArray();
        for (var index = 0; index < channels.Length; index++)
        {
            channels[index].FrequencyHz = originalFrequencies[index];
        }

        viewModel.ApplyPrescription(CreateTiPrescription());

        Assert.Equal(
            originalFrequencies,
            channels.Select(channel => channel.FrequencyHz));
        Assert.All(channels, channel => Assert.Equal("2", channel.CurrentMA));
    }

    [Fact]
    public void TiApplyPrescriptionToChannel_ChangesOnlyTargetAndPreservesCarrierFrequency()
    {
        var viewModel = CreateTiViewModel();
        var target = viewModel.Groups[0].Channels[1];
        var untouched = viewModel.Groups[0].Channels[0];
        target.FrequencyHz = "1010";
        target.CurrentMA = "0.5";
        untouched.CurrentMA = "0.8";

        viewModel.ApplyPrescription(CreateTiPrescription(), target);

        Assert.Equal("2", target.CurrentMA);
        Assert.Equal("1010", target.FrequencyHz);
        Assert.Equal("0.8", untouched.CurrentMA);
    }

    [Fact]
    public void TiPrescriptionCommands_RequestAllOrOneChannel()
    {
        var viewModel = CreateTiViewModel();
        var requests = new List<StimulationPrescriptionRequestEventArgs>();
        viewModel.PrescriptionRequested += (_, args) => requests.Add(args);
        var target = viewModel.Groups[0].Channels[1];

        viewModel.UsePrescriptionCommand.Execute(null);
        viewModel.UseChannelPrescriptionCommand.Execute(target);

        Assert.Equal(2, requests.Count);
        Assert.True(requests[0].AppliesToAllChannels);
        Assert.Equal("TI", requests[0].StimulationType);
        Assert.False(requests[1].AppliesToAllChannels);
        Assert.Same(target, requests[1].TargetChannel);
    }

    [Fact]
    public void DebugSimulation_EnablesStimulationStartCommands()
    {
        var simulation = new MutableDebugHardwareSimulation();
        var simulationResult = simulation.Connect(realHardwareConnected: false);
        var realConnection = new MutableHardwareConnectionState();
        var viewModel = new DirectCurrentControlViewModel(
            new NoopStimulationEngine(),
            realConnection,
            simulation,
            new NoopLoggingService(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService(),
            new TestUserDialogService());

        Assert.True(simulationResult.Succeeded);
        Assert.True(simulation.IsConnected);
        Assert.False(realConnection.IsConnected);
        Assert.False(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.False(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));

        foreach (var channel in viewModel.Channels)
        {
            channel.UpdateImpedance(500m);
        }

        Assert.True(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.True(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));
    }

    [Fact]
    public void ExhibitionMode_DebugSimulationCannotReplaceRealHardwareConnection()
    {
        var simulation = new MutableDebugHardwareSimulation();
        Assert.True(simulation.Connect(realHardwareConnected: false).Succeeded);
        var realConnection = new MutableHardwareConnectionState();
        var viewModel = new DirectCurrentControlViewModel(
            new NoopStimulationEngine(),
            realConnection,
            simulation,
            new NoopLoggingService(),
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService(),
            new TestUserDialogService(),
            exhibitionMode: new ExhibitionModeState());
        foreach (var channel in viewModel.Channels)
        {
            channel.UpdateImpedance(500m);
        }

        Assert.False(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.False(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));
    }

    [Fact]
    public void PulseCurrentStartCommands_RequireRealHardwareConnection()
    {
        var connection = new MutableHardwareConnectionState();
        var viewModel = new PulseCurrentControlViewModel(
            connection,
            new LocalizationViewModel(new AppLocalizationService()),
            new NoopToastService(),
            new NoopLoggingService(),
            new TestUserDialogService());

        Assert.False(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.False(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));

        connection.SetConnected(true);

        Assert.True(viewModel.SynchronizedStartCommand.CanExecute(null));
        Assert.True(viewModel.StartChannelCommand.CanExecute(viewModel.Channels[0]));
    }

    private static TiControlViewModel CreateTiViewModel() => new(
        new NoopStimulationEngine(),
        new MutableHardwareConnectionState(),
        new DebugHardwareSimulationService(),
        new NoopLoggingService(),
        new DemoTiGroupFactory(),
        new LocalizationViewModel(new AppLocalizationService()),
        new NoopToastService());

    private static PrescriptionDefinition CreateTiPrescription() => new(
        Id: "ti",
        Name: "TI test",
        Indication: "test",
        StimulationType: "TI",
        CurrentMilliamp: 2,
        DeliveryMode: PrescriptionDeliveryModes.Interval,
        TotalDurationMinutes: 20,
        IntervalMinutes: 1,
        SessionDurationMinutes: 2,
        Course: string.Empty,
        RampUpSeconds: 30,
        RampDownSeconds: 30,
        EvidenceGrade: string.Empty,
        IsBuiltin: false);

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

    private sealed class MutableDebugHardwareSimulation : IDebugHardwareSimulationService
    {
        public event EventHandler? ConnectionChanged;

        public bool IsAvailable => true;

        public bool IsConnected { get; private set; }

        public DebugHardwareSimulationResult Connect(bool realHardwareConnected)
        {
            if (realHardwareConnected)
            {
                return new DebugHardwareSimulationResult(false, "Real hardware is connected.");
            }

            if (!IsConnected)
            {
                IsConnected = true;
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }

            return new DebugHardwareSimulationResult(true, "Debug simulation is connected.");
        }

        public DebugHardwareSimulationResult Disconnect()
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            return new DebugHardwareSimulationResult(true, "Disconnected.");
        }
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
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> CompleteGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default) => NotUsed();

        private static Task<HardwareOperationResult> NotUsed() =>
            throw new InvalidOperationException("Command-state tests must not execute stimulation commands.");
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
