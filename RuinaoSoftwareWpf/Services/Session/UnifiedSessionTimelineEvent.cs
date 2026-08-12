namespace RuinaoSoftwareWpf;

public sealed record UnifiedSessionTimelineEvent(
    string SessionKey,
    string ModuleCode,
    string EventType,
    long SequenceNo,
    long EventTimeUnixMs,
    long SessionElapsedMs,
    long MonotonicTicks,
    long MonotonicFrequency,
    long? SourceTimeUnixMs,
    string Message,
    string PayloadJson);
