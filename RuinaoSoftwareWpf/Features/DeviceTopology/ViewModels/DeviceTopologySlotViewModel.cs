namespace RuinaoSoftwareWpf;

public sealed class DeviceTopologySlotViewModel
{
    public DeviceTopologySlotViewModel(DeviceTopologySlot slot)
    {
        SlotIndex = slot.SlotIndex;
        AddressText = $"0x{slot.Address:X2}";
        InsertedText = slot.IsInserted ? "已插板" : "空槽位";
        OnlineText = slot.IsOnline ? "在线" : "离线";
        BoardKindText = slot.BoardKind switch
        {
            DeviceBoardKind.Stimulation => "电刺激板",
            DeviceBoardKind.Eeg => "EEG板",
            _ => "—",
        };
        IdentityText = string.IsNullOrWhiteSpace(slot.IdentityText) ? "—" : slot.IdentityText;
        IdentityRegistersText = slot.IdentityRegisters.Count == 0
            ? "—"
            : string.Join(" ", slot.IdentityRegisters.Select(value => $"0x{value:X8}"));
        ElapsedText = slot.Elapsed is { } elapsed
            ? $"{elapsed.TotalMilliseconds:F1} ms"
            : "—";
        StatusMessage = slot.StatusMessage;
    }

    public int SlotIndex { get; }
    public string AddressText { get; }
    public string InsertedText { get; }
    public string OnlineText { get; }
    public string BoardKindText { get; }
    public string IdentityText { get; }
    public string IdentityRegistersText { get; }
    public string ElapsedText { get; }
    public string StatusMessage { get; }
}
