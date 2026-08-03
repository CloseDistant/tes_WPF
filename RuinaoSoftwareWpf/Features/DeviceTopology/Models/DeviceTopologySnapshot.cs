namespace RuinaoSoftwareWpf;

public enum DeviceBoardKind
{
    Unknown,
    Stimulation,
    Eeg,
}

/// <summary>应用层使用的业务板槽位快照，不向界面泄漏硬件DLL内部对象。</summary>
public sealed record DeviceTopologySlot(
    int SlotIndex,
    byte Address,
    bool IsInserted,
    bool IsOnline,
    DeviceBoardKind BoardKind,
    string IdentityText,
    IReadOnlyList<uint> IdentityRegisters,
    TimeSpan? Elapsed,
    string StatusMessage);

/// <summary>应用层使用的一次设备拓扑扫描结果。</summary>
public sealed record DeviceTopologySnapshot(
    uint SlotBitmap,
    DateTimeOffset CapturedAt,
    IReadOnlyList<DeviceTopologySlot> Slots);

public sealed record DeviceTopologyChangedEventArgs(DeviceTopologySnapshot? Snapshot);
