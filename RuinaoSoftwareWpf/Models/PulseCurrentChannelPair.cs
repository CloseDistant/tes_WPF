namespace RuinaoSoftwareWpf;

/// <summary>tPCS 通道展示组。外层分组容纳两个通道，内部通道独立选择。</summary>
public sealed class PulseCurrentChannelPair
{
    public PulseCurrentChannelPair(
        int pairNumber,
        PulseCurrentChannelConfig firstChannel,
        PulseCurrentChannelConfig secondChannel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pairNumber, 1);
        ArgumentNullException.ThrowIfNull(firstChannel);
        ArgumentNullException.ThrowIfNull(secondChannel);
        if (ReferenceEquals(firstChannel, secondChannel))
        {
            throw new ArgumentException("tPCS 通道组必须包含两个不同的通道。", nameof(secondChannel));
        }

        PairNumber = pairNumber;
        FirstChannel = firstChannel;
        SecondChannel = secondChannel;
        Channels = [firstChannel, secondChannel];
    }

    public int PairNumber { get; }

    public PulseCurrentChannelConfig FirstChannel { get; }

    public PulseCurrentChannelConfig SecondChannel { get; }

    public IReadOnlyList<PulseCurrentChannelConfig> Channels { get; }

}
