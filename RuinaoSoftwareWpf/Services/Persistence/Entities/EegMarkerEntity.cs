namespace RuinaoSoftwareWpf;

internal sealed class EegMarkerEntity
{
    public long Id { get; set; }
    public long EegRecordingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Shortcut { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public long EventTimeUnixMs { get; set; }
    public long ExperimentElapsedMs { get; set; }
    public long SampleIndex { get; set; }
    public int PageIndex { get; set; }
    public int PageSampleIndex { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? MarkerCode { get; set; }
}
