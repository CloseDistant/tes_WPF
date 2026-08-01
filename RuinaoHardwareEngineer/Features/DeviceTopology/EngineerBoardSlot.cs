namespace RuinaoHardwareEngineer.Features.DeviceTopology;

public sealed record EngineerBoardSlot(
    int SlotIndex,
    byte Address,
    bool IsOnline,
    EngineerBoardKind BoardKind,
    string IdentityText,
    IReadOnlyList<uint> IdentityRegisters,
    TimeSpan? Elapsed,
    string StatusMessage)
{
    /// <summary>
    /// 背板0x0900槽位位图是否报告该位置已插板。
    /// IsOnline进一步表示该业务板地址是否返回了合法协议回复。
    /// </summary>
    public bool IsInserted { get; init; } = IsOnline;
}
