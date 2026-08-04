namespace RuinaoSoftwareWpf;

/// <summary>tDCS 同步启动的首层操作确认信息。</summary>
public sealed record DirectCurrentSynchronizedStartConfirmationRequest(
    int TotalChannelCount);
