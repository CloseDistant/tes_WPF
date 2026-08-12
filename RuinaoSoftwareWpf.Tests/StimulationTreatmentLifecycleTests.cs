namespace RuinaoSoftwareWpf.Tests;

using System.Text.Json;
using Xunit;

public sealed class StimulationTreatmentLifecycleTests
{
    [Fact]
    public void RecalculateRunState_WhenOneSynchronizedChannelStillRuns_KeepsRunRunning()
    {
        var run = CreateRun(
            CreateChannel(StimulationTreatmentLifecycle.EndedStatus, 100),
            CreateChannel(StimulationTreatmentLifecycle.RunningStatus, null));

        StimulationTreatmentLifecycle.RecalculateRunState(run, 200);

        Assert.Equal(StimulationTreatmentLifecycle.RunningStatus, run.Status);
        Assert.Null(run.EndedAtUnixMs);
        Assert.Equal(200, run.UpdatedAtUnixMs);
    }

    [Fact]
    public void RecalculateRunState_WhenEveryChannelEnded_UsesLatestChannelEndTime()
    {
        var run = CreateRun(
            CreateChannel(StimulationTreatmentLifecycle.EndedStatus, 100),
            CreateChannel(StimulationTreatmentLifecycle.EndedStatus, 150));

        StimulationTreatmentLifecycle.RecalculateRunState(run, 200);

        Assert.Equal(StimulationTreatmentLifecycle.EndedStatus, run.Status);
        Assert.Equal(150, run.EndedAtUnixMs);
    }

    [Fact]
    public void RecalculateRunState_WhenAnyChannelIsIncomplete_MarksRunIncomplete()
    {
        var run = CreateRun(
            CreateChannel(StimulationTreatmentLifecycle.EndedStatus, 100),
            CreateChannel(StimulationTreatmentLifecycle.IncompleteStatus, null));

        StimulationTreatmentLifecycle.RecalculateRunState(run, 200);

        Assert.Equal(StimulationTreatmentLifecycle.IncompleteStatus, run.Status);
        Assert.Null(run.EndedAtUnixMs);
    }

    [Fact]
    public void MarkSoftwareInterrupted_DoesNotInventAnEndTime()
    {
        var channel = CreateChannel(StimulationTreatmentLifecycle.RunningStatus, null);

        StimulationTreatmentLifecycle.MarkSoftwareInterrupted(channel, 200);

        Assert.Equal(StimulationTreatmentLifecycle.IncompleteStatus, channel.Status);
        Assert.Equal(StimulationTreatmentLifecycle.AbnormalTerminationEndType, channel.EndType);
        Assert.Equal(StimulationEndReasonCodes.SoftwareInterrupted, channel.EndReasonCode);
        Assert.Null(channel.EndedAtUnixMs);
    }

    [Fact]
    public void CreateRunStartRequest_PreservesIndependentChannelParameters()
    {
        var group = new TiGroup { Title = "TI 刺激 1" };
        group.Channels.Add(CreateConfiguredChannel("CH 1", "1.25", "1000"));
        group.Channels.Add(CreateConfiguredChannel("CH 2", "1.75", "1010"));
        var reusable = StimulationRecordParameters.CreateTiPrescription(group, "测试处方");

        var request = StimulationRecordParameters.CreateRunStartRequest(group, reusable);

        Assert.Equal(2, request.Channels.Count);
        Assert.Equal(1.25, request.Channels[0].CurrentMilliamp);
        Assert.Equal(1.75, request.Channels[1].CurrentMilliamp);
        var first = JsonSerializer.Deserialize<ChannelParameterSnapshot>(
            request.Channels[0].ParameterSnapshotJson);
        var second = JsonSerializer.Deserialize<ChannelParameterSnapshot>(
            request.Channels[1].ParameterSnapshotJson);
        Assert.Equal(1000, first?.CarrierFrequencyHz);
        Assert.Equal(1010, second?.CarrierFrequencyHz);
    }

    [Fact]
    public void CreatePulseRunStartRequest_PreservesPlannedCountAndPulseParameters()
    {
        var channel = new PulseCurrentChannelConfig { Name = "CH 1" };
        var parameters = new PulseCurrentParameters(
            2,
            10,
            5,
            20,
            1200,
            PulseCurrentPolarities.NotReversed,
            34286);

        var request = StimulationRecordParameters.CreatePulseRunStartRequest(
            new Dictionary<PulseCurrentChannelConfig, PulseCurrentParameters>
            {
                [channel] = parameters
            },
            "脉冲处方",
            channel.Name);

        var savedChannel = Assert.Single(request.Channels);
        Assert.Equal(34286, savedChannel.PlannedTotalCount);
        var snapshot = JsonSerializer.Deserialize<ChannelParameterSnapshot>(
            savedChannel.ParameterSnapshotJson);
        Assert.Equal(10, snapshot?.PulseWidthMilliseconds);
        Assert.Equal(5, snapshot?.PulseRiseWidthMilliseconds);
        Assert.Equal(20, snapshot?.PulseIntervalWidthMilliseconds);
    }

    private static StimulationRunEntity CreateRun(params StimulationChannelTreatmentEntity[] channels) =>
        new()
        {
            RunId = Guid.NewGuid().ToString("N"),
            Status = StimulationTreatmentLifecycle.RunningStatus,
            Channels = channels
        };

    private static StimulationChannelTreatmentEntity CreateChannel(string status, long? endedAtUnixMs) =>
        new()
        {
            ChannelName = Guid.NewGuid().ToString("N"),
            Status = status,
            EndedAtUnixMs = endedAtUnixMs
        };

    private static ChannelConfig CreateConfiguredChannel(
        string name,
        string currentMilliamp,
        string frequencyHz) =>
        new()
        {
            Name = name,
            Anode = "E1",
            Cathode = "E2",
            CurrentMA = currentMilliamp,
            RampUpS = "0.5",
            RampDownS = "0.5",
            DurationS = "1200.0",
            IntervalS = "5.0",
            SingleDurationS = "60.0",
            FrequencyHz = frequencyHz,
            Polarity = "不调转",
            StimulationMode = PrescriptionDeliveryModes.Interval
        };
}
