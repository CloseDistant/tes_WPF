using System.Windows;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TiSynchronizedStartTests
{
    [Fact]
    public void SynchronizedStart_StartsAllSixteenChannelsInOneExecutionGroup()
    {
        var engine = new CapturingStimulationEngine();
        var viewModel = CreateViewModel(engine);
        foreach (var channel in viewModel.Groups.SelectMany(group => group.Channels))
        {
            channel.CurrentMA = "1";
        }

        viewModel.StartCommand.Execute(null);

        Assert.NotNull(engine.LastStartedTiGroup);
        Assert.Equal(16, engine.LastStartedTiGroup.Channels.Count);
        Assert.Equal(
            Enumerable.Range(1, 16).Select(number => $"CH {number}"),
            engine.LastStartedTiGroup.Channels.Select(channel => channel.Name));
        Assert.All(
            viewModel.Groups.SelectMany(group => group.Channels),
            channel => Assert.False(channel.IsParameterEditingEnabled));
        Assert.All(
            viewModel.Groups.SelectMany(group => group.Channels),
            channel => Assert.True(channel.IsStimulating));

        viewModel.EmergencyStopCommand.Execute(null);
        Assert.NotNull(engine.LastEmergencyStoppedTiGroup);
        Assert.Equal(16, engine.LastEmergencyStoppedTiGroup.Channels.Count);
        Assert.All(
            viewModel.Groups.SelectMany(group => group.Channels),
            channel => Assert.False(channel.IsStimulating));
    }

    [Fact]
    public void SynchronizedStart_WhenAnyChannelIsInvalid_StartsNoChannels()
    {
        var engine = new CapturingStimulationEngine();
        var viewModel = CreateViewModel(engine);
        var channels = viewModel.Groups.SelectMany(group => group.Channels).ToArray();
        foreach (var channel in channels)
        {
            channel.CurrentMA = "1";
        }

        channels[15].FrequencyHz = string.Empty;

        viewModel.StartCommand.Execute(null);

        Assert.Null(engine.LastStartedTiGroup);
        Assert.All(channels, channel => Assert.True(channel.IsParameterEditingEnabled));
        Assert.All(channels, channel => Assert.False(channel.IsStimulating));
    }

    [Fact]
    public void StartChannel_ChangesOnlyTargetIndicatorToRunning()
    {
        var engine = new CapturingStimulationEngine();
        var viewModel = CreateViewModel(engine);
        var channels = viewModel.Groups.SelectMany(group => group.Channels).ToArray();
        var target = channels[0];
        target.CurrentMA = "1";

        viewModel.StartChannelCommand.Execute(target);

        Assert.True(target.IsStimulating);
        Assert.All(channels.Skip(1), channel => Assert.False(channel.IsStimulating));

        viewModel.EmergencyStopCommand.Execute(null);
        Assert.False(target.IsStimulating);
    }

    [Fact]
    public void StopChannelCommand_InDebugSimulation_StopsOnlyTarget()
    {
        var engine = new CapturingStimulationEngine();
        var viewModel = CreateViewModel(engine, new ConnectedDebugSimulation());
        var channels = viewModel.Groups.SelectMany(group => group.Channels).ToArray();
        var first = channels[0];
        var second = channels[1];
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
        var engine = new CapturingStimulationEngine { FailStop = true };
        var toast = new CapturingToastService();
        var viewModel = CreateViewModel(
            engine,
            new ConnectedDebugSimulation(),
            toast);
        var target = viewModel.Groups[0].Channels[0];
        target.CurrentMA = "1";

        viewModel.StartChannelCommand.Execute(target);
        viewModel.StopChannelCommand.Execute(target);

        Assert.True(target.IsStimulating);
        Assert.Equal("刺激停止失败", toast.Title);
        Assert.Contains("停止命令未完成", toast.Message);
    }

    private static TiControlViewModel CreateViewModel(
        CapturingStimulationEngine stimulationEngine,
        IDebugHardwareSimulationService? debugHardwareSimulation = null,
        IToastService? toastService = null)
    {
        return new TiControlViewModel(
            stimulationEngine,
            new ConnectedHardwareState(),
            debugHardwareSimulation ?? new DebugHardwareSimulationService(),
            new NoopLoggingService(),
            new DemoTiGroupFactory(),
            new LocalizationViewModel(new AppLocalizationService()),
            toastService ?? new NoopToastService());
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

        public DebugHardwareSimulationResult Disconnect() =>
            new(true, "已断开");
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

    private sealed class CapturingStimulationEngine : IStimulationEngine
    {
        public TiGroup? LastStartedTiGroup { get; private set; }

        public TiGroup? LastEmergencyStoppedTiGroup { get; private set; }

        public bool FailStop { get; init; }

        public StimulationExecutionState CurrentState => StimulationExecutionState.Idle;

        public Task<HardwareOperationResult> StartTiGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string prescriptionName,
            CancellationToken cancellationToken = default)
        {
            LastStartedTiGroup = group;
            return Success();
        }

        public Task<HardwareOperationResult> StartDirectCurrentGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string prescriptionName,
            CancellationToken cancellationToken = default) => NotUsed();

        public Task<HardwareOperationResult> StopGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default) =>
            FailStop
                ? Task.FromException<HardwareOperationResult>(new TimeoutException("stop timeout"))
                : Success();

        public Task<HardwareOperationResult> EmergencyStopTiGroupAsync(
            TiGroup group,
            string reason,
            CancellationToken cancellationToken = default)
        {
            LastEmergencyStoppedTiGroup = group;
            return Success();
        }

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
            throw new InvalidOperationException("This operation is not used by synchronized-start tests.");

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
        public string Title { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;
        public string Icon => string.Empty;
        public string Accent => string.Empty;
        public void Show(ToastKind kind, string title, string message, TimeSpan? duration = null)
        {
            Title = title;
            Message = message;
        }
        public void ShowInformation(string message, string title = "提示") => Show(ToastKind.Information, title, message);
        public void ShowSuccess(string title, string message) => Show(ToastKind.Success, title, message);
        public void ShowError(string title, string message) => Show(ToastKind.Error, title, message);
    }
}
