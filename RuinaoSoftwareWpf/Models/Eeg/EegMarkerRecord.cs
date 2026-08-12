using System.Windows.Media;

namespace RuinaoSoftwareWpf;

public sealed record EegMarkerRecord(
    string Name,
    string Shortcut,
    Color Color,
    long AbsoluteTimestampMs,
    TimeSpan ExperimentTime,
    int PageIndex,
    int PageSampleIndex,
    long SampleIndex,
    string Source,
    string Code);
