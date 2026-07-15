namespace RuinaoHardwareDebugWpf;

public sealed record StimulationChannelSnapshot(
    string Name,
    string Anode,
    string Cathode,
    string CurrentMA,
    string RampUpS,
    string RampDownS,
    string DurationS,
    string IntervalS,
    string SingleDurationS,
    string FrequencyHz,
    string Polarity,
    string StimulationMode);

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
                channel.StimulationMode)).ToArray());
    }

    public TiGroup ToMutableGroup()
    {
        var group = new TiGroup { Title = Title, DeltaText = DeltaText };
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
                StimulationMode = channel.StimulationMode
            });
        }

        return group;
    }
}
