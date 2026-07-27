using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TiGroupTests
{
    [Fact]
    public void DeltaText_WithTwoValidCarrierFrequencies_ShowsAbsoluteDifference()
    {
        var group = CreateGroup("1010", "1000");

        Assert.Equal("Δf: 10.0 Hz", group.DeltaText);
    }

    [Fact]
    public void DeltaText_WhenCarrierFrequencyChanges_NotifiesAndRecalculates()
    {
        var group = CreateGroup("1000", "1010");
        var changedProperties = new List<string?>();
        group.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        group.Channels[1].FrequencyHz = "1012.5";

        Assert.Contains(nameof(TiGroup.DeltaText), changedProperties);
        Assert.Equal("Δf: 12.5 Hz", group.DeltaText);
    }

    [Theory]
    [InlineData("", "1010")]
    [InlineData("not-a-number", "1010")]
    [InlineData("-1000", "1010")]
    public void DeltaText_WithMissingOrInvalidCarrierFrequency_ShowsUnavailable(
        string firstFrequencyHz,
        string secondFrequencyHz)
    {
        var group = CreateGroup(firstFrequencyHz, secondFrequencyHz);

        Assert.Equal("Δf: -- Hz", group.DeltaText);
    }

    [Fact]
    public void DeltaText_WhenSecondChannelIsAdded_NotifiesAndCalculates()
    {
        var group = new TiGroup();
        group.Channels.Add(new ChannelConfig { FrequencyHz = "1000" });
        var deltaChanged = false;
        group.PropertyChanged += (_, args) =>
            deltaChanged |= args.PropertyName == nameof(TiGroup.DeltaText);

        group.Channels.Add(new ChannelConfig { FrequencyHz = "1010" });

        Assert.True(deltaChanged);
        Assert.Equal("Δf: 10.0 Hz", group.DeltaText);
    }

    private static TiGroup CreateGroup(string firstFrequencyHz, string secondFrequencyHz)
    {
        var group = new TiGroup();
        group.Channels.Add(new ChannelConfig { FrequencyHz = firstFrequencyHz });
        group.Channels.Add(new ChannelConfig { FrequencyHz = secondFrequencyHz });
        return group;
    }
}
