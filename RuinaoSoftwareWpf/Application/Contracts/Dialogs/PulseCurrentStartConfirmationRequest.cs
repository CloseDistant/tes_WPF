namespace RuinaoSoftwareWpf;

/// <summary>经过业务校验后的tPCS启动确认快照。</summary>
public sealed record PulseCurrentStartConfirmationRequest(
    bool IsSynchronized,
    IReadOnlyList<PulseCurrentStartChannelConfirmation> Channels);

public sealed record PulseCurrentStartChannelConfirmation(
    string ChannelName,
    double CurrentMilliampere,
    int PulseWidthMilliseconds,
    int RiseWidthMilliseconds,
    int IntervalWidthMilliseconds,
    double TreatmentDurationSeconds,
    string Polarity,
    long PlannedTotalCount,
    decimal ImpedanceOhms,
    bool IsImpedanceWarning);
