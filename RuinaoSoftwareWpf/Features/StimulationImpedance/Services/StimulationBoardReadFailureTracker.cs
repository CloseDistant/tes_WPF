namespace RuinaoSoftwareWpf;

/// <summary>按业务板统计连续读取失败；成功一次即清零，连续第二次失败才判定快照失效。</summary>
internal sealed class StimulationBoardReadFailureTracker
{
    private readonly Dictionary<byte, int> failureCounts = [];

    public bool RecordFailure(byte boardAddress)
    {
        var failureCount = failureCounts.GetValueOrDefault(boardAddress) + 1;
        failureCounts[boardAddress] = failureCount;
        return failureCount >= 2;
    }

    public void RecordSuccess(byte boardAddress)
    {
        failureCounts.Remove(boardAddress);
    }

    public void Retain(IReadOnlySet<byte> boardAddresses)
    {
        foreach (var address in failureCounts.Keys
                     .Where(address => !boardAddresses.Contains(address))
                     .ToArray())
        {
            failureCounts.Remove(address);
        }
    }

    public void Clear()
    {
        failureCounts.Clear();
    }
}
