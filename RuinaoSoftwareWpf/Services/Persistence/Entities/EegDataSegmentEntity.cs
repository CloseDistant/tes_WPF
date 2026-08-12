namespace RuinaoSoftwareWpf;

internal sealed class EegDataSegmentEntity
{
    public long Id { get; set; }
    public long EegRecordingId { get; set; }
    public int SegmentIndex { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public long StartSampleIndex { get; set; }
    public long SampleCount { get; set; }
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long ByteLength { get; set; }
    public string Status { get; set; } = string.Empty;
}
