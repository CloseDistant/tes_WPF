namespace RuinaoSoftwareWpf;

/// <summary>应用层使用的一次设备拓扑扫描结果。</summary>
public sealed record DeviceTopologySnapshot(
    uint SlotBitmap,
    DateTimeOffset CapturedAt,
    IReadOnlyList<DeviceTopologySlot> Slots);
