namespace RuinaoSoftwareWpf;

using System.Collections.Concurrent;
using System.IO;

public sealed class ReliableHardwareTransport : IHardwareTransport
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private const int IdempotentMaxAttempts = 3;

    private readonly IHardwareLink link;
    private readonly ILoggingService logger;
    private readonly IRuntimeTelemetryService telemetry;
    private readonly ConcurrentDictionary<Guid, HardwareLinkReply> completedCommands = new();
    private long sequence;

    public ReliableHardwareTransport(
        IHardwareLink link,
        ILoggingService logger,
        IRuntimeTelemetryService telemetry)
    {
        this.link = link;
        this.logger = logger;
        this.telemetry = telemetry;
    }

    public async Task SendFrameAsync(string commandName, byte[] frame, CancellationToken cancellationToken = default)
    {
        var commandId = Guid.NewGuid();
        if (completedCommands.ContainsKey(commandId))
        {
            return;
        }

        var isIdempotent = IsIdempotent(commandName);
        var maxAttempts = isIdempotent ? IdempotentMaxAttempts : 1;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!link.IsConnected)
                {
                    await link.ReconnectAsync(cancellationToken);
                }

                var envelope = new HardwareCommandEnvelope(
                    commandId,
                    Interlocked.Increment(ref sequence),
                    commandName,
                    frame.ToArray(),
                    attempt,
                    isIdempotent);
                var reply = await link.SendAsync(envelope, cancellationToken)
                    .WaitAsync(DefaultTimeout, cancellationToken);

                if (reply.CommandId != commandId)
                {
                    telemetry.RecordPacketLoss();
                    throw new InvalidDataException(
                        $"硬件回复乱序或关联错误：expected={commandId}, actual={reply.CommandId}。");
                }

                if (reply.Acknowledgement == HardwareAcknowledgement.Ack)
                {
                    completedCommands.TryAdd(commandId, reply);
                    TrimCompletedCommands();
                    logger.Hardware(
                        $"命令完成：id={commandId}, seq={envelope.Sequence}, command={commandName}, ack={reply.Acknowledgement}, attempt={attempt}");
                    return;
                }

                if (reply.Acknowledgement == HardwareAcknowledgement.Simulated)
                {
                    throw new HardwareCommandException(
                        commandId,
                        commandName,
                        new InvalidOperationException("模拟回复不属于有效硬件 ACK。"));
                }

                if (reply.Acknowledgement == HardwareAcknowledgement.Nak)
                {
                    throw new HardwareNakException(commandId, commandName, reply.Message);
                }

                throw new IOException(reply.Message ?? "硬件链路已断开。");
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or HardwareNakException)
            {
                lastException = exception;
                telemetry.RecordPacketLoss();
                logger.Warning(
                    $"硬件命令失败：id={commandId}, command={commandName}, attempt={attempt}/{maxAttempts}, reason={exception.Message}");
                if (attempt < maxAttempts)
                {
                    await link.ReconnectAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
            }
        }

        throw new HardwareCommandException(commandId, commandName, lastException);
    }

    private static bool IsIdempotent(string commandName)
    {
        return commandName.Contains("READ", StringComparison.Ordinal)
            || commandName.Contains("HANDSHAKE", StringComparison.Ordinal)
            || commandName.Contains("STOP", StringComparison.Ordinal)
            || commandName.Contains("DISCONNECT", StringComparison.Ordinal);
    }

    private void TrimCompletedCommands()
    {
        if (completedCommands.Count <= 1024)
        {
            return;
        }

        foreach (var commandId in completedCommands.Keys.Take(completedCommands.Count - 512))
        {
            completedCommands.TryRemove(commandId, out _);
        }
    }
}

public sealed class HardwareNakException : Exception
{
    public HardwareNakException(Guid commandId, string commandName, string? message)
        : base($"硬件返回 NAK：id={commandId}, command={commandName}, message={message ?? "未提供"}")
    {
    }
}

public sealed class HardwareCommandException : Exception
{
    public HardwareCommandException(Guid commandId, string commandName, Exception? innerException)
        : base($"硬件命令执行失败：id={commandId}, command={commandName}", innerException)
    {
    }
}
