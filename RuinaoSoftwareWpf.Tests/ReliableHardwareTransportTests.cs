namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class ReliableHardwareTransportTests
{
    [Fact]
    public async Task SendFrameAsync_AcceptsMatchingHardwareAck()
    {
        var transport = CreateTransport(HardwareAcknowledgement.Ack);

        await transport.SendFrameAsync(
            "START_CHANNELS",
            [0x01],
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendFrameAsync_RejectsSimulatedReplyAsHardwareConfirmation()
    {
        var transport = CreateTransport(HardwareAcknowledgement.Simulated);

        await Assert.ThrowsAsync<HardwareCommandException>(() => transport.SendFrameAsync(
            "START_CHANNELS",
            [0x01],
            TestContext.Current.CancellationToken));
    }

    private static ReliableHardwareTransport CreateTransport(HardwareAcknowledgement acknowledgement) =>
        new(new ReplyingHardwareLink(acknowledgement), new NullLoggingService(), new NullRuntimeTelemetryService());

    private sealed class ReplyingHardwareLink(HardwareAcknowledgement acknowledgement) : IHardwareLink
    {
        public bool IsConnected => true;
        public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<HardwareLinkReply> SendAsync(
            HardwareCommandEnvelope command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareLinkReply(command.CommandId, acknowledgement));
    }

    private sealed class NullLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }

    private sealed class NullRuntimeTelemetryService : IRuntimeTelemetryService
    {
        public RuntimeTelemetrySnapshot GetSnapshot() => new(
            DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public void SetEegQueue(int depth, int capacity) { }
        public void RecordEegQueueDelay(TimeSpan delay) { }
        public void RecordEegRejectedBatch() { }
        public void RecordDatabaseCommitDelay(TimeSpan delay) { }
        public void RecordDiskWrite(long bytes, TimeSpan elapsed) { }
        public void RecordPacketLoss(long count = 1) { }
        public void RecordUiFrame(TimeSpan frameTime) { }
    }
}
