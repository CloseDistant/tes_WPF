namespace RuinaoSoftwareWpf;

/// <summary>应用层使用的业务板槽位快照，不向界面泄漏硬件 DLL 内部对象。</summary>
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
