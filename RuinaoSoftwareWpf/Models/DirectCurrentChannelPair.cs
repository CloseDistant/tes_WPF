namespace RuinaoSoftwareWpf;

/// <summary>
/// tDCS 通道展示组。外层分组始终同时容纳两个通道，但内部通道独立选择。
/// </summary>
public sealed class DirectCurrentChannelPair
{
    public DirectCurrentChannelPair(
        int pairNumber,
        ChannelConfig firstChannel,
        ChannelConfig secondChannel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pairNumber, 1);
        ArgumentNullException.ThrowIfNull(firstChannel);
        ArgumentNullException.ThrowIfNull(secondChannel);
        if (ReferenceEquals(firstChannel, secondChannel))
        {
            throw new ArgumentException("tDCS 通道组必须包含两个不同的通道。", nameof(secondChannel));
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
