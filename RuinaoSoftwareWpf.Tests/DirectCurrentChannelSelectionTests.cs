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
    public void SynchronizedStart_StartsAllSixteenChannelsAfterEveryChannelPassesValidation()
    {
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine);
        foreach (var channel in viewModel.Channels)
        {
            channel.CurrentMA = "1";
        }

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.NotNull(engine.LastStartedDirectCurrentGroup);
        Assert.Equal(16, engine.LastStartedDirectCurrentGroup.Channels.Count);
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

        viewModel.Channels[15].CurrentMA = string.Empty;

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

    [Theory]
    [InlineData(null)]
    [InlineData(20001)]
    public void StartChannel_WhenImpedanceIsUnavailableOrCritical_DoesNotStart(int? impedanceOhms)
    {
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine);
        var target = viewModel.Channels[0];
        target.CurrentMA = "1";
        target.UpdateImpedance(impedanceOhms);

        viewModel.StartChannelCommand.Execute(target);

        Assert.False(target.IsStimulating);
        Assert.Null(engine.LastStartedDirectCurrentGroup);
    }

    [Fact]
    public void StartChannel_WhenImpedanceIsWarningAndUserCancels_DoesNotStart()
    {
        var dialog = new TestUserDialogService { ConfirmationResult = false };
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine, userDialogService: dialog);
        var target = viewModel.Channels[1];
        target.CurrentMA = "1";
        target.UpdateImpedance(12_400m);

        viewModel.StartChannelCommand.Execute(target);

        Assert.False(target.IsStimulating);
        Assert.Null(engine.LastStartedDirectCurrentGroup);
        Assert.Contains("CH2：12.40kΩ", dialog.LastConfirmationMessage);
    }

    [Fact]
    public void SynchronizedStart_SkipsCriticalAndUnavailableChannelsAfterOneConfirmation()
    {
        var dialog = new TestUserDialogService();
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine, userDialogService: dialog);
        foreach (var channel in viewModel.Channels)
        {
            channel.CurrentMA = "1";
        }

        viewModel.Channels[1].UpdateImpedance(12_400m);
        viewModel.Channels[2].UpdateImpedance(20_001m);
        viewModel.Channels[3].UpdateImpedance(null);

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.Equal(14, engine.LastStartedDirectCurrentGroup?.Channels.Count);
        Assert.True(viewModel.Channels[1].IsStimulating);
        Assert.False(viewModel.Channels[2].IsStimulating);
        Assert.False(viewModel.Channels[3].IsStimulating);
        Assert.Contains("CH3：阻抗过高", dialog.LastConfirmationMessage);
        Assert.Contains("CH4：阻抗不可用", dialog.LastConfirmationMessage);
    }

    [Fact]
    public void StopChannelCommand_InDebugSimulation_StopsOnlyTarget()
    {
        var viewModel = CreateViewModel(
            debugHardwareSimulation: new ConnectedDebugSimulation());
        var first = viewModel.Channels[0];
        var second = viewModel.Channels[1];
        first.CurrentMA = "1";
        second.CurrentMA = "1";

        viewModel.StartChannelCommand.Execute(first);
        viewModel.StartChannelCommand.Execute(second);

        Assert.True(viewModel.StopChannelCommand.CanExecute(first));
        viewModel.StopChannelCommand.Execute(first);

        Assert.False(first.IsStimulating);
        Assert.True(first.IsParameterEditingEnabled);
        Assert.Equal("00:00:00", first.RemainingTime);
        Assert.True(second.IsStimulating);
        Assert.False(viewModel.StopChannelCommand.CanExecute(first));
    }

    [Fact]
    public void StopChannelCommand_WhenStopFails_ShowsStopFailureAndKeepsChannelRunning()
    {
        var engine = new NoopStimulationEngine { FailStop = true };
        var toast = new CapturingToastService();
        var viewModel = CreateViewModel(
            engine,
            new ConnectedDebugSimulation(),
            toast);
        var target = viewModel.Channels[0];
        target.CurrentMA = "1";

        viewModel.StartChannelCommand.Execute(target);
        viewModel.StopChannelCommand.Execute(target);

        Assert.True(target.IsStimulating);
        Assert.Equal("刺激停止失败", toast.Title);
        Assert.Contains("停止命令未完成", toast.Message);
    }

    [Fact]
    public void StopChannelCommand_WithRealConnection_StopsOnlyTarget()
    {
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(engine);
        var first = viewModel.Channels[0];
        var second = viewModel.Channels[1];
        first.CurrentMA = "1";
        second.CurrentMA = "1";

        viewModel.StartChannelCommand.Execute(first);
        viewModel.StartChannelCommand.Execute(second);
        viewModel.StopChannelCommand.Execute(first);

        Assert.Equal([first.Name], engine.LastStoppedChannelNames);
        Assert.False(first.IsStimulating);
        Assert.True(second.IsStimulating);
    }

    [Fact]
    public void EmergencyStopCommand_WhenConnectedWithoutRunningChannels_RemainsAvailable()
    {
        var engine = new NoopStimulationEngine();
        var toast = new CapturingToastService();
        var viewModel = CreateViewModel(engine, toastService: toast);

        Assert.True(viewModel.EmergencyStopCommand.CanExecute(null));

        viewModel.EmergencyStopCommand.Execute(null);

        Assert.Equal(1, engine.DirectCurrentEmergencyStopCount);
        Assert.Equal("紧急停止", toast.Title);
    }

    [Fact]
    public void DebugImpedanceProvider_PopulatesChannelsAndAllowsSynchronizedStart()
    {
        var simulation = new ConnectedDebugSimulation();
        var provider = new DebugStimulationImpedanceProvider(simulation);

#if DEBUG
        var engine = new NoopStimulationEngine();
        var viewModel = CreateViewModel(
            engine,
            simulation,
            debugImpedanceProvider: provider,
            initializeImpedance: false);

        Assert.Equal(500m, viewModel.Channels[0].ImpedanceOhms);
        Assert.Equal(800m, viewModel.Channels[15].ImpedanceOhms);
        Assert.All(
            viewModel.Channels,
            channel => Assert.Equal(StimulationImpedanceStatus.Normal, channel.ImpedanceStatus));
        Assert.True(viewModel.SynchronizedStartCommand.CanExecute(null));

        viewModel.SynchronizedStartCommand.Execute(null);

        Assert.Equal(16, engine.LastStartedDirectCurrentGroup?.Channels.Count);
#else
        Assert.Null(provider.GetSnapshot());
#endif
    }

    [Fact]
    public void ParameterValidationFailedCommand_ShowsWarningToast()
    {
        var toast = new CapturingToastService();
        var viewModel = CreateViewModel(toastService: toast);

        viewModel.ParameterValidationFailedCommand.Execute("幅值最小设置步进为 0.01 mA。");

        Assert.Equal(ToastKind.Warning, toast.Kind);
        Assert.Equal("幅值最小设置步进为 0.01 mA。", toast.Message);
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
        NoopStimulationEngine? stimulationEngine = null,
        IDebugHardwareSimulationService? debugHardwareSimulation = null,
        IToastService? toastService = null,
        IUserDialogService? userDialogService = null,
        IDebugStimulationImpedanceProvider? debugImpedanceProvider = null,
        bool initializeImpedance = true)
    {
        var viewModel = new DirectCurrentControlViewModel(
            stimulationEngine ?? new NoopStimulationEngine(),
            new ConnectedHardwareState(),
            debugHardwareSimulation ?? new DebugHardwareSimulationService(),
            new NoopLoggingService(),
            new LocalizationViewModel(new AppLocalizationService()),
            toastService ?? new NoopToastService(),
            userDialogService ?? new TestUserDialogService(),
            debugImpedanceProvider);
        if (initializeImpedance)
        {
            foreach (var channel in viewModel.Channels)
            {
                channel.UpdateImpedance(500m);
            }
        }

        return viewModel;
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

        public string[] LastStoppedChannelNames { get; private set; } = [];

        public int DirectCurrentEmergencyStopCount { get; private set; }

        public bool FailStop { get; init; }

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

        public Task<HardwareOperationResult> StopGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default)
        {
            if (FailStop)
            {
                return Task.FromException<HardwareOperationResult>(new TimeoutException("stop timeout"));
            }

            LastStoppedChannelNames = group.Channels.Select(channel => channel.Name).ToArray();
            return Success();
        }

        public Task<HardwareOperationResult> EmergencyStopTiGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> EmergencyStopDirectCurrentGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default)
        {
            DirectCurrentEmergencyStopCount++;
            return Success();
        }

        public Task<HardwareOperationResult> CompleteGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
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

    private sealed class CapturingToastService : IToastService
    {
        public Visibility Visibility => Visibility.Visible;
        public ToastKind Kind { get; private set; }
        public string Title { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;
        public string Icon => string.Empty;
        public string Accent => string.Empty;
        public void Show(ToastKind kind, string title, string message, TimeSpan? duration = null)
        {
            Kind = kind;
            Title = title;
            Message = message;
        }
        public void ShowInformation(string message, string title = "提示") => Show(ToastKind.Information, title, message);
        public void ShowSuccess(string title, string message) => Show(ToastKind.Success, title, message);
        public void ShowError(string title, string message) => Show(ToastKind.Error, title, message);
    }
}
