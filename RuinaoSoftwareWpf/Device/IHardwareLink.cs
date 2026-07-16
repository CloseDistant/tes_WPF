namespace RuinaoSoftwareWpf;

public enum HardwareAcknowledgement
{
    Ack,
    Nak,
    Simulated,
    Disconnected
}

public sealed record HardwareCommandEnvelope(
    Guid CommandId,
    long Sequence,
    string CommandName,
    byte[] Frame,
    int Attempt,
    bool IsIdempotent);

public sealed record HardwareLinkReply(
    Guid CommandId,
    HardwareAcknowledgement Acknowledgement,
    byte[]? Payload = null,
    string? Message = null);

public interface IHardwareLink
{
    bool IsConnected { get; }

    Task ReconnectAsync(CancellationToken cancellationToken = default);

    Task<HardwareLinkReply> SendAsync(
        HardwareCommandEnvelope command,
        CancellationToken cancellationToken = default);
}
