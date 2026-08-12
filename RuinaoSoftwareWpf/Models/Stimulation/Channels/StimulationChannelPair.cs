namespace RuinaoSoftwareWpf;

/// <summary>由两个独立刺激通道组成的通用展示分组。</summary>
public sealed class StimulationChannelPair
{
    public StimulationChannelPair(
        int pairNumber,
        ChannelConfig firstChannel,
        ChannelConfig secondChannel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pairNumber, 1);
        ArgumentNullException.ThrowIfNull(firstChannel);
        ArgumentNullException.ThrowIfNull(secondChannel);
        if (ReferenceEquals(firstChannel, secondChannel))
        {
            throw new ArgumentException("刺激通道组必须包含两个不同的通道。", nameof(secondChannel));
        }

        PairNumber = pairNumber;
        FirstChannel = firstChannel;
        SecondChannel = secondChannel;
        Channels = [firstChannel, secondChannel];
    }

    public int PairNumber { get; }
    public ChannelConfig FirstChannel { get; }
    public ChannelConfig SecondChannel { get; }
    public IReadOnlyList<ChannelConfig> Channels { get; }
}
