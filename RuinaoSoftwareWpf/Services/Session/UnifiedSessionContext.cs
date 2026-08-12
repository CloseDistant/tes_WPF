namespace RuinaoSoftwareWpf;

/// <summary>
/// 三个实时模块共享的一次业务 Session。
/// UTC 用于跨文件查询，单调时钟用于同一进程内稳定排序和计算相对时间。
/// </summary>
public sealed record UnifiedSessionContext(
    string SessionKey,
    string PatientCode,
    DateTimeOffset StartedAtUtc,
    long OriginMonotonicTicks,
    long MonotonicFrequency);
