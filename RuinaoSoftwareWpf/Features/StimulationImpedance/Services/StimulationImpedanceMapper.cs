namespace RuinaoSoftwareWpf;

/// <summary>
/// 按背板槽位顺序把前两块在线电刺激板映射为CH1～CH8、CH9～CH16。
/// 这是纯映射规则，不访问USB，也不解释协议帧。
/// </summary>
internal static class StimulationImpedanceMapper
{
    private const int ChannelsPerBoard = 8;
    private const int LogicalChannelCount = 16;

    public static StimulationImpedanceSnapshot Map(
        DeviceTopologySnapshot? topology,
        IReadOnlyDictionary<byte, StimulationBoardImpedanceReading> boardReadings,
        DateTimeOffset capturedAt)
    {
        var boards = topology?.Slots
            .Where(slot => slot.IsInserted
                && slot.IsOnline
                && slot.BoardKind == DeviceBoardKind.Stimulation)
            .OrderBy(slot => slot.SlotIndex)
            .ThenBy(slot => slot.Address)
            .Take(LogicalChannelCount / ChannelsPerBoard)
            .ToArray()
            ?? [];

        var channels = new StimulationImpedanceChannelSnapshot[LogicalChannelCount];
        for (var logicalIndex = 0; logicalIndex < LogicalChannelCount; logicalIndex++)
        {
            var boardIndex = logicalIndex / ChannelsPerBoard;
            var physicalChannelNumber = logicalIndex % ChannelsPerBoard + 1;
            if (boardIndex >= boards.Length)
            {
                channels[logicalIndex] = Unavailable(logicalIndex + 1);
                continue;
            }

            var board = boards[boardIndex];
            if (!boardReadings.TryGetValue(board.Address, out var reading))
            {
                channels[logicalIndex] = Unavailable(
                    logicalIndex + 1,
                    board.SlotIndex,
                    board.Address,
                    physicalChannelNumber);
                continue;
            }

            var channel = reading.Channels.FirstOrDefault(
                item => item.PhysicalChannelNumber == physicalChannelNumber);
            channels[logicalIndex] = channel is null
                ? Unavailable(
                    logicalIndex + 1,
                    board.SlotIndex,
                    board.Address,
                    physicalChannelNumber)
                : new StimulationImpedanceChannelSnapshot(
                    logicalIndex + 1,
                    board.SlotIndex,
                    board.Address,
                    channel.PhysicalChannelNumber,
                    channel.RegisterAddress,
                    channel.RawValue,
                    channel.RawValue == 0 ? null : channel.ImpedanceOhms,
                    reading.CapturedAt);
        }

        return new StimulationImpedanceSnapshot(capturedAt, channels);
    }

    private static StimulationImpedanceChannelSnapshot Unavailable(
        int logicalChannelNumber,
        int? boardSlotIndex = null,
        byte? boardAddress = null,
        int? physicalChannelNumber = null) =>
        new(
            logicalChannelNumber,
            boardSlotIndex,
            boardAddress,
            physicalChannelNumber,
            null,
            null,
            null,
            null);
}
