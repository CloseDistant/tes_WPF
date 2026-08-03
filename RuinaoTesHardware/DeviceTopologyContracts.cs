namespace RuinaoTesHardware;

/// <summary>业务板类型。当前硬件阶段仅正式使用电刺激板类型。</summary>
public enum TesBusinessBoardKind
{
    Unknown,
    Stimulation,
    Eeg,
}

/// <summary>背板中一个业务板槽位的只读探测结果。</summary>
public sealed record TesBusinessBoardSlot(
    int SlotIndex,
    byte Address,
    bool IsInserted,
    bool IsOnline,
    TesBusinessBoardKind BoardKind,
    string IdentityText,
    IReadOnlyList<uint> IdentityRegisters,
    TimeSpan? Elapsed,
    string StatusMessage);

/// <summary>一次设备拓扑扫描生成的不可变快照。</summary>
public sealed record TesDeviceTopologySnapshot(
    uint SlotBitmap,
    DateTimeOffset CapturedAt,
    IReadOnlyList<TesBusinessBoardSlot> Slots);
