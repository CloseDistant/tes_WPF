namespace RuinaoSoftwareWpf.Tests;

using RuinaoHardwareEngineer.Features.Stimulation.Services;
using RuinaoHardwareEngineer.Features.Stimulation.ViewModels;
using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using RuinaoTesProtocol.V15;
using Xunit;

public sealed class EngineerStimulationViewModelTests
{
    private static readonly BackplaneConnectionOptions Options = new(
        TesV14ProtocolConstants.UsbTestProtocolVersion,
        TimeSpan.FromSeconds(5));

    [Fact]
    public async Task DirectCurrent_PreservesRawLevelsAndConvertsSeconds()
    {
        var service = new CapturingStimulationService();
        var viewModel = new EngineerStimulationViewModel(service)
        {
            LowLevel = "12345",
            HighLevel = "54321",
            TotalDurationSeconds = "120",
            DirectRampUpSeconds = "12",
            DirectRampDownSeconds = "18",
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => viewModel.ConfigureAsync(Options, TestContext.Current.CancellationToken));

        var configuration = Assert.IsType<TesV15StimulationConfiguration>(service.Configuration);
        var waveform = Assert.Single(configuration.Waveforms);
        Assert.Equal(120_000U, configuration.TotalTimeMs);
        Assert.Equal(120_000_000U, waveform.DurationUs);
        Assert.Equal(12_345U, waveform.LowLevelOrPositiveValue);
        Assert.Equal(54_321U, waveform.HighLevelOrNegativeValue);
        Assert.Equal(100U, waveform.RisePermilleOrPositiveDurationUs);
        Assert.Equal(750U, waveform.HoldPermilleOrInterphaseIntervalUs);
        Assert.Equal(150U, waveform.FallPermilleOrNegativeDurationUs);
    }

    [Fact]
    public async Task PulseCurrent_PreservesRawLevelsAndConvertsMilliseconds()
    {
        var service = new CapturingStimulationService();
        var viewModel = new EngineerStimulationViewModel(service)
        {
            SelectedMode = EngineerStimulationViewModel.PulseCurrentMode,
            LowLevel = "11111",
            HighLevel = "55555",
            TotalDurationSeconds = "2",
            PulseRiseWidthMilliseconds = "1.5",
            PulseWidthMilliseconds = "2.5",
            PulseIntervalWidthMilliseconds = "3",
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => viewModel.ConfigureAsync(Options, TestContext.Current.CancellationToken));

        var configuration = Assert.IsType<TesV15StimulationConfiguration>(service.Configuration);
        Assert.Equal(2_000U, configuration.TotalTimeMs);
        Assert.Collection(
            configuration.Waveforms,
            active =>
            {
                Assert.Equal(4_000U, active.DurationUs);
                Assert.Equal(11_111U, active.LowLevelOrPositiveValue);
                Assert.Equal(55_555U, active.HighLevelOrNegativeValue);
                Assert.Equal(375U, active.RisePermilleOrPositiveDurationUs);
                Assert.Equal(625U, active.HoldPermilleOrInterphaseIntervalUs);
                Assert.Equal(0U, active.FallPermilleOrNegativeDurationUs);
            },
            interval =>
            {
                Assert.Equal(TesV15StimulationMode.Constant, interval.Mode);
                Assert.Equal(3_000U, interval.DurationUs);
                Assert.Equal(11_111U, interval.Offset);
            });
    }

    private sealed class CapturingStimulationService : IEngineerStimulationService
    {
        public TesV15StimulationConfiguration? Configuration { get; private set; }

        public Task<BackplaneStimulationConfigurationResult> ConfigureAsync(
            byte targetAddress,
            TesV15StimulationConfiguration configuration,
            BackplaneConnectionOptions options,
            CancellationToken cancellationToken = default)
        {
            Configuration = configuration;
            return Task.FromException<BackplaneStimulationConfigurationResult>(
                new OperationCanceledException("测试在USB发送前停止。"));
        }

        public Task<BackplaneRegisterOperationResult> StartAsync(
            byte targetAddress,
            BackplaneConnectionOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BackplaneRegisterOperationResult> StopAsync(
            byte targetAddress,
            BackplaneConnectionOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BackplaneStimulationStatusResult> ReadStatusAsync(
            byte targetAddress,
            BackplaneConnectionOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
