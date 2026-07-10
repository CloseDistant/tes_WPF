namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 仅写日志的硬件传输层。
/// 当前阶段尚未接入真实 USB/串口/HID/TCP 传输，因此这里只记录 TX 帧，
/// 用于确认 WPF 和协议 DLL 已经生成正确命令。
/// </summary>
public sealed class LogOnlyHardwareTransport : IHardwareLink
{
    private readonly ILoggingService logger;

    public LogOnlyHardwareTransport(ILoggingService logger)
    {
        this.logger = logger;
    }

    public bool IsConnected => true;

    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<HardwareLinkReply> SendAsync(
        HardwareCommandEnvelope command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.HardwareTx(command.CommandName, command.Frame);
        logger.HardwareDecision(
            $"模拟确认：id={command.CommandId}, seq={command.Sequence}。当前尚未接入真实链路，不代表硬件 ACK。");
        return Task.FromResult(new HardwareLinkReply(
            command.CommandId,
            HardwareAcknowledgement.Simulated,
            Message: "Log-only transport"));
    }
}
