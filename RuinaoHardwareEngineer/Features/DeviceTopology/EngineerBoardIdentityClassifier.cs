using System.Text;

namespace RuinaoHardwareEngineer.Features.DeviceTopology;

public static class EngineerBoardIdentityClassifier
{
    public static (EngineerBoardKind Kind, string Text) Classify(IReadOnlyList<uint> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var bytes = new byte[values.Count * sizeof(uint)];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var offset = index * sizeof(uint);
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        var text = Encoding.UTF8.GetString(bytes)
            .TrimEnd('\0', '\uFFFF', ' ');
        var normalized = text.ToUpperInvariant();
        var kind = normalized.Contains("EEG", StringComparison.Ordinal)
            ? EngineerBoardKind.Eeg
            : normalized.Contains("TES", StringComparison.Ordinal)
                || normalized.Contains("STIM", StringComparison.Ordinal)
                ? EngineerBoardKind.Stimulation
                : EngineerBoardKind.Unknown;

        return (kind, string.IsNullOrWhiteSpace(text) ? "未提供可识别文本" : text);
    }
}
