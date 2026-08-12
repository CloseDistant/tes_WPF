namespace RuinaoSoftwareWpf;

internal sealed class SensorSampleEntity
{
    public long Id { get; set; }
    public long? SessionId { get; set; }
    public long? ModuleRecordId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? SourceName { get; set; }
    public long SampleTimeUnixMs { get; set; }
    public long? SequenceNo { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}
