namespace RuinaoSoftwareWpf;

internal sealed class SessionTimelineEventEntity
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public string SessionKey { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public long SequenceNo { get; set; }
    public long EventTimeUnixMs { get; set; }
    public long SessionElapsedMs { get; set; }
    public long MonotonicTicks { get; set; }
    public long MonotonicFrequency { get; set; }
    public long? SourceTimeUnixMs { get; set; }
    public string? Message { get; set; }
    public string? PayloadJson { get; set; }
}
