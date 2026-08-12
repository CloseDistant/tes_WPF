namespace RuinaoSoftwareWpf;

public sealed record UnifiedSessionTimestamp(
    long EventTimeUnixMs,
    long SessionElapsedMs,
    long MonotonicTicks);
