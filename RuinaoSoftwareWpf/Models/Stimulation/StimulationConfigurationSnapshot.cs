namespace RuinaoSoftwareWpf;

public sealed record StimulationConfigurationSnapshot(
    string Title,
    string DeltaText,
    IReadOnlyList<StimulationChannelSnapshot> Channels)
{
    public static StimulationConfigurationSnapshot Create(TiGroup group)
    {
        return new StimulationConfigurationSnapshot(
            group.Title,
            group.DeltaText,
            group.Channels.Select(channel => new StimulationChannelSnapshot(
                channel.Name,
                channel.Anode,
                channel.Cathode,
                channel.CurrentMA,
                channel.RampUpS,
                channel.RampDownS,
                channel.DurationS,
                channel.IntervalS,
                channel.SingleDurationS,
                channel.FrequencyHz,
                channel.Polarity,
                channel.StimulationMode,
                channel.PulseWidthMilliseconds,
                channel.PulseRiseWidthMilliseconds,
                channel.PulseIntervalWidthMilliseconds,
                channel.PlannedPulseCount)).ToArray());
    }

    public TiGroup ToMutableGroup()
    {
        var group = new TiGroup { Title = Title };
        foreach (var channel in Channels)
        {
            group.Channels.Add(new ChannelConfig
            {
                Name = channel.Name,
                Anode = channel.Anode,
                Cathode = channel.Cathode,
                CurrentMA = channel.CurrentMA,
                RampUpS = channel.RampUpS,
                RampDownS = channel.RampDownS,
                DurationS = channel.DurationS,
                IntervalS = channel.IntervalS,
                SingleDurationS = channel.SingleDurationS,
                FrequencyHz = channel.FrequencyHz,
                Polarity = channel.Polarity,
                StimulationMode = channel.StimulationMode,
                PulseWidthMilliseconds = channel.PulseWidthMilliseconds,
                PulseRiseWidthMilliseconds = channel.PulseRiseWidthMilliseconds,
                PulseIntervalWidthMilliseconds = channel.PulseIntervalWidthMilliseconds,
                PlannedPulseCount = channel.PlannedPulseCount
            });
        }

        return group;
    }
}
