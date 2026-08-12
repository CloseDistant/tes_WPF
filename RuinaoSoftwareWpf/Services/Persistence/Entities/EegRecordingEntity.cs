namespace RuinaoSoftwareWpf;

internal sealed class EegRecordingEntity
{
    public long Id { get; set; }
    public long ModuleRecordId { get; set; }
    public string RecordName { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public int ChannelCount { get; set; }
    public int SampleRateHz { get; set; }
    public int PageSeconds { get; set; }
    public int SegmentSeconds { get; set; }
    public string DataType { get; set; } = "float32";
    public string ChannelNamesJson { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long SampleCount { get; set; }
    public string Status { get; set; } = string.Empty;
}
